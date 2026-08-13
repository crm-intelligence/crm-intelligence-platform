# Azure Container Apps deployment

## Target and current state

- Subscription: `Azure subscription 1`
  (`971bb0d9-7f3a-4872-9bc4-b602f3b682a8`)
- Resource group: `crm-project-rg-target`
- Region: `swedencentral`
- Container Apps Environment: `crm-analytics-pilot-env`
- Default domain: `jollyplant-0f656895.swedencentral.azurecontainerapps.io`

The API is deployed with external HTTPS ingress and is healthy. The Teams host
is intentionally gated until its Teams client secret and Copilot Studio Direct
Line secret are supplied. The existing Azure Bot has not been changed.

## Architecture and applications

```text
Azure Bot / Teams
    -> crm-analytics-pilot-teams (external HTTPS, port 8080)
    -> crm-analytics-pilot-api (external HTTPS, port 8080)
       -> Azure SQL CrmAnalytics
       -> Service Bus crm-report-processing
       -> Fabric DWH / Fabric OLTP
       -> Power BI
```

There is no separate worker executable. `CrmAnalytics.Api` hosts the REST API,
outbox dispatcher, Service Bus consumer, and report-processing worker in one
process. Ollama is disabled in the production configuration and is not a
Container App.

| App | Image | Ingress | Port | Probes | Scale |
| --- | --- | --- | --- | --- | --- |
| `crm-analytics-pilot-api` | `crmprojectacr634c.azurecr.io/crm-analytics-api@sha256:6718bd34543c1154198545affa9abe9c0624938ad84bc730ac2edb875faf9233` | External | 8080 | `/health/live`, `/health/ready` | 1–2 |
| `crm-analytics-pilot-teams` | `crmprojectacr634c.azurecr.io/crm-analytics-teams@sha256:70d144a49a3b2235e87f4c81bc1c884755957c86484d1e8635d418c6cc2fe358` | External | 8080 | `/health/live`, `/health/ready` | 1–1; pending secrets |

The API cannot scale to zero because its background consumers must continue to
process outbox and Service Bus work. Teams uses in-memory delivery/action state,
so it is limited to one replica until that state is externalized.

## Existing resources reused

- ACR: `crmprojectacr634c`
- Azure SQL server/database: `crmprojectsql634c` / `CrmAnalytics`
- Service Bus namespace/queue: `crmprojectsb634c` /
  `crm-report-processing`
- Azure Bot: `crm-analytics-pilot-bot`
- API and Bot Entra registrations, Bot Teams channel, Bot OAuth connection,
  Fabric/Power BI workspace and reports

Container Apps-related resources added in the target group are:

- runtime UAMI `id-crm-analytics-runtime`
- RBAC-enabled Key Vault `crmprojectkv634c`
- Log Analytics workspace `crm-analytics-pilot-logs`
- Container Apps Environment `crm-analytics-pilot-env`
- API Container App `crm-analytics-pilot-api`

## Configuration

Important non-secret values are represented in
`infra/azure/bicep/parameters/crm-project-rg-target.bicepparam`:

- Entra tenant, API client ID, and audience
- Teams Bot client ID, tenant, app type, and OAuth connection name
- runtime UAMI resource, client, and principal IDs
- SQL/Fabric database endpoints and provider modes
- Service Bus namespace and queue
- Power BI workspace/report/semantic-model IDs
- image digests, ingress, and replica limits

Key Vault contains these credentialless managed-identity SQL configurations:

- `crm-analytics-application-db`
- `crm-analytics-query-dwh`
- `crm-analytics-query-oltp`

The existing `crm-analytics-internal-api-key` is shared by
`TeamsNotifications__ApiKey`, `BackendApi__InternalApiKey`, and
`ReportNotifications__ApiKey` through Key Vault references.

Required application secrets that remain to be supplied, without values:

- `crm-analytics-teams-client-secret`: an unexpired client secret for Entra app
  `3e277fe2-0da0-4149-80da-2168ac44e7b9`, mapped to
  `Teams__ClientSecret`
- `crm-analytics-copilot-direct-line-secret`: the existing Copilot Studio Direct
  Line secret, mapped to `CopilotStudio__DirectLineSecret`; it is currently
  unavailable and Copilot Studio remains disabled

Populate secrets through an approved operator workflow or Azure Portal. Do not
put values in Bicep parameters, shell history, source control, or CI logs.

## Identity and authorization

