-- A read-only login for looking at the data with an SQL tool, such as the
-- PostgreSQL extension for VS Code (see the README). For local development:
-- run it by hand, like the seed; running it again changes nothing.
--   docker compose exec -T db psql -U app -d app -v ON_ERROR_STOP=1 < db/reader.sql
do $$ begin
  if not exists (select 1 from pg_roles where rolname = 'reader') then
    create role reader login password 'reader';
  end if;
end $$;
grant connect on database app to reader;
grant usage on schema public to reader;
grant select on all tables in schema public to reader;
-- tables that later migrations add are readable too
alter default privileges for role app in schema public grant select on tables to reader;
