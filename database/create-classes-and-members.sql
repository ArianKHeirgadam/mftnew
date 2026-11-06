/*
  DEPRECATED — DO NOT EXECUTE.
  This legacy script defines UNIQUEIDENTIFIER keys incompatible with the
  canonical bigint Users and Departments tables.
  Use database/patch-bigint-feature-tables.sql only after the API/EF Core
  bigint migration is complete. Existing class data requires a reviewed
  migration; do not drop or recreate existing tables.
*/
THROW 52021, 'Deprecated GUID schema script. Use patch-bigint-feature-tables.sql after application cutover.', 1;
