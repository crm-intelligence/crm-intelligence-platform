# API / Query Builder Audit Compatibility

## Applied revision

- Repository: `C:\Users\ramaz\source\repos\crm-project`
- Branch (informational): `fix/audit-dispute`
- Base HEAD: `b83b9712c652aa69b75cb73dddc7bf7d7b4244e1`
- Audit date: 2026-08-05
- Result: `AuditMigrationApplied`

The revision contains source-aware DWH/OLTP SQL production, source-bearing
canonical request/revision contracts, fail-closed source resolution, isolated
execution surfaces and OLTP ceilings of 15 seconds and 1000 rows. There is no
DWH/OLTP execution fallback.

## Persistent audit contract

Migration `20260805100943_AddApplicationAuditMetadata` adds one nullable
`nvarchar(max)` column, `crm.ApplicationAuditEvents.AuditMetadataJson`. Existing
rows remain valid with NULL metadata. SQL Server/Azure SQL enforces
`AuditMetadataJson IS NULL OR ISJSON(AuditMetadataJson) = 1` through the active,
trusted `CK_ApplicationAuditEvents_AuditMetadataJson_IsJson` constraint.

The immutable metadata schema version is `application-audit-metadata-v1`.
Metadata contains only fields absent from existing audit columns:

- `schemaVersion`
- `conversationId`
- `sqlContractVersion`
- `physicalObject`
- `queryFingerprint`
- `timeoutSeconds`
- `rowLimit`
- `attemptNumber`
- optional `deliveryId`

Source remains in the existing `DataSource` column and is deliberately not
duplicated in JSON. Existing `RequestId`, `CorrelationId`, actor/tenant,
`ReasonCode`, duration, row count and truncation fields also remain only in their
existing columns.

## Source and execution-plan mapping

| Source (`DataSource`) | SQL contract version | Verified surface | Limits |
| --- | --- | --- | --- |
| `Dwh` | `fabric-dwh-object-mapping-v1` | Guardrail allow-listed `mart.*` object, including `mart.vw_sales`, `mart.vw_customer_rfm`, `mart.vw_payment` | Plan timeout and DWH contract row ceiling |
| `Oltp` | `fabric-oltp-operational-orders-v1` | `dbo.vw_operational_orders` | 15 seconds, 1000 rows |

The mapping is explicit and closed over `Dwh`/`Oltp`. Unknown or undefined source,
missing verified object, missing row limit or an unsupported contract cannot produce
successful execution metadata. The physical object is read from the parsed SQL AST,
canonicalized against the selected allow-list and then carried by the execution plan.
Prompt, canonical fields and other user-provided schema/table text are not object-name
sources.

## Query fingerprint and sensitive data

`queryFingerprint` is lowercase SHA-256 over the parameterized SQL shape. Before
hashing, whitespace is collapsed, SQL text is normalized invariantly and parameter
identifiers are replaced by deterministic first-use ordinals. Parameter values do not
participate, so the same shape has the same fingerprint across values; different SQL
shapes have different fingerprints. Raw SQL is not stored in application audit.

The JSON does not contain raw prompt/result payload, parameter values, result rows,
connection strings, tokens, client secrets, API keys, passwords, stack traces or raw
technical errors. Parameter metadata is currently unnecessary and therefore omitted.
Guardrail codes such as `GR003`, `GR008`, `GR012` and `GR014` remain exact values in
the existing `ReasonCode` field.

## Events, retry and idempotency

`QueryExecutionStarted` uses the existing append-only audit event system. Started and
terminal query events share the same versioned metadata for one processing attempt.
Service Bus provides `DeliveryCount` as `attemptNumber` and `MessageId` as
`deliveryId`; the internal compatibility worker uses attempt 1 and does not invent a
delivery identifier. `RequestId` remains unchanged.

Existing deterministic message IDs, terminal report no-op behavior, audit `EventId`
idempotency and conflicting-content detection remain intact. Service Bus redelivery of
a terminal request cannot execute or append another terminal audit. Clarification is
still `ReportClarificationRequested` rather than execution failure; guardrail rejection
is still `ReportRejected` rather than database failure.

## Azure SQL result

The idempotent EF script was applied to
`crmprojectsql634c.database.windows.net/CrmAnalytics` with the active Entra user. No
Key Vault value or SQL password was read. A temporary rule for one verified public IPv4
was created and removed; the final server firewall list is empty.

- Migration history contains `20260805100943_AddApplicationAuditMetadata`.
- `AuditMetadataJson` is nullable `nvarchar(max)`.
- The JSON constraint is enabled and trusted.
- Application table row counts were unchanged; the database contained no pre-existing
  audit/application rows, and no UPDATE/DELETE conversion ran.
- A second full idempotent script execution made no schema/data change.
- Runtime UAMI `id-crm-analytics-runtime` remains an `EXTERNAL_USER`, has no database
  role or DDL/migration grant, and retains only CONNECT plus application DML on `crm`.

Script SHA-256:
`082925c36e0c5bb31cc468261fa07b9679509bdb39530ef951851b53f47726e4`.

## Validation

| Validation | Final result |
| --- | --- |
| Root `dotnet build -c Release` | Passed, 0 warnings/errors |
| Backend Release build | Passed, 0 warnings/errors |
| Backend unit | 637/637 passed |
| Backend integration | 126/126 passed |
| SQL unit | 452/452 passed |
| SQLite fixture generation | Passed |
| CI SQL integration (`CRM_REQUIRE_SQLITE_DB=1`) | 71/71 passed |
| API tests | 15/15 passed |
| Secret-pattern scan | Passed; production matches were placeholders/variable names, test matches were fixture-only |
| Bicep build/lint/pilot parameter compile | Passed |

## API image

- Tag:
  `crmprojectacr634c.azurecr.io/crm-analytics-api:b83b9712c652-audit-metadata-manual-20260805102107`
- OCI index digest:
  `sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`
- linux/amd64 manifest:
  `sha256:cf97b6a0d383b25742d113a06164fcf2f5cbb086a5c2057c17b22234e0214103`
- Registry image size: 139,733,722 bytes
- ACR update: `2026-08-05T10:23:58.0400922Z`
- Tag policy: read/list enabled, write disabled, delete retained as enabled

Local `/health/live` and `/health/ready` returned HTTP 200 with restart count 0
and no startup exception. Digest pull/inspect confirmed linux/amd64. Environment,
layer history, root filesystem and appsettings scans found no embedded secret. The
Teams digest remains
`sha256:91bd6b83283f0c66686e0219256e3a4a881b0ff74cdab86002529e9c4036650f`.

No Teams build/push, Container Apps validation, what-if or deployment was performed.
