# Deployment runbook

## Prerequisites

- .NET SDK from `global.json`, restored local tools, Docker, and Azure CLI with Bicep.
- Existing subscription/resource group, ACR, Key Vault, user-assigned identity, Azure SQL application DB, query DWH/OLTP, Service Bus queue, Entra registrations, Teams/Bot registration, pre-created Power BI report, and—before Fabric activation—an approved staging/parameter contract.
- GitHub Environment approval and OIDC federation. No client secret is used.

## Minimum access matrix

| Principal | Resource | Minimum access |
|---|---|---|
| Runtime identity | ACR | `AcrPull` |
| Runtime identity | Key Vault | `Key Vault Secrets User` only at the secrets referenced by API and Teams |
| Runtime identity | Application Azure SQL DB | Only required read/write schema permissions |
| Runtime identity | DWH/OLTP query DB | `SELECT` only on approved objects |
| Runtime identity | Service Bus queue | Data Sender and Data Receiver at queue/namespace scope as required |
| Runtime identity | Fabric workspace/item | Minimum role required to run/read the approved pre-created job |
| Runtime identity | Power BI workspace | Minimum access to read the configured report and, if enabled, refresh the semantic model |
| GitHub deploy identity | Resource group/ACR | Scoped deployment and image push rights only |

Do not grant Owner or broad subscription Contributor to the runtime identity.
Do not grant vault-wide Key Vault access; the current Bicep contract uses the
runtime user-assigned identity for Container App secret references and creates
only the required secret-scope assignments.

The GitHub OIDC principal needs `AcrPush` at the target ACR plus narrowly scoped
resource-group deployment rights for the resources in `main.bicep`, Container
Apps revision/traffic operations, and deployment read/write. Because the template
creates secret-scope role assignments, it also needs role-assignment write at only
those Key Vault secret scopes and permission to assign the existing runtime UAMI
to the two Container Apps at that identity's resource scope. The workspace
shared-key action is required only
when `manageContainerAppsEnvironment=true`; the tracked production parameters
reuse the existing environment and set it to `false`. Prefer a custom role or
equivalently scoped role combination; do not use subscription-wide Owner.

## Configuration and secrets

Non-secret environment values include provider names, workspace/report/item/semantic-model GUIDs, job type, Service Bus namespace host, timeouts, and managed identity client ID. Key Vault references hold the application DB connection and Bot/internal API keys that cannot use managed identity. Entra tenant/client values are supplied by the environment and are never committed as real values.

GitHub `production` Environment variables are `AZURE_CLIENT_ID`,
`AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, and
`ACR_NAME`. The app names and all other non-secret deployment metadata come from
the tracked `infra/azure/bicep/parameters/crm-project-rg-target.bicepparam`
contract and Bicep outputs. No GitHub secret is required by the canonical
workflow. DevOps creates the environment-scoped federated credential; no
client-secret fallback is permitted.

## Build, validate, deploy

Both images build from the canonical repository root:

```powershell
docker build -f src/CrmAnalytics.Api/Dockerfile -t <acr>/crm-analytics-api:<unique-build-tag> .
docker build -f src/CrmAnalytics.Teams/Dockerfile -t <acr>/crm-analytics-teams:<unique-build-tag> .
az bicep build --file infra/azure/bicep/main.bicep
az deployment group what-if --resource-group <rg> --template-file infra/azure/bicep/main.bicep --parameters <approved-file> apiImage=<api-digest-reference> teamsImage=<teams-digest-reference>
```

Review what-if output for deletions and role expansion. A successful `main` push
CI run or a manual dispatch from `main` starts the workflow, and the `production`
Environment is the approval boundary before the job can run. Build tags use
`<12-char-git-sha>-<pipeline-run-id>-<attempt>` exactly once; deployment resolves
those pushes to registry digests and never uses `latest`. Fabric/Power BI
resources are not deployed by Bicep.

## Database and application release

CI regenerates the idempotent migration script and fails on drift from
`deploy/sql/CrmAnalytics.Migrations.sql`. DBA approval and a separate database
pipeline apply that reviewed artifact; application startup and Container App
deployment never run `database update`. The production Environment approver must
confirm the required migration IDs and artifact hash are already satisfied before
allowing the container rollout.

The Bicep contract retains `Multiple` revision mode. CD first requires each new
revision to be active and healthy and to reference the exact pushed image digest.
Only then does it move 100% traffic, run the topology-aware scripts under
`scripts/smoke`, and deactivate superseded API revisions because every API
revision hosts outbox, Service Bus, and report-processing workers. The previous
Teams revision remains available at zero traffic for operator recovery. A failure
before smoke completion leaves previous revisions active; the workflow does not
perform destructive rollback. Coordinate any schema incompatibility as a
reviewed forward migration.

Power BI access also requires the end user to have Entra/RLS permissions. Backend ownership and data scope do not replace Power BI RLS. Do not place user, tenant, region/store, token, or dynamic filter values in report URLs.
