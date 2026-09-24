// Serves app/index.html and the /api it talks to. The page does all the maths;
// this only stores and returns rows, and enforces who may change what.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const pg = require('pg');

pg.types.setTypeParser(1082, v => v);            // date stays 'YYYY-MM-DD', as the page expects
const pool = new pg.Pool({ connectionString: process.env.DATABASE_URL });
pool.on('error', console.error);
const PAGE = fs.readFileSync(path.join(__dirname, 'index.html'));
// ponytail: both roles may do everything for now. Take 'standard' out to make the
// ADM routes below admin-only again (and the same in canEdit() in index.html).
const ADMIN = ['admin', 'standard'];

const camel = row => Object.fromEntries(Object.entries(row).map(([k, v]) =>
  [k.replace(/_(.)/g, (_, c) => c.toUpperCase()), v]));
const q = async (sql, args, db = pool) => (await db.query(sql, args)).rows.map(camel);

async function tx(fn) {
  const c = await pool.connect();
  try { await c.query('begin'); const r = await fn(c); await c.query('commit'); return r; }
  catch (e) { await c.query('rollback'); throw e; }
  finally { c.release(); }
}

class HttpError extends Error { constructor(status, msg) { super(msg); this.status = status; } }
const fail = (status, msg) => { throw new HttpError(status, msg); };
const text = v => v == null ? '' : String(v);
const need = (v, what) => text(v).trim() || fail(400, what + ' is required');
const list = v => Array.isArray(v) && v.every(x => typeof x === 'string') ? v : fail(400, 'Expected a list of ids');
const found = (rows, msg) => rows.length ? rows : fail(409, msg);

/* Who is asking. Signed in through oauth2-proxy (the auth service), which has
   already checked the Microsoft sign-in and the @columbusglobal.com rule, and
   is the only way to reach this server: the e-mail is its X-Forwarded-Email
   header, and the display name comes from the ID token it forwards. The token
   is only read, not verified - the proxy did that.
   DEV_USER_EMAIL (docker-compose.dev.yml only) skips the proxy for local work. */
const DEV_EMAIL = process.env.DEV_USER_EMAIL || '';
const ADMIN_EMAILS = (process.env.ADMIN_EMAILS || '').toLowerCase().split(',').map(s => s.trim()).filter(Boolean);

function tokenClaims(req) {
  try { return JSON.parse(Buffer.from((req.headers.authorization || '').split('.')[1], 'base64url')); }
  catch { return {}; }
}

// The first sign-in creates the Columbus profile: Standard, or Admin for
// ADMIN_EMAILS. A profile an admin added beforehand with the same e-mail is used as it is.
async function whoami(req) {
  const email = (DEV_EMAIL || text(req.headers['x-forwarded-email'])).trim();
  if (!email) fail(401, 'Not signed in');
  const find = () => q("select id, role from columbus_profiles where email <> '' and lower(email) = lower($1)", [email]);
  let [u] = await find();
  if (!u) {
    await q(`insert into columbus_profiles (name, email, role) values ($1, $2, $3)
             on conflict (lower(email)) where email <> '' do nothing`,
            [tokenClaims(req).name || email.split('@')[0], email,
             ADMIN_EMAILS.includes(email.toLowerCase()) ? 'admin' : 'standard']);
    [u] = await find();
  }
  return { userId: u.id, role: u.role, email, dev: !!DEV_EMAIL };
}

async function state(me) {
  const [[{ today }], customers, contacts, boards, domains, msProfiles, columbusProfiles, relations] = await Promise.all([
    q('select current_date as today'),
    q('select * from customers order by seq'),
    q('select * from contacts order by seq'),
    q('select * from boards order by seq'),
    q('select * from domains order by system, seq'),
    // No domain row means Unmarked - so deleting a domain needs no fallback step.
    q(`select p.*,
         coalesce((select array_agg(x.domain_id order by d.seq) from profile_domains x
                   join domains d on d.id = x.domain_id where x.ms_profile_id = p.id), '{d0}'::text[]) as domain_ids,
         coalesce((select array_agg(x.customer_id order by c.seq) from profile_customers x
                   join customers c on c.id = x.customer_id where x.ms_profile_id = p.id), '{}'::text[]) as customer_ids
       from ms_profiles p order by p.seq`),
    q('select * from columbus_profiles order by seq'),
    q('select * from relations order by seq')
  ]);
  return { today, me, customers, contacts, boards, domains, msProfiles, columbusProfiles, relations };
}

