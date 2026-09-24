-- Migration 004: remove the Columbus people the mockup's data brought along -
-- all 26 seeded users (ids c1..c26), with their relations and the generated
-- contact log (ids ct1..). People who sign up get uuid ids, so they are safe.
-- The Microsoft people, domains, boards and customers stay. On an empty
-- database this does nothing. It is recorded in History as one change, so an
-- Admin can undo it.
update ms_profiles set cadence = 'none' where owner_id ~ '^c[0-9]+$';   -- no owner left to remind
delete from contacts where id ~ '^ct[0-9]+$';
delete from columbus_profiles where id ~ '^c[0-9]+$';                  -- relations and owners go with them
