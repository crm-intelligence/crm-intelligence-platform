# CRM Analytics

CRM Analytics is a Teams-based reporting platform that turns natural-language requests into validated semantic plans, executes allow-listed and parameterized analytical queries, and returns safe report results through Teams and Power BI integrations.

The planning model never generates production SQL or chooses physical database objects. `Crm.Analytics.Sql` is the only production SQL-generation boundary, and unsupported or incompatible requests fail closed before query execution.

## High-level architecture

```text
Microsoft Teams
-> CrmAnalytics.Teams
-> CrmAnalytics.Api
-> Application / Infrastructure workflow
-> semantic planning and CanonicalRequest validation
-> Crm.Analytics.Sql deterministic Query Builder
-> Fabric DWH or supported OLTP source
-> persistence, queue, analytics and reporting integrations
-> Teams notification / Power BI report link
```

The API process hosts the REST API, transactional outbox dispatcher, and Service Bus consumer. The Teams host is deployed separately. See the [system architecture](docs/architecture/SYSTEM_ARCHITECTURE.md) for the detailed runtime flow.

## Repository structure

```text
src/                 Production applications and libraries
tests/               Unit and integration tests
tools/               Data-scope, fixture, and planning utilities
data/                Versioned development fixtures
infra/               Azure/Bicep infrastructure definitions
deploy/              SQL, Teams package sources, and deployment helpers
scripts/             Reusable operational and smoke scripts
docs/                Architecture, deployment, operations, testing, and planning docs
.github/workflows/    Canonical CI/CD workflows
CrmAnalytics.slnx    Canonical 14-project solution
```

`crm-project/`, `crm-project.Tests/`, and `.github/workflows/devops_lokman-crm-project.yml` are retained only for legacy Azure Web App deployment compatibility. They are not the canonical runtime or source layout and will be removed after the Container Apps cutover is verified.

## Prerequisites

- .NET SDK `10.0.301` with the `latestFeature` roll-forward policy, as defined by `global.json`.
- Repository-local .NET tools restored from `.config/dotnet-tools.json` (`dotnet-ef` `10.0.10`).
- Docker only for the local container workflow or image builds.
- Azure CLI with Bicep only for IaC validation or deployment work.

No production credentials or secrets belong in the repository. Use the configured environment, user-secret, managed-identity, and Key Vault mechanisms.

## Build and test

From the repository root:

```bash
dotnet tool restore
dotnet restore CrmAnalytics.slnx
dotnet build CrmAnalytics.slnx -c Release --no-restore
dotnet test CrmAnalytics.slnx -c Release --no-build
```

The SQL integration fixture can be regenerated from the versioned Olist CSV files when required:

```bash
dotnet run --project tools/Crm.Analytics.Sql.DevData/Crm.Analytics.Sql.DevData.csproj
dotnet test tests/Crm.Analytics.Sql.IntegrationTests/Crm.Analytics.Sql.IntegrationTests.csproj -c Release
```

The generated `crm_dev.db` files are local artifacts and are ignored by Git.

## Local development

The default Development configuration supports direct host execution:

```bash
dotnet run --project src/CrmAnalytics.Api/CrmAnalytics.Api.csproj --launch-profile https
dotnet run --project src/CrmAnalytics.Teams/CrmAnalytics.Teams.csproj
```

The launch profiles expose the API at `https://localhost:7090` (and `http://localhost:5090`) and the Teams host at `http://localhost:3978`. The Teams Development configuration targets the API HTTPS endpoint. Configure required local values outside source control.

For the containerized local workflow:

```bash
docker compose -f docker-compose.local.yml up --build
```

This binds the API to `127.0.0.1:8080` and the Teams host to `127.0.0.1:3978`.

## Deployment

Canonical deployment assets live under `infra/`, `deploy/`, and `.github/workflows/`. Start with the [deployment documentation](docs/deployment/DEPLOYMENT_RUNBOOK.md); do not run production deployment or database migration commands without the required review and approvals.

## Documentation

The documentation index is [docs/README.md](docs/README.md). It separates living architecture, deployment, operations, testing, and planning material from point-in-time records under `docs/archive/`.