async function saveMs(c, id, b) {
  const cols = [need(b.name, 'A name'), text(b.title), text(b.email), text(b.phone), text(b.group),
                text(b.source), text(b.notes), b.cadence || 'none', b.ownerId || null];
  if (id) found(await q(`update ms_profiles set name = $2, title = $3, email = $4, phone = $5, "group" = $6,
      source = $7, notes = $8, cadence = $9, owner_id = $10, updated_at = current_date
      where id = $1 returning id`, [id, ...cols], c), 'That profile no longer exists');
  else [{ id }] = await q(`insert into ms_profiles (name, title, email, phone, "group", source, notes, cadence, owner_id)
      values ($1, $2, $3, $4, $5, $6, $7, $8, $9) returning id`, cols, c);
  await c.query('delete from profile_domains where ms_profile_id = $1', [id]);
  await c.query('insert into profile_domains select $1, unnest($2::text[])', [id, list(b.domainIds)]);
  await c.query('delete from profile_customers where ms_profile_id = $1', [id]);
  await c.query('insert into profile_customers select $1, unnest($2::text[])', [id, list(b.customerIds)]);
}

const ID = '([^/]+)';
const ADM = { admin: true }, ANY = {};
const routes = [
  ['GET', '/api/state', {}, me => state(me)],

  ['POST', '/api/boards', ADM, (me, b) => q(`insert into boards (id, label, dashboard, graphics, subtitle, owner, version, updated, system)
      values ($1, $2, $3, $4, $5, $6, $7, $8, not exists (select 1 from boards))`,
      [need(b.id, 'An id'), need(b.label, 'A dashboard name'), need(b.dashboard, 'A route'), need(b.graphics, 'A route'),
       text(b.subtitle), text(b.owner), text(b.version), text(b.updated)])],
  ['PUT', '/api/boards/' + ID, ADM, (me, b, id) => q(`update boards set label = $2, subtitle = $3, owner = $4, version = $5,
      updated = $6 where id = $1 returning id`,
      [id, need(b.label, 'A dashboard name'), text(b.subtitle), text(b.owner), text(b.version), text(b.updated)])
      .then(r => found(r, 'That dashboard no longer exists'))],
  ['DELETE', '/api/boards/' + ID, ADM, (me, b, id) => q('delete from boards where id = $1 and not system returning id', [id])
      .then(r => found(r, 'The landing dashboard cannot be deleted'))],

  ['POST', '/api/domains', ADM, (me, b) => q('insert into domains (dept, name, owner, "desc") values ($1, $2, $3, $4)',
      [need(b.dept, 'A dashboard'), need(b.name, 'A domain name'), text(b.owner), text(b.desc)])],
  // A hand-tuned panel only fits the board it was tuned for, and shows the real
  // name once the domain is renamed.
  ['PUT', '/api/domains/' + ID, ADM, (me, b, id) => q(`update domains set
      panel = case when dept is distinct from $2 then null
                   when name <> $3 then panel - 'title' - 'subtitle' else panel end,
      dept = $2, name = $3, owner = $4, "desc" = $5
      where id = $1 and not system returning id`,
      [id, need(b.dept, 'A dashboard'), need(b.name, 'A domain name'), text(b.owner), text(b.desc)])
      .then(r => found(r, 'The Unmarked domain cannot be edited'))],
  ['DELETE', '/api/domains/' + ID, ADM, (me, b, id) => q('delete from domains where id = $1 and not system returning id', [id])
      .then(r => found(r, 'The Unmarked domain cannot be deleted'))],

  ['POST', '/api/ms-profiles', ADM, (me, b) => tx(c => saveMs(c, null, b))],
  ['PUT', '/api/ms-profiles/' + ID, ADM, (me, b, id) => tx(c => saveMs(c, id, b))],
  ['DELETE', '/api/ms-profiles/' + ID, ADM, (me, b, id) => q('delete from ms_profiles where id = $1', [id])],

  ['POST', '/api/columbus-profiles', ADM, (me, b) => q(`insert into columbus_profiles (name, title, email, department, role, skills)
      values ($1, $2, $3, $4, $5, $6)`,
      [need(b.name, 'A name'), text(b.title), text(b.email), text(b.department), b.role || 'standard', list(b.skills || [])])],
  ['PATCH', '/api/columbus-profiles/' + ID, ADM, (me, b, id) => q('update columbus_profiles set role = $2 where id = $1 returning id',
      [id, b.role]).then(r => found(r, 'That user no longer exists'))],
  ['DELETE', '/api/columbus-profiles/' + ID, ADM, (me, b, id) => q('delete from columbus_profiles where id = $1', [id])],

  /* Item by item, never "replace all": callers send only what changed. An upsert,
     not delete + insert - deleting the owner's relation would drop the ownership. */
  ['PUT', '/api/my-relations', ANY, (me, items) => tx(async c => {
    if (!Array.isArray(items)) fail(400, 'Expected a list of relations');
    for (const d of items) {
      if (d.removed) await c.query('delete from relations where columbus_id = $1 and ms_profile_id = $2', [me.userId, d.msProfileId]);
      else await c.query(`insert into relations (columbus_id, ms_profile_id, score, note) values ($1, $2, $3, $4)
          on conflict (columbus_id, ms_profile_id) do update
          set score = excluded.score, note = excluded.note, updated_at = current_date`,
          [me.userId, d.msProfileId, d.score, text(d.note)]);
    }
    await c.query('update ms_profiles set updated_at = current_date where id = any($1)', [items.map(d => d.msProfileId)]);
  })],
  ['POST', '/api/contacts', ANY, (me, b) => q('insert into contacts (ms_profile_id, by_id) values ($1, $2)',
      [need(b.msProfileId, 'A profile'), me.userId])]
].map(([method, p, opts, fn]) => ({ method, re: new RegExp('^' + p + '$'), opts, fn }));

