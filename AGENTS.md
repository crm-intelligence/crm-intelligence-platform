# CRM Intelligence Platform — Codex Instructions

## Purpose and hard boundary

This repository implements governed CRM analytics through Microsoft Teams.

Primary submitted-plan flow:

`Teams -> Copilot Studio semantic plan -> ASP.NET API -> backend validation -> CanonicalRequest -> data scope -> deterministic Query Builder -> DWH or OLTP -> report result -> Teams`

Requests without a submitted Copilot plan use the configured Ollama fallback flow.

Models may interpret business semantics. In the deterministic path, models must not generate SQL. In the agentic SQL path, an approved reasoning model may generate candidate SQL using backend-provided semantic, schema, relationship, and query tools.

Model-generated SQL is never trusted or executed directly. It must pass backend-owned semantic validation, SQL AST validation, physical object and column allow-lists, approved join-graph validation, authorization and data-scope enforcement, parameterization, complexity/cost limits, and safe execution validation before it can become an executable plan.

Models must never determine authorization, widen data scope, bypass source compatibility, choose credentials, bypass execution policy, or directly execute SQL. The backend remains the final execution authority.

Fail closed. Clarification is preferable to an unsupported or unsafe semantic substitution.

---

## Source-of-truth order

When repository descriptions disagree, use this order:

1. Runtime code, contracts, tracked configuration, and passing tests.
2. Current architecture, deployment, and operations documents under `docs/`.
3. Root `README.md` for orientation only.
4. `docs/archive/` for historical context only; never use it as a current implementation contract.

Search for symbols before opening large files. Do not infer current behavior from test counts, dated reports, archived plans, or comments in the legacy projects.

---

## Work efficiently

Before editing:

1. Record branch, HEAD SHA, and `git status --short`.
2. Classify the task using the routing section below.
3. Read only the listed entry points and directly referenced dependencies.
4. Prefer `rg`/`rg --files`; exclude `bin/`, `obj/`, generated databases, and local diagnostics.
5. Start with the smallest relevant test project or filter.
6. Inspect another project only when a dependency, compiler error, or failing test proves it is relevant.

Do not produce architecture reports unless requested. Do not continue with unrelated improvements after the requested goal is complete.

---

## Canonical repository map

The canonical solution is `CrmAnalytics.slnx`. `global.json` currently specifies .NET SDK 10.0.301 with `latestFeature` roll-forward.

Runtime projects under `src/`:

- `CrmAnalytics.Api` — HTTP host, authentication/authorization composition, controllers, health endpoints.
- `CrmAnalytics.Application` — report lifecycle, conversations, processing, authorization abstractions, outbox contracts.
- `CrmAnalytics.Contracts` — public/internal transport and Copilot Studio contracts.
- `CrmAnalytics.Domain` — report-request and conversation domain behavior.
- `CrmAnalytics.Infrastructure` — persistence, identity/data scope, integrations, messaging, outbox, query execution.
- `CrmAnalytics.Teams` — Teams host, cards/actions, Copilot Studio client, notifications.
- `Crm.Analytics.Sql` — semantic catalog, canonical contracts, routing, guardrails, deterministic query construction.

Test projects under `tests/`:

- `CrmAnalytics.UnitTests`
- `CrmAnalytics.IntegrationTests`
- `CrmAnalytics.DataScopeProvisioner.Tests`
- `Crm.Analytics.Sql.Tests`
- `Crm.Analytics.Sql.IntegrationTests`

Tools:

- `tools/CrmAnalytics.DataScopeProvisioner` — production-safe unrestricted data-scope provisioning.
- `tools/Crm.Analytics.Sql.DevData` — deterministic local/test fixture generation.
- `tools/CrmAnalytics.OllamaSmoke` — local semantic-planning quality evaluation; it is not included in `CrmAnalytics.slnx`, so build it explicitly when changed.

Deployment and operations:

- `infra/azure/bicep/` — Azure Container Apps infrastructure definitions.
- `deploy/sql/` — reviewed SQL deployment artifacts.
- `deploy/teams/` — Teams manifest and source icons.
- `deploy/scripts/` — operator examples; never place secrets in them.
- `scripts/smoke/` — post-deployment smoke checks.
- `docs/deployment/` and `docs/operations/` — current runbooks and resource state.

