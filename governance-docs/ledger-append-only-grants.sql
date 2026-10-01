-- AMD-2026-10-01-0005: operational hardening for the governance ledger (PostgreSQL).
--
-- The migration installs triggers that make governance_ledger append-only for every role, including roles that
-- hold UPDATE/DELETE. This script removes those privileges from the application role as defence in depth.
-- Run it once per environment as the database owner, replacing rpas_app with the role the API connects as.
--
--   psql -v app_role=rpas_app -f ledger-append-only-grants.sql

REVOKE UPDATE, DELETE, TRUNCATE ON governance_ledger FROM :"app_role";
GRANT  SELECT, INSERT          ON governance_ledger TO   :"app_role";

-- The application role must NOT own governance_ledger and must NOT be a superuser: an owner can disable the triggers.
-- Run migrations with a separate, privileged migration role.