async function body(req) {
  let raw = '';
  for await (const chunk of req) { raw += chunk; if (raw.length > 1e6) fail(413, 'Request too large'); }
  if (!raw) return {};
  try { return JSON.parse(raw) || {}; } catch { fail(400, 'Invalid JSON'); }
}

/* Schema changes are numbered files in migrations/ (001_schema.sql, 002_...),
   each applied once, in name order, in its own transaction, and recorded in
   schema_migrations. A failed one rolls back and the server does not start. */
// ponytail: no lock, one app container; take pg_advisory_lock first if it ever runs as several.
async function migrate() {
  await pool.query(`create table if not exists schema_migrations (
    name text primary key, applied_at timestamptz not null default now())`);
  const done = new Set((await pool.query('select name from schema_migrations')).rows.map(r => r.name));
  const dir = path.join(__dirname, 'migrations');
  for (const f of fs.readdirSync(dir).filter(f => f.endsWith('.sql')).sort()) {
    if (done.has(f)) continue;
    await tx(async c => {
      await c.query(fs.readFileSync(path.join(dir, f), 'utf8'));
      await c.query('insert into schema_migrations (name) values ($1)', [f]);
    });
    console.log('applied migration', f);
  }
}

const PG_STATUS = { '23505': 409, '23503': 409, '23514': 400, '23502': 400, '22P02': 400 };

const server = http.createServer(async (req, res) => {
  const send = (status, data, type = 'application/json') => {
    res.writeHead(status, { 'content-type': type });
    res.end(type === 'application/json' ? JSON.stringify(data) : data);
  };
  try {
    const url = new URL(req.url, 'http://localhost');
    if (req.method === 'GET' && url.pathname === '/') return send(200, PAGE, 'text/html; charset=utf-8');
    const route = routes.find(r => r.method === req.method && r.re.test(url.pathname));
    if (!route) fail(404, 'Not found');
    // A cross-site form can post text/plain but never JSON, so writes must be JSON.
    if (req.method !== 'GET' && !/^application\/json/.test(req.headers['content-type'] || '')) fail(415, 'Send JSON');
    const me = await whoami(req);
    if (route.opts.admin && ADMIN.indexOf(me.role) === -1) fail(403, 'Only Admin can change this');
    const id = (url.pathname.match(route.re)[1] || '');
    const out = await route.fn(me, await body(req), decodeURIComponent(id));
    send(200, out && !Array.isArray(out) ? out : { ok: true });
  } catch (e) {
    const status = e.status || PG_STATUS[e.code] || 500;
    if (status === 500) console.error(e);
    send(status, { error: status === 500 ? 'Something went wrong' : e.code === '23514' ? e.message : e.detail || e.message });
  }
});

migrate().then(
  () => server.listen(process.env.PORT || 8080, () => console.log('listening on', process.env.PORT || 8080)),
  e => { console.error('migration failed, not starting:', e.message); process.exit(1); });