The canonical GitHub Actions boundary is `.github/workflows/ci.yml` for validation and `.github/workflows/deploy-azure.yml` for the Azure Container Apps production path. Neither workflow includes the legacy Web App projects. Edit these workflows only when the task explicitly targets CI/CD.

`crm-project/` and `crm-project.Tests/` are retained legacy Azure Web App compatibility projects. They are outside `CrmAnalytics.slnx` and are not canonical runtime source. Do not modify or remove them unless the task explicitly targets the legacy cutover.

---

## Current semantic-planning architecture

Start with `docs/architecture/semantic-catalog-driven-planning.md`. It is the maintained architecture document for planning behavior.

### Submitted Copilot plan

For `/planned` requests, Copilot Studio supplies a strict full semantic plan. The backend does not reconstruct metric, grouping, filters, date, or ranking from the raw prompt when that plan is structurally valid.

Planned clarification and revision endpoints validate ownership, permission, current data scope, lifecycle state, and the full submitted plan before enqueueing processing. They do not perform semantic planning. Clarification resumes the same logical request; revision creates a child request.

Relevant areas:

- `src/CrmAnalytics.Contracts/CopilotStudio/`
- `src/CrmAnalytics.Api/Integrations/CopilotStudio/`
- `src/CrmAnalytics.Api/Controllers/ReportRequestsController.cs`
- `src/CrmAnalytics.Application/ReportRequests/`
- `src/CrmAnalytics.Teams/Planning/`
- `src/CrmAnalytics.Teams/Messaging/`
- `src/CrmAnalytics.Infrastructure/Integrations/SubmittedSemanticPlanningResultMapper.cs`

### Ollama fallback

For requests without a submitted Copilot plan, `Ollama:PlanningMode` selects the flow. Tracked API configuration currently selects `LlmFirst` and `qwen3:8b`; Ollama itself is disabled unless configured. In `LlmFirst`, disabled or invalid model output produces a safe clarification rather than silently dropping into the other planning mode. `EmbeddingFirst` remains a rollback mode. The tracked embedding model is `qwen3-embedding:0.6b`.

`LlmFirst` flow:

`natural language -> Qwen structured semantic extraction -> strict JSON validation -> catalog/operation/compatibility validation -> deterministic source selection -> deterministic date calculation -> canonical validation -> data scope -> deterministic Query Builder`

The LLM-first model returns extracted semantic intent, not SQL and not a model-authored `CanonicalRequest`. Source selection and calendar arithmetic remain backend-owned.

`EmbeddingFirst` flow:

`deterministic preprocessing -> date/slot intent -> semantic representations -> embedding retrieval -> discrimination -> completeness gate -> deterministic canonical assembly or candidate-constrained partial Qwen -> canonical validation`

Relevant entry files:

- `src/CrmAnalytics.Infrastructure/Integrations/LlmFirstSemanticPlanning.cs`
- `src/CrmAnalytics.Infrastructure/Integrations/OllamaStructuredPlanningClient.cs`
- `src/CrmAnalytics.Infrastructure/Integrations/OllamaCanonicalContract*.cs`
- `src/CrmAnalytics.Infrastructure/Integrations/SemanticPlanning.cs`
- `src/CrmAnalytics.Infrastructure/Integrations/SemanticCompleteness.cs`
- `src/CrmAnalytics.Infrastructure/Integrations/SemanticEmbedding*.cs`
- `src/CrmAnalytics.Infrastructure/Integrations/CrmAnalyticsSqlProductionClient.cs`

Do not redesign or merge these flows unless explicitly requested. Read the configured planning mode before diagnosing a behavior.

---

## Semantic and SQL invariants

`SemanticCatalogRegistry` is the authoritative semantic source. Model-facing projections, deterministic resolution, backend compatibility, and Query Builder behavior must derive from it. Never create a second independent metric/dimension catalog.

Aliases describe concepts, not complete user requests. Do not add full prompt sentences or introduce test-specific branches, regex mappings, sentence mappings, thresholds, or hard-coded business values.

These invariants must not be weakened:

