// Serves app/index.html and the /api it talks to. The page does all the maths;
// this only stores and returns rows, and enforces who may change what.
const http = require('node:http');
const fs = require('node:fs');
const path = require('node:path');
const { AsyncLocalStorage } = require('node:async_hooks');
const { execFile } = require('node:child_process');
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
/* A write request runs in one transaction (see the dispatcher): its connection
   rides along in `request`, so q() and tx() inside it join that transaction -
   which is what makes one request one change in the history. */
const request = new AsyncLocalStorage();
const q = async (sql, args) => (await (request.getStore() || pool).query(sql, args)).rows.map(camel);

async function tx(fn) {
  if (request.getStore()) return fn(request.getStore());
  const c = await pool.connect();
  try { await c.query('begin'); const r = await request.run(c, () => fn(c)); await c.query('commit'); return r; }
  catch (e) { await c.query('rollback').catch(() => {}); throw e; }
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

// Someone without a Columbus profile yet gets userId null and fills in the
// account page first (POST /api/account); `name` pre-fills it. A profile an
// admin added beforehand with the same e-mail is used as it is.
async function whoami(req) {
  const email = (DEV_EMAIL || text(req.headers['x-forwarded-email'])).trim();
  if (!email) fail(401, 'Not signed in');
  const [u] = await q("select id, role from columbus_profiles where email <> '' and lower(email) = lower($1)", [email]);
  return { userId: u ? u.id : null, role: u ? u.role : null, email, name: tokenClaims(req).name || '', dev: !!DEV_EMAIL };
}

async function state(me) {
  const [[{ today, version }], customers, contacts, boards, domains, msProfiles, columbusProfiles, relations] = await Promise.all([
    q('select current_date as today, (select coalesce(max(id), 0) from history) as version'),
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
  return { today, version, me, customers, contacts, boards, domains, msProfiles, columbusProfiles, relations };
}

async function saveMs(c, id, b) {
  const cols = [need(b.name, 'A name'), text(b.title), text(b.email), text(b.phone), text(b.group),
                text(b.source), text(b.notes), b.cadence || 'none', b.ownerId || null];
  if (id) found(await q(`update ms_profiles set name = $2, title = $3, email = $4, phone = $5, "group" = $6,
      source = $7, notes = $8, cadence = $9, owner_id = $10, updated_at = current_date
      where id = $1 returning id`, [id, ...cols]), 'That profile no longer exists');
  else [{ id }] = await q(`insert into ms_profiles (name, title, email, phone, "group", source, notes, cadence, owner_id)
      values ($1, $2, $3, $4, $5, $6, $7, $8, $9) returning id`, cols);
  // Only the links that changed, so the history shows what really happened.
  const doms = list(b.domainIds), custs = list(b.customerIds);
  await c.query('delete from profile_domains where ms_profile_id = $1 and domain_id <> all($2::text[])', [id, doms]);
  await c.query('insert into profile_domains select $1, unnest($2::text[]) on conflict do nothing', [id, doms]);
  await c.query('delete from profile_customers where ms_profile_id = $1 and customer_id <> all($2::text[])', [id, custs]);
  await c.query('insert into profile_customers select $1, unnest($2::text[]) on conflict do nothing', [id, custs]);
}

/* The latest 50 changes before a history id, newest first, each with up to 50
   of its lines and the total. */
// ponytail: groups the whole table on every call; page by an index once it holds many thousand changes.
async function history(before) {
  const rows = await q(`with ev as (
      select change, max(id) as last, count(*) as total from history
      group by change having $1::bigint is null or max(id) < $1
      order by last desc limit 50)
    select ev.change, ev.last, ev.total, h.at, h.actor, h.tbl, h.op, h.old, h.new, h.undoes,
           exists (select 1 from history u where u.undoes = ev.change) as undone
      from ev cross join lateral (select * from history x where x.change = ev.change order by id limit 50) h
     order by ev.last desc, h.id`, [before || null]);
  const out = [];
  for (const r of rows) {
    let e = out[out.length - 1];
    if (!e || e.change !== r.change) out.push(e = { change: r.change, last: r.last, total: r.total, at: r.at,
      actor: r.actor, undone: r.undone, undoes: r.undoes, entries: [] });
    e.at = r.at;
    e.entries.push({ tbl: r.tbl, op: r.op, old: r.old, new: r.new });
  }
  return { changes: out };
}

/* Whole-database backups: the dumps in the `backups` volume, which the backup
   service writes every day and the app writes on request. Admin only. */
const DUMPS = '/backups', DUMP_NAME = /^app-[\w-]+\.dump$/;
const stamp = () => new Date().toISOString().slice(0, 19).replace('T', '-').replace(/:/g, '');
function sh(cmd, args) {
  return new Promise((ok, no) => execFile(cmd, args, { maxBuffer: 64 << 20 }, (e, out, err) =>
    e ? no(new HttpError(409, cmd + ' failed: ' + (String(err).trim().split('\n').pop() || e.message))) : ok(out)));
}
function backups() {
  return { backups: fs.readdirSync(DUMPS).filter(f => DUMP_NAME.test(f)).map(f => {
    const st = fs.statSync(path.join(DUMPS, f));
    return { name: f, size: st.size, at: st.mtime.toISOString() };
  }).sort((a, b) => (a.at < b.at ? 1 : -1)) };
}
async function backupNow(kind) {
  const name = 'app-' + kind + '-' + stamp() + '.dump', file = path.join(DUMPS, name);
  await sh('pg_dump', ['-Fc', '-f', file + '.partial', '-d', process.env.DATABASE_URL]);
  fs.renameSync(file + '.partial', file);            // a half-written dump never looks like a backup
  return { name };
}
/* Put the whole database back as a backup held it. Only a backup of this same
   schema - `pg_restore --clean` would leave anything newer half in place. The
   current state is dumped first, so the restore itself can be reversed; the
   restore is one transaction, so it happens completely or not at all. */
async function restoreBackup(name, me) {
  const file = path.join(DUMPS, name);
  if (!DUMP_NAME.test(name) || !fs.existsSync(file)) fail(404, 'There is no backup called ' + name);
  const theirs = (await sh('pg_restore', ['-a', '-t', 'schema_migrations', '-f', '-', file])).match(/^\d{3}_\S+\.sql(?=\t)/gm) || [];
  const ours = (await q('select name from schema_migrations order by name')).map(r => r.name);
  if (theirs.sort().join() !== ours.join())
    fail(409, 'That backup was made by an older version of the app - restore it on the command line (see the README)');
  const before = (await backupNow('before-restore')).name;
  await sh('pg_restore', ['--clean', '--if-exists', '--single-transaction', '--no-owner', '-d', process.env.DATABASE_URL, file]);
  await q(`insert into history (tbl, op, new, actor) values ('backup', 'restore', $1, $2)`, [{ file: name, before }, me.email]);
  return { before };
}

const ID = '([^/]+)';
const ADM = { admin: true }, ONLY_ADMIN = { onlyAdmin: true }, ANY = {}, OPEN = { open: true };
const routes = [
  ['GET', '/api/state', OPEN, me => state(me)],
  // Live updates ask this every 20 s: one indexed lookup, a few bytes back.
  ['GET', '/api/version', OPEN, () => q('select coalesce(max(id), 0) as v from history').then(r => r[0])],
  // The account page: your own profile, with your sign-in e-mail.
  ['POST', '/api/account', OPEN, (me, b) => me.userId ? fail(409, 'You already have an account')
    : q(`insert into columbus_profiles (name, title, department, skills, phone, email, role)
         values ($1, $2, $3, $4, $5, $6, $7)`,
        [need(b.name, 'Your name'), need(b.title, 'Your title'), need(b.department, 'Your department'),
         list(b.skills || []), text(b.phone), me.email,
         ADMIN_EMAILS.includes(me.email.toLowerCase()) ? 'admin' : 'standard'])],
  ['GET', '/api/history', {}, (me, b, id, url) => history(url.searchParams.get('before'))],
  // Outside a request transaction (`raw`): pg_dump and pg_restore are their own sessions.
  ['GET', '/api/backups', ONLY_ADMIN, () => backups()],
  ['POST', '/api/backups', { onlyAdmin: true, raw: true }, () => backupNow('manual')],
  ['POST', '/api/backups/' + ID + '/restore', { onlyAdmin: true, raw: true }, (me, b, id) => restoreBackup(id, me)],
  ['POST', '/api/history/' + ID + '/undo', ONLY_ADMIN, (me, b, id) =>
      /^-?\d+$/.test(id) ? q('select undo_change($1::bigint)', [id]) : fail(400, 'Not a change')],

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

  /* User management. Roles, e-mails (the sign-in identity) and deleting users
     are Admin-only - otherwise anyone could make themselves Admin. */
  ['POST', '/api/columbus-profiles', ADM, (me, b) => (b.role || 'standard') !== 'standard' && me.role !== 'admin'
    ? fail(403, 'Only Admin can give the Admin role')
    : q(`insert into columbus_profiles (name, title, email, department, role, skills, phone)
         values ($1, $2, $3, $4, $5, $6, $7)`,
        [need(b.name, 'A name'), text(b.title), text(b.email), text(b.department), b.role || 'standard',
         list(b.skills || []), text(b.phone)])],
  ['PUT', '/api/columbus-profiles/' + ID, ADM, (me, b, id) => q(`update columbus_profiles set name = $2, title = $3,
      department = $4, skills = $5, phone = $6, email = case when $7 then $8 else email end where id = $1 returning id`,
      [id, need(b.name, 'A name'), text(b.title), text(b.department), list(b.skills || []), text(b.phone),
       me.role === 'admin' && b.email !== undefined, text(b.email)])
      .then(r => found(r, 'That user no longer exists'))],
  ['PATCH', '/api/columbus-profiles/' + ID, ONLY_ADMIN, (me, b, id) => q('update columbus_profiles set role = $2 where id = $1 returning id',
      [id, b.role]).then(r => found(r, 'That user no longer exists'))],
  ['DELETE', '/api/columbus-profiles/' + ID, ONLY_ADMIN, (me, b, id) => q('delete from columbus_profiles where id = $1', [id])],

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

const PG_STATUS = { '23505': 409, '23503': 409, '23514': 400, '23502': 400, '22P02': 400, 'P0001': 409 };

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
    if (!route.opts.open && !me.userId) fail(403, 'Create your account first');
    if (route.opts.admin && ADMIN.indexOf(me.role) === -1) fail(403, 'Only Admin can change this');
    if (route.opts.onlyAdmin && me.role !== 'admin') fail(403, 'Only Admin can do this');
    const id = decodeURIComponent(url.pathname.match(route.re)[1] || ''), b = await body(req);
    const run = () => route.fn(me, b, id, url);
    // A write is one transaction, and the history records who made it.
    const out = req.method === 'GET' || route.opts.raw ? await run()
      : await tx(async c => { await c.query("select set_config('app.actor', $1, true)", [me.email]); return run(); });
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
