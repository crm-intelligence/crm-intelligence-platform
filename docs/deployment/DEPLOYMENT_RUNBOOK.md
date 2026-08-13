# Deployment runbook

## Prerequisites

- .NET SDK from `global.json`, restored local tools, Docker, and Azure CLI with Bicep.
- Existing subscription/resource group, ACR, Key Vault, user-assigned identity, Azure SQL application DB, query DWH/OLTP, Service Bus queue, Entra registrations, Teams/Bot registration, pre-created Power BI report, and—before Fabric activation—an approved staging/parameter contract.
- GitHub Environment approval and OIDC federation. No client secret is used.

## Minimum access matrix

| Principal | Resource | Minimum access |
|---|---|---|
| Runtime identity | ACR | `AcrPull` |
| API system-assigned identity | Key Vault | `Key Vault Secrets User` only for API-referenced secrets |
| Teams system-assigned identity | Key Vault | `Key Vault Secrets User` only for Teams-referenced secrets |
| Runtime identity | Application Azure SQL DB | Only required read/write schema permissions |
| Runtime identity | DWH/OLTP query DB | `SELECT` only on approved objects |
| Runtime identity | Service Bus queue | Data Sender and Data Receiver at queue/namespace scope as required |
| Runtime identity | Fabric workspace/item | Minimum role required to run/read the approved pre-created job |
| Runtime identity | Power BI workspace | Minimum access to read the configured report and, if enabled, refresh the semantic model |
| GitHub deploy identity | Resource group/ACR | Scoped deployment and image push rights only |

Do not grant Owner or broad subscription Contributor to the runtime identity.
Do not grant the runtime identity redundant Key Vault secret access; Container
App Key Vault references use each app's system-assigned identity.

## Configuration and secrets

Non-secret environment values include provider names, workspace/report/item/semantic-model GUIDs, job type, Service Bus namespace host, timeouts, and managed identity client ID. Key Vault references hold the application DB connection and Bot/internal API keys that cannot use managed identity. Entra tenant/client values are supplied by the environment and are never committed as real values.

GitHub Environment variables: `AZURE_CLIENT_ID`, `AZURE_TENANT_ID`, `AZURE_SUBSCRIPTION_ID`, `AZURE_RESOURCE_GROUP`, `ACR_NAME`, `API_CONTAINER_APP_NAME`, and `TEAMS_CONTAINER_APP_NAME`. Store the complete deployment parameter JSON as the masked Environment secret `BICEP_PARAMETERS_JSON`; the workflow writes it only to runner temporary storage and never uploads it. DevOps creates the federated credential; no client-secret fallback is permitted.

## Build, validate, deploy

Both images build from the canonical repository root:

```powershell
docker build -f src/CrmAnalytics.Api/Dockerfile -t <acr>/crm-analytics-api:<unique-build-tag> .
docker build -f src/CrmAnalytics.Teams/Dockerfile -t <acr>/crm-analytics-teams:<unique-build-tag> .
az bicep build --file infra/azure/bicep/main.bicep
az deployment group what-if --resource-group <rg> --template-file infra/azure/bicep/main.bicep --parameters <approved-file> apiImage=<api-digest-reference> teamsImage=<teams-digest-reference>
```

Review what-if for deletions and role expansion. The workflow deploys only when manually dispatched with the approved GitHub Environment and `deployInfrastructure=true`. Build tags use `<12-char-git-sha>-<pipeline-run-id>-<attempt>` exactly once; deployment uses the resolved registry digest and never `latest`. Fabric/Power BI resources are not deployed by Bicep.

## Database and application release

Generate the idempotent migration script as an artifact. DBA approval and a separate database pipeline apply it; application startup and Container App deployment never run `database update`. Record migration IDs and artifact hash before container rollout.

After deployment, require active healthy revisions, run scripts under `scripts/smoke`, verify the Teams Bot messaging endpoint/manifest, and collect evidence without secrets. Keep new revision traffic at zero or limited until approval; then shift traffic manually. Rollback moves traffic to the previous immutable revision. Coordinate any schema incompatibility as a reviewed forward migration.

Power BI access also requires the end user to have Entra/RLS permissions. Backend ownership and data scope do not replace Power BI RLS. Do not place user, tenant, region/store, token, or dynamic filter values in report URLs.
