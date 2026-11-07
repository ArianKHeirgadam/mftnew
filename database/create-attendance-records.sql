/*
  DEPRECATED — DO NOT EXECUTE.
  This legacy script defines UNIQUEIDENTIFIER keys, while the canonical MFT
  database uses bigint identity keys for Users and Departments.
  Use database/patch-bigint-feature-tables.sql only after the API/EF Core
  bigint migration is complete. Back up and test on a restored database first.
*/
THROW 52020, 'Deprecated GUID schema script. Use patch-bigint-feature-tables.sql after application cutover.', 1;
