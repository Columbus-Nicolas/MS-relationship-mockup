-- Migration 003: a history of every change, and undo.
--
-- Every insert, update and delete on the data tables writes one history line
-- with the row before and after, including the rows a cascade touches. The
-- lines of one user action share `change` (the transaction, unless
-- app.change says otherwise); `actor` is the signed-in e-mail the API sets in
-- app.actor, or 'database' for anything done directly (migrations, seed).

create table history (
  id     bigint generated always as identity primary key,
  change bigint not null default coalesce(nullif(current_setting('app.change', true), '')::bigint, txid_current()),
  at     timestamptz not null default now(),
  actor  text not null default coalesce(nullif(current_setting('app.actor', true), ''), 'database'),
  tbl    text not null,
  op     text not null check (op in ('insert', 'update', 'delete', 'restore')),
  old    jsonb,
  new    jsonb,
  undoes bigint                      -- the change this line reverses
);
create index history_change on history (change);
create index history_undoes on history (undoes) where undoes is not null;

create function record_history() returns trigger language plpgsql as $$
begin
  insert into history (tbl, op, old, new)
  values (tg_table_name, lower(tg_op),
          case when tg_op <> 'INSERT' then to_jsonb(old) end,
          case when tg_op <> 'DELETE' then to_jsonb(new) end);
  return null;
end $$;

do $$
declare t text;
begin
  foreach t in array array['boards', 'domains', 'customers', 'columbus_profiles', 'ms_profiles',
                           'relations', 'profile_domains', 'profile_customers', 'contacts'] loop
    execute format('create trigger history_write after insert or delete on %I
                    for each row execute function record_history()', t);
    execute format('create trigger history_update after update on %I
                    for each row when (old.* is distinct from new.*) execute function record_history()', t);
  end loop;
end $$;

-- An undo puts rows back in whatever order they come; the foreign keys are
-- then checked once, at the end of it. Outside an undo nothing changes.
do $$
declare c record;
begin
  for c in select conrelid::regclass as tbl, conname from pg_constraint
           where contype = 'f' and connamespace = 'public'::regnamespace loop
    execute format('alter table %s alter constraint %I deferrable initially immediate', c.tbl, c.conname);
  end loop;
end $$;

/* Undo one change - everything one request did - newest line first. Each row
   must still be exactly as that change left it, and the undo may not touch
   anything else (a cascade onto rows added later); otherwise it raises and
   nothing happens. A row is found by its id, or by all its columns in the two
   link tables. Only the columns a line recorded are written back, so a column
   added by a later migration keeps its default. */
create function undo_change(target bigint) returns void language plpgsql as $$
declare
  me   bigint := coalesce(nullif(current_setting('app.change', true), '')::bigint, txid_current());
  h    record;
  k    jsonb;
  cur  jsonb;
  cols text;
  n    int := 0;
  made int;
begin
  if target = me then raise exception 'A change cannot undo itself'; end if;
  if not exists (select 1 from history where change = target) then raise exception 'There is no change %', target; end if;
  if exists (select 1 from history where change = target and op = 'restore') then
    raise exception 'A backup restore cannot be undone here - restore the backup taken just before it';
  end if;
  if exists (select 1 from history where undoes = target) then raise exception 'This change has already been undone'; end if;

  set constraints all deferred;
  for h in select * from history where change = target order by id desc loop
    k := case when coalesce(h.new, h.old) ? 'id' then jsonb_build_object('id', coalesce(h.new, h.old) -> 'id')
              else coalesce(h.new, h.old) end;
    execute format('select to_jsonb(t) from %I t where to_jsonb(t) @> $1', h.tbl) into cur using k;
    if (h.op = 'delete' and cur is not null)
       or (h.op <> 'delete' and (cur is null
           or (select jsonb_object_agg(c, cur -> c) from jsonb_object_keys(h.new) c) <> h.new)) then
      raise exception '% changed since - undo the later change first', replace(h.tbl, '_', ' ');
    end if;

    select string_agg(quote_ident(column_name), ', ') into cols from information_schema.columns
     where table_schema = 'public' and table_name = h.tbl
       and (h.op = 'delete' or is_identity = 'NO') and coalesce(h.old, h.new) ? column_name;
    if h.op = 'insert' then
      execute format('delete from %I t where to_jsonb(t) @> $1', h.tbl) using k;
    elsif h.op = 'delete' then
      execute format('insert into %I (%s) overriding system value select %s from jsonb_populate_record(null::%I, $1)',
                     h.tbl, cols, cols, h.tbl) using h.old;
    else
      execute format('update %I t set (%s) = (select %s from jsonb_populate_record(null::%I, $1)) where to_jsonb(t) @> $2',
                     h.tbl, cols, cols, h.tbl) using h.old, k;
    end if;
    n := n + 1;
  end loop;
  set constraints all immediate;     -- a row put back that points at something gone fails here

  -- Each step above wrote exactly one history line; any more came from a cascade.
  select count(*) into made from history where change = me and undoes is null;
  if made <> n then raise exception 'Later changes depend on this one - undo those first'; end if;
  update history set undoes = target where change = me and undoes is null;
end $$;
