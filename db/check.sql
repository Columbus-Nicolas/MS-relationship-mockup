-- The rules the database enforces for the app, and history with undo. Changes nothing: it builds its
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

-- History and undo. Each step is its own change, marked with a negative
-- app.change so it can never match a real one.
do $$
begin
  perform set_config('app.change', '-10', true);
  insert into ms_profiles (id, name) values ('t-h', 'Hist');
  insert into profile_domains values ('t-h', 't-d2');
  insert into relations (columbus_id, ms_profile_id, score) values ('t-c2', 't-h', 1);

  perform set_config('app.change', '-11', true);
  delete from ms_profiles where id = 't-h';                  -- takes its relation and domain link along
  assert (select count(*) from history where change = -11) = 3, 'FAIL: a delete and its cascades are not one change';

  perform set_config('app.change', '-12', true);
  perform undo_change(-11);
  assert exists (select 1 from ms_profiles where id = 't-h'), 'FAIL: undo did not bring the profile back';
  assert exists (select 1 from relations where ms_profile_id = 't-h'), 'FAIL: undo did not bring the relation back';
  assert exists (select 1 from profile_domains where ms_profile_id = 't-h'), 'FAIL: undo did not bring the domain link back';
  assert (select count(*) from history where undoes = -11) = 3, 'FAIL: the undo is not marked as undoing the change';

  perform set_config('app.change', '-13', true);
  begin
    perform undo_change(-11);
    raise exception 'FAIL: the same change was undone twice';
  exception when raise_exception then
    if sqlerrm not like '%already been undone%' then raise; end if;
  end;

  perform set_config('app.change', '-14', true);
  update ms_profiles set title = 'First' where id = 't-h';
  perform set_config('app.change', '-15', true);
  update ms_profiles set title = 'Second' where id = 't-h';
  perform set_config('app.change', '-16', true);
  begin
    perform undo_change(-14);
    raise exception 'FAIL: undid a change whose row changed since';
  exception when raise_exception then
    if sqlerrm not like '%changed since%' then raise; end if;
  end;
  perform undo_change(-15);                                  -- the latest one is fine
  assert (select title from ms_profiles where id = 't-h') = 'First', 'FAIL: undo did not put the title back';

  perform set_config('app.change', '-17', true);
  insert into ms_profiles (id, name) values ('t-h2', 'Hist 2');
  perform set_config('app.change', '-18', true);
  insert into relations (columbus_id, ms_profile_id, score) values ('t-c2', 't-h2', 2);
  perform set_config('app.change', '-19', true);
  begin
    perform undo_change(-17);                                -- would take the later relation with it
    raise exception 'FAIL: undid a change that a later one depends on';
  exception when raise_exception then
    if sqlerrm not like '%depend%' then raise; end if;
  end;
  assert exists (select 1 from relations where ms_profile_id = 't-h2'), 'FAIL: the later relation was lost';

  raise notice 'check.sql: history and undo hold';
end $$;

rollback;