1. Unsupported semantic concepts never reach Query Builder.
2. Unsupported or rejected requests generate no SQL.
3. Unknown semantic keys and operations are rejected.
4. Metric/dimension/filter/source compatibility is validated by the backend catalog.
5. Source selection comes from authoritative compatibility, never model preference.
6. Deterministic bypass in `EmbeddingFirst` requires semantic completeness and generic lexical evidence; embedding confidence alone is insufficient.
7. Candidate-constrained partial Qwen cannot select outside its candidate set.
8. Resolved semantic slots remain immutable during partial-Qwen resolution.
9. Data scope is backend enforced and cannot be supplied or widened by a model.
10. Physical mappings and approved relationships come only from reviewed repository semantic contracts, allow-lists, and join-graph definitions.
11. Deterministic SQL remains generated by the deterministic Query Builder. Agentic SQL may be model-authored only as candidate SQL inside the approved Agentic SQL Strategy.
12. Model-authored candidate SQL must never bypass the common SQL security pipeline. Before execution it must pass AST/read-only validation, physical object and column allow-lists, approved join validation, authorization/data-scope enforcement, parameterization, row/complexity/cost limits, and safe validation.
13. Models cannot directly execute SQL, select credentials, widen authorization/data scope, or override backend execution policy.
14. Raw prompts, semantic representations, unresolved expressions, model responses, SQL, filter values, user identity, tokens, and credentials must not enter audit/log contracts.

DWH is for supported analytical/aggregated reporting. OLTP is only for supported operational-detail queries and is disabled by default in tracked API configuration. Do not invent source compatibility or database objects.

Resolve relative dates deterministically through `src/Crm.Analytics.Sql/Nlu/RelativeDateResolver.cs`. The model must not perform calendar arithmetic. Do not add sentence-specific date rules.

---

## Data scope and authorization

Application data scope is resolved and enforced in the backend. For runtime data-scope behavior start with:

- `src/CrmAnalytics.Application/Identity/`
- `src/CrmAnalytics.Infrastructure/Identity/`
- `src/CrmAnalytics.Infrastructure/Integrations/SqlProductionScopeCompatibilityMapper.cs`
- `tests/CrmAnalytics.UnitTests/ReportDataAccessTests.cs`
- `tests/Crm.Analytics.Sql.IntegrationTests/Execution/ScopeEnforcementTests.cs`

Use `tools/CrmAnalytics.DataScopeProvisioner` for application data-scope provisioning. Its CLI contract is `ProvisionerOptions.cs`. The current tool provisions unrestricted scope only, so both `ALLOW_ALL_REGIONS` and `ALLOW_ALL_STORES` must be `true`. Production writes require `CONFIRM_PRODUCTION_WRITE=true`; start with `DRY_RUN=true`. Prefer execution inside Azure with the runtime managed identity.

Do not directly insert/update data-scope tables with ad-hoc SQL unless the task explicitly authorizes diagnostic SQL.

For API authorization start with:

- `src/CrmAnalytics.Api/Program.cs`
- `src/CrmAnalytics.Api/Authentication/`
- `src/CrmAnalytics.Application/Authorization/`
- `src/CrmAnalytics.Infrastructure/Authorization/`
- relevant `CrmAnalytics.IntegrationTests`

Never weaken Entra scope/app-role checks, ownership filters, or action-token validation to make a test pass.

---

## Task routing

### API/report lifecycle

Start with `src/CrmAnalytics.Api/Program.cs`, the relevant controller, `src/CrmAnalytics.Application/ReportRequests/`, and `src/CrmAnalytics.Application/ReportProcessing/`. Then inspect the exact infrastructure adapter used by the service.

### Copilot plan, clarification, or revision

Start with the semantic architecture document, `src/CrmAnalytics.Contracts/CopilotStudio/`, API Copilot integrations, `ReportRequestsController`, Teams `Planning`/`Messaging`, and the matching unit/integration tests.

### Ollama, embeddings, or semantic quality

Start with the semantic architecture document and the relevant integration files listed above, then `src/Crm.Analytics.Sql/Catalog/` and `Nlu/`. Use `tools/CrmAnalytics.OllamaSmoke` only after implementation stabilizes.

### Query Builder, guardrail, or physical mapping

Start with:

- `src/Crm.Analytics.Sql/QueryBuilder/`
- `src/Crm.Analytics.Sql/Guardrail/`
- `src/Crm.Analytics.Sql/Contracts/`
- `src/Crm.Analytics.Sql/Catalog/`
- matching SQL unit and integration tests

