-- The rules the database enforces for the app. Changes nothing: it builds its
-- own fixtures inside a transaction and rolls it back. Works empty or seeded.
--   docker compose exec -T db psql -U app -d app -v ON_ERROR_STOP=1 < db/check.sql
begin;

insert into boards (id, label, dashboard, graphics) values ('t-board', 'T', 't-dash', 't-gfx');
insert into domains (id, dept, name) values ('t-d1', 't-board', 'T1'), ('t-d2', 't-board', 'T2');
insert into columbus_profiles (id, name) values ('t-c1', 'Owner'), ('t-c2', 'Other');
insert into ms_profiles (id, name) values ('t-m1', 'Sole'), ('t-m2', 'Both');
insert into profile_domains values ('t-m1', 't-d1'), ('t-m2', 't-d1'), ('t-m2', 't-d2');
insert into relations (columbus_id, ms_profile_id, score) values ('t-c1', 't-m1', 2), ('t-c2', 't-m1', 1), ('t-c1', 't-m2', 3);
update ms_profiles set owner_id = 't-c1' where id in ('t-m1', 't-m2');
insert into contacts (ms_profile_id, by_id) values ('t-m1', 't-c1');

do $$
begin
  begin
    delete from boards where id = 't-board';
    raise exception 'FAIL: deleted a board that still has domains';
  exception when foreign_key_violation then null;
  end;

  begin
    insert into relations (columbus_id, ms_profile_id, score) values ('t-c2', 't-m2', 4);
    raise exception 'FAIL: stored a score of 4';
  exception when check_violation then null;
  end;

  begin
    update ms_profiles set owner_id = 't-c2' where id = 't-m2';
    raise exception 'FAIL: made someone owner without a relation';
  exception when foreign_key_violation then null;
  end;

  -- Re-scoring is the upsert the API uses; it must not drop the ownership.
  insert into relations (columbus_id, ms_profile_id, score) values ('t-c1', 't-m1', -1)
    on conflict (columbus_id, ms_profile_id) do update set score = excluded.score;
  assert (select owner_id from ms_profiles where id = 't-m1') = 't-c1', 'FAIL: re-scoring dropped the owner';

  -- Deleting a domain: a profile left with none reads back as Unmarked; the other keeps its second one.
  delete from domains where id = 't-d1';
  assert not exists (select 1 from profile_domains where ms_profile_id = 't-m1'), 'FAIL: Sole still has a domain';
  assert (select array_agg(domain_id) from profile_domains where ms_profile_id = 't-m2') = '{t-d2}', 'FAIL: Both lost its other domain';

  delete from relations where columbus_id = 't-c1' and ms_profile_id = 't-m2';
  assert (select owner_id from ms_profiles where id = 't-m2') is null, 'FAIL: owner kept after their relation went';

  -- Deleting a user: relations go, what they owned loses its owner, the contact log stays.
  delete from columbus_profiles where id = 't-c1';
  assert not exists (select 1 from relations where columbus_id = 't-c1'), 'FAIL: relations survived their user';
  assert (select owner_id from ms_profiles where id = 't-m1') is null, 'FAIL: owner kept after the user went';
  assert exists (select 1 from contacts where ms_profile_id = 't-m1' and by_id is null), 'FAIL: contact log went with the user';

  -- Deleting a profile takes its relations and contacts with it.
  delete from ms_profiles where id = 't-m1';
  assert not exists (select 1 from relations where ms_profile_id = 't-m1'), 'FAIL: relations survived their profile';
  assert not exists (select 1 from contacts where ms_profile_id = 't-m1'), 'FAIL: contacts survived their profile';

  raise notice 'check.sql: all rules hold';
end $$;

rollback;
