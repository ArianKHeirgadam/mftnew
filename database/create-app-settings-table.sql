/*
  DEPRECATED — DO NOT EXECUTE.
  This legacy script uses a GUID identity key. The canonical support-table
  contract uses bigint identity keys.
  Use database/patch-bigint-feature-tables.sql only after the API/EF Core
  bigint migration is complete.
*/
THROW 52022, 'Deprecated GUID schema script. Use patch-bigint-feature-tables.sql after application cutover.', 1;