The old UAMI could not move across subscriptions. The replacement UAMI has:

- `AcrPull` on `crmprojectacr634c`
- `Azure Service Bus Data Sender` and `Azure Service Bus Data Receiver` on the
  `crm-report-processing` queue only
- Azure SQL `CrmAnalytics` schema `crm` SELECT/INSERT/UPDATE/DELETE
- Fabric OLTP SELECT on the six existing approved objects only
- the same existing Fabric/Power BI workspace role as the old runtime identity
- `Key Vault Secrets User` at only the referenced secret scopes

The old database/Fabric identity assignments remain for rollback.

## Build and deployment

Use a unique version tag and deploy by digest, never `latest`.

```powershell
az account set --subscription 971bb0d9-7f3a-4872-9bc4-b602f3b682a8

docker build --platform linux/amd64 `
  -f src/CrmAnalytics.Api/Dockerfile `
  -t crmprojectacr634c.azurecr.io/crm-analytics-api:<version> .
docker build --platform linux/amd64 `
  -f src/CrmAnalytics.Teams/Dockerfile `
  -t crmprojectacr634c.azurecr.io/crm-analytics-teams:<version> .

az acr login --name crmprojectacr634c
docker push crmprojectacr634c.azurecr.io/crm-analytics-api:<version>
docker push crmprojectacr634c.azurecr.io/crm-analytics-teams:<version>

az bicep lint --file infra/azure/bicep/main.bicep
az deployment group validate `
  --resource-group crm-project-rg-target `
  --parameters infra/azure/bicep/parameters/crm-project-rg-target.bicepparam
az deployment group what-if `
  --resource-group crm-project-rg-target `
  --parameters infra/azure/bicep/parameters/crm-project-rg-target.bicepparam
az deployment group create `
  --resource-group crm-project-rg-target `
  --parameters infra/azure/bicep/parameters/crm-project-rg-target.bicepparam
```

ACR Tasks returned `TasksOperationsNotAllowed` in this subscription, so the
current release was built with local Docker Desktop for `linux/amd64`.

## Enabling Teams after secret configuration

After both remaining required secrets exist and are enabled in `crmprojectkv634c`, set the
following parameters to `true` in the target parameter file:

```bicep
param deployTeams = true
param internalApiKeyIntegrationEnabled = true
param teamsClientSecretIntegrationEnabled = true
param teamsNotificationsEnabled = true
param copilotStudioDirectLineSecretIntegrationEnabled = true
param copilotStudioEnabled = true
```

Run validation and what-if, then deploy. Require the Teams revision to be
healthy and verify public HTTP 200 responses for `/`, `/privacy`, `/terms`,
`/health/live`, and `/health/ready` before changing the Bot.

The Bot endpoint is currently:

```text
Old Messaging Endpoint:
https://crm-analytics-pilot-teams.mangodesert-3e89f5b7.swedencentral.azurecontainerapps.io/api/messages

New Messaging Endpoint:
Pending Teams deployment; expected form is
https://crm-analytics-pilot-teams.jollyplant-0f656895.swedencentral.azurecontainerapps.io/api/messages
```

Update the Bot endpoint only after the exact deployed Teams FQDN and bot route
are healthy.

## Verification

```powershell
az containerapp list -g crm-project-rg-target -o table
az containerapp revision list -g crm-project-rg-target `
  -n crm-analytics-pilot-api -o table
az containerapp replica list -g crm-project-rg-target `
  -n crm-analytics-pilot-api `
  --revision crm-analytics-pilot-api--acgk1op -o table
az containerapp logs show -g crm-project-rg-target `
  -n crm-analytics-pilot-api --type system --tail 100
az servicebus queue show -g crm-project-rg-target `
  --namespace-name crmprojectsb634c --name crm-report-processing
```

The internal API has no public endpoint. Its `/health/ready` is used as the
Container Apps readiness probe and covers the application database, outbox,
messaging runtime, query sources, and external provider readiness.

## Rollback

Images are immutable digest references and Container Apps uses multiple
revisions. Keep the prior healthy revision active until the replacement is
verified. Roll back by moving traffic to the prior healthy revision, then
deactivate the failed replacement. For the API, do not leave an obsolete
background-worker revision active: zero-percent HTTP traffic does not stop its
Service Bus/outbox workers.

Do not automatically reverse database migrations. Use a separately reviewed
forward migration when schema compatibility is involved.
