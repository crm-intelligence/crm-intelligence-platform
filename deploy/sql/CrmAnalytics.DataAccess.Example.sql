/*
  REVIEWED EXAMPLE ONLY - do not run directly in production.
  Replace every placeholder GUID through an approved DBA/provisioning process.
  "*" is never a valid region or store value.

  AllowAllRegions = 1 requires no region child rows.
  AllowAllStores  = 1 requires no store child rows.
  An assignment that grants no access in either dimension is invalid.
*/
SET XACT_ABORT ON;
BEGIN TRANSACTION;

DECLARE @TenantId nvarchar(36) = N'00000000-0000-4000-8000-000000000001';
DECLARE @Now datetimeoffset(7) = SYSUTCDATETIME();

-- Scenario 1: all regions and all stores.
INSERT crm.UserDataAccessAssignments
    (TenantId, UserId, AllowAllRegions, AllowAllStores, IsActive,
     CreatedAt, UpdatedAt)
VALUES
    (@TenantId, N'00000000-0000-4000-8000-000000000011',
     1, 1, 1, @Now, @Now);

-- Scenario 2: SP/RJ regions and all stores.
INSERT crm.UserDataAccessAssignments
    (TenantId, UserId, AllowAllRegions, AllowAllStores, IsActive,
     CreatedAt, UpdatedAt)
VALUES
    (@TenantId, N'00000000-0000-4000-8000-000000000012',
     0, 1, 1, @Now, @Now);

INSERT crm.UserDataAccessRegions (TenantId, UserId, RegionCode)
VALUES
    (@TenantId, N'00000000-0000-4000-8000-000000000012', N'SP'),
    (@TenantId, N'00000000-0000-4000-8000-000000000012', N'RJ');

-- Scenario 3: all regions with an explicit store restriction.
-- The backend policy model supports this. If a future Crm.Analytics.Sql
-- adapter cannot enforce store scope, that adapter must remain fail-closed.
INSERT crm.UserDataAccessAssignments
    (TenantId, UserId, AllowAllRegions, AllowAllStores, IsActive,
     CreatedAt, UpdatedAt)
VALUES
    (@TenantId, N'00000000-0000-4000-8000-000000000013',
     1, 0, 1, @Now, @Now);

INSERT crm.UserDataAccessStores (TenantId, UserId, StoreId)
VALUES
    (@TenantId, N'00000000-0000-4000-8000-000000000013', N'STORE-001'),
    (@TenantId, N'00000000-0000-4000-8000-000000000013', N'STORE-002');

COMMIT TRANSACTION;
