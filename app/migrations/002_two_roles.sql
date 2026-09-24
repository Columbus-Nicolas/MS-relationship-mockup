-- Migration 002: two roles, Admin and Standard. Super Admins become Admins and
-- Moderators become Standard. For now both roles have the same access (see
-- ADMIN in app/server.js and canEdit() in app/index.html).
update columbus_profiles set role = 'admin' where role = 'superadmin';
update columbus_profiles set role = 'standard' where role = 'moderator';

alter table columbus_profiles drop constraint columbus_profiles_role_check;
alter table columbus_profiles add constraint columbus_profiles_role_check check (role in ('admin', 'standard'));
