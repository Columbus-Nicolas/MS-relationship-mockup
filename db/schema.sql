-- Runs once, on the first start of an empty database volume.
-- Text ids so the mockup's seed ids (m1, c1, d0, data-ai) survive; `seq` keeps
-- insertion order, which the app relies on (boards[0] is the landing board, the
-- people on a panel follow the profile order).

create table boards (
  seq       int generated always as identity,
  id        text primary key,
  label     text not null,
  dashboard text not null unique,
  graphics  text not null unique,
  subtitle  text not null default '',
  updated   text not null default '',
  version   text not null default '',
  owner     text not null default '',
  system    boolean not null default false
);

create table domains (
  seq    int generated always as identity,
  id     text primary key default gen_random_uuid()::text,
  dept   text references boards (id),          -- no action: a board with domains cannot be deleted
  name   text not null,
  owner  text not null default '',
  "desc" text not null default '',
  system boolean not null default false,
  panel  jsonb                                 -- hand-tuned board layout; null = next free slot
);

-- App structure, not data: where profiles without a domain land. Never on a board.
insert into domains (id, dept, name, owner, "desc", system) values
  ('d0', null, 'Unmarked', '—', 'Microsoft profiles that are not placed in a domain yet, plus everyone left behind when a domain is deleted.', true);

create table customers (
  seq  int generated always as identity,
  id   text primary key default gen_random_uuid()::text,
  name text not null,
  type text not null default 'unknown'
);

create table columbus_profiles (
  seq        int generated always as identity,
  id         text primary key default gen_random_uuid()::text,
  name       text not null,
  title      text not null default '',
  department text not null default '',
  skills     text[] not null default '{}',
  role       text not null default 'standard' check (role in ('superadmin', 'admin', 'moderator', 'standard')),
  email      text not null default '',
  phone      text not null default '',
  from_deck  boolean not null default false
);
create unique index columbus_profiles_email on columbus_profiles (lower(email)) where email <> '';

create table ms_profiles (
  seq        int generated always as identity,
  id         text primary key default gen_random_uuid()::text,
  name       text not null,
  title      text not null default '',
  "group"    text not null default '',
  source     text not null default '',
  email      text not null default '',
  phone      text not null default '',
  notes      text not null default '',
  tentative  boolean not null default false,
  cadence    text not null default 'none' check (cadence in ('none', 'monthly', 'quarterly', 'half', 'yearly')),
  owner_id   text,
  updated_at date not null default current_date
);

create table relations (
  seq           int generated always as identity,
  id            text primary key default gen_random_uuid()::text,
  columbus_id   text not null references columbus_profiles (id) on delete cascade,
  ms_profile_id text not null references ms_profiles (id) on delete cascade,
  score         smallint not null check (score between -3 and 3),
  note          text not null default '',
  updated_at    date not null default current_date,
  unique (columbus_id, ms_profile_id)
);

-- An owner must hold a relation to the person, and stops owning them when that
-- relation (or the user) goes.
alter table ms_profiles add foreign key (owner_id, id)
  references relations (columbus_id, ms_profile_id) on delete set null (owner_id);

create table profile_domains (
  ms_profile_id text references ms_profiles (id) on delete cascade,
  domain_id     text references domains (id) on delete cascade,
  primary key (ms_profile_id, domain_id)
);

create table profile_customers (
  ms_profile_id text references ms_profiles (id) on delete cascade,
  customer_id   text references customers (id) on delete cascade,
  primary key (ms_profile_id, customer_id)
);

-- The contact log outlives the person who wrote a line: by_id goes null, shown as "unknown".
create table contacts (
  seq           int generated always as identity,
  id            text primary key default gen_random_uuid()::text,
  ms_profile_id text not null references ms_profiles (id) on delete cascade,
  by_id         text references columbus_profiles (id) on delete set null,
  date          date not null default current_date
);