Inspect existing mapping contracts and execution tests before changing any physical object or expression.

### Agentic SQL production

For Agentic SQL Strategy work, start with:

- `src/Crm.Analytics.Sql/`
- the query-strategy routing layer
- semantic catalog/model contracts
- join-graph contracts
- SQL guardrails
- query execution contracts
- matching SQL unit and integration tests

Preserve the deterministic Query Builder as the fast path.

Agentic SQL generation must be implemented as a separate strategy and must converge into the same backend-owned validation, authorization, data-scope, parameterization, and execution-security pipeline before execution.

Do not give model clients direct database execution authority.

### Persistence, outbox, queue, or messaging

Start with the corresponding folders under `src/CrmAnalytics.Application/` and `src/CrmAnalytics.Infrastructure/`, plus `src/CrmAnalytics.Api/Program.cs`. Preserve transactional outbox semantics, idempotency, retries, and health behavior.

### Teams

Start with `src/CrmAnalytics.Teams/`, its local `README.md`, and `deploy/teams/`. Do not modify Teams for API/query-planning work unless the failure is proven to originate there.

### Database migrations

Start with `src/CrmAnalytics.Infrastructure/Persistence/SqlServer/Migrations/` and `deploy/sql/`. Generate/review idempotent migration artifacts as the repository contract requires. Application startup and Container App deployment must not run `database update`; applying a migration in production is a separately approved DBA operation.

### Azure deployment

Inspect only `infra/azure/bicep/`, the relevant deployment/operations runbooks, the relevant image/Dockerfile, and authorized current Azure state. Existing resources are inputs; do not recreate them by default.

For ordinary code tasks, do not deploy Bicep, create Azure resources, push images, mutate production data, or change live revisions. Use managed identity and Key Vault patterns already present. Never enable ACR admin credentials. Deploy immutable image digests, not `latest`.

Container Apps revisions that host background workers must be deactivated when replaced; assigning 0% HTTP traffic does not stop their workers.

---

## Testing and verification

Use this order in proportion to the change:

1. Targeted test method/class or smallest relevant test project.
2. Affected project tests.
3. SQL/application integration tests when contracts, persistence, routing, scope, or execution changed.
4. `dotnet build CrmAnalytics.slnx -c Release` for cross-project code changes.
5. Full solution tests only for cross-cutting or release-ready work.

Canonical commands:

```powershell
dotnet restore CrmAnalytics.slnx
dotnet build CrmAnalytics.slnx -c Release --no-restore
dotnet test CrmAnalytics.slnx -c Release --no-build
```

Use `--no-build` only after building the same configuration and current source. If `CrmAnalytics.OllamaSmoke` changed, build its `.csproj` separately because it is outside the solution.

For semantic-planning changes verify unsupported safety, clarification behavior, submitted-plan validation, deterministic date handling, candidate constraints when relevant, and no SQL for rejected requests.

Do not run local-model quality suites after every edit. After code is frozen, use a separate unseen set for new certification. Once an evaluation set has been inspected, treat it as regression data. The evaluation runner's Release/build-parity and repository-fingerprint checks must not be bypassed.

Documentation-only changes do not require a .NET build unless they alter executable examples or reveal a repository inconsistency. Always run `git diff --check`; validate every path or command added to this file against the checkout.

---

## Git and change safety

Preserve existing user changes and avoid unrelated edits. Never use `git reset --hard`, `git checkout .`, or `git clean -fd` unless explicitly authorized. Do not commit or push unless requested.

Do not edit generated `bin/`, `obj/`, local `crm_dev.db*`, test results, or `.codex-diagnostics/` content. Do not add secrets, tokens, connection strings, private certificates, raw production output, or local evaluation logs.

---

## Definition of done

A code task is complete when:

- requested behavior is implemented;
- relevant targeted tests pass;
- affected integration tests and Release build pass when appropriate;
- no safety invariant is weakened;
- `git diff --check` passes;
- only intended files changed.

Final responses must be concise and report:

- goal or root cause;
- changed files;
- tests/verification;
- deployment state;
- blockers, or explicitly state none.

Stop when the requested goal is satisfied.
