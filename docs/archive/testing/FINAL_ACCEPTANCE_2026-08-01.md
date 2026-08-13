# Final acceptance — combined phases 8 and 9

Assessment date: 2026-08-01. `Completed` is used only for locally evidenced work.

## Delivery status

| Area | Code ready | Configuration ready | Azure/external resource ready | Real smoke passed | UAT passed |
|---|---|---|---|---|---|
| Direct analytics | Yes | Yes; explicit protected-environment opt-in | N/A | Not run | Not run |
| Fabric job | HTTP/token/retry/poll code ready | Fail-closed | No evidence | Blocked | Blocked |
| Power BI report | Yes | Template ready | No evidence | Not run | Not run |
| Semantic refresh | Yes; accepted/no-poll and bounded poll | Template ready; off by default | No evidence | Not run | Not run |
| API/Teams containers | Dockerfiles ready | Yes | No evidence | Not run | Not run |
| Container Apps Bicep | Yes | Placeholder template ready | Not deployed | Not run | Not run |
| CI/CD | Workflow code ready | External adapter repo and GitHub Environment required | No workflow run evidence | Not run | N/A |

## Completed features

- Analytics provider selection (`Mock`, `Direct`, `FabricJob`) and reporting selection (`Mock`, `PowerBi`) with Production/Staging mock rejection.
- Direct analytics summaries use only row count, source, and truncation metadata; rows, SQL, prompt, scopes, and parameters are not copied.
- Fabric and Power BI token abstractions use Azure Identity; bearer tokens are request-local and are not logged or persisted.
- Fixed official API security boundaries, configured GUID validation, bounded transient retry honoring `Retry-After`, bounded polling/timeout, cancellation propagation, and safe failures.
- Power BI Get Report maps only configured report ID and validated HTTPS `app.powerbi.com` `webUrl`; `embedUrl` and embed tokens are unused.
- Optional semantic-model refresh is off by default. When enabled, non-202 or terminal refresh failure fails explicitly; polling can be disabled after accepted submission or bounded when enabled.
- API/Teams multi-stage .NET 10 non-root images, sanitized live/ready endpoints, Container Apps/Log Analytics/ACR identity/Key Vault Bicep, OIDC workflows, migration artifact separation, smoke scripts, UAT and runbooks.

## Mock and external state

Mock query planning remains from phases 0–7. Mock analytics/reporting remain available only for Development/test. The real `Crm.Analytics.Sql` adapter remains referenced and unchanged. No actual Azure, Fabric, Power BI, Teams, SQL, Service Bus, Key Vault, ACR, or Container Apps identifiers/credentials were supplied or committed.

The repository contains no approved Fabric pipeline/notebook parameter schema and no durable Blob/OneLake/Delta staging contract. Existing `qry_*` references are process-local. Consequently `FabricJob` configuration validation is intentionally blocked and query rows are never embedded in Fabric REST, outbox, or Service Bus payloads. Activation requires a data-owner-approved staging lifecycle, identity access, retention/deletion rules, and exact safe parameter contract.

## Security and regression boundaries

Existing Entra authentication, `oid`/`tid` ownership, app-role/data-scope policies, SQL persistence/migrations, application audit, transactional outbox, Service Bus, DWH/OLTP routing, Teams SSO/durable stores/card revision and clarification, Rejected versus Failed semantics, and public API contracts were not intentionally changed. Teams opens only a credential-free `app.powerbi.com` HTTPS URL. A link grants no data authority: users still require Power BI Entra access and RLS; backend ownership/scope does not replace RLS.

No PBIX, semantic model per question, DAX, embed token/iframe, Fabric workspace/item, fabricated pipeline/notebook contract, broad runtime role, client-secret deployment, actual Azure deployment, or automatic startup migration was added.

## Evidence, migrations, and risks

- Migrations added: none. Existing migration list remains unchanged; drift check passed and a 17,340-byte idempotent SQL migration script was generated successfully for artifact verification.
- Local validation on 2026-08-01: tool restore and solution restore passed; Release build passed with 0 warnings and 0 errors; 568 unit and 125 integration tests passed (693 total, 0 failed/skipped); EF reported no pending model changes.
- NuGet vulnerable-package audit reported no known vulnerable packages for API, Infrastructure, Teams, or the sibling `Crm.Analytics.Sql` project using the configured package sources.
- `az bicep build --file infra/bicep/main.bicep` passed. No Azure validation/what-if/deployment ran because no subscription/resource values were supplied.
- Docker CLI 29.1.5 was found, but the Docker Desktop Linux daemon was unavailable. Image build, container start, and container health checks are therefore `Blocked`, not passed.
- All five PowerShell smoke scripts parsed successfully. They were not executed against real hosts/resources; Fabric explicitly returns `Blocked` until its approved contract exists.
- Main blockers: approved Fabric staging/parameter contract; portable/authorized source location for the sibling `Crm.Analytics.Sql` dependency in CI; real resource IDs/permissions; OIDC federation; DBA migration approval; real smoke and UAT evidence.
- Demo readiness: code/configuration demo-ready with Development mocks or Direct analytics after explicit choice; real Fabric/Power BI demo not evidenced.
- Production readiness: not ready until blockers are resolved and Azure resources, real smoke tests, security review, operational drill, and UAT are passed.
