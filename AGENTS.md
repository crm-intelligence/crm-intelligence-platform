# CRM Analytics — Codex Instructions

## Goal

This repository implements a Teams-based CRM analytics system.

Primary flow:

Teams
→ ASP.NET API
→ semantic planning
→ CanonicalRequest
→ validation / data scope
→ deterministic Query Builder
→ Fabric DWH or OLTP
→ report result
→ Teams

The model must never generate production SQL.

---

## Work efficiently

Do not scan the entire repository by default.

Before editing:

1. Identify the task category below.
2. Read only the listed entry files and directly referenced dependencies.
3. Search for symbols before opening large files.
4. Do not rediscover architecture already documented here.
5. Do not inspect unrelated projects unless required by compilation or a failing test.
6. Prefer targeted tests first. Run full suites only before final verification or when the change is cross-cutting.
7. Do not produce long architecture reports unless explicitly requested.
8. Final responses should be concise and report only:

   * root cause / goal
   * changed files
   * tests
   * deployment state
   * blockers

---

# Repository map

## Backend API

Root:

`src/`

Main projects:

* `CrmAnalytics.Api`
* `CrmAnalytics.Application`
* `CrmAnalytics.Infrastructure`
* `CrmAnalytics.Contracts`
* `CrmAnalytics.Domain`

Backend tests:

`tests/`

---

## SQL / semantic planning

Root:

`src/Crm.Analytics.Sql/`

Important areas:

* `Catalog/`
* `Contracts/`
* `Nlu/`
* `QueryBuilder/`
* `Service/`

Tests:

* `tests/Crm.Analytics.Sql.Tests/`
* `tests/Crm.Analytics.Sql.IntegrationTests/`

Primary documentation:

`docs/architecture/semantic-catalog-driven-planning.md`

---

## Teams

Teams host:

`src/CrmAnalytics.Teams`

Teams deployment package:

`deploy/teams/`

Do not modify Teams for API/query-planning tasks unless the failure is proven to originate in Teams.

---

## Data Scope administration

Provisioner:

`tools/CrmAnalytics.DataScopeProvisioner`

Use this tool for application data-scope provisioning.

Do not directly INSERT/UPDATE data-scope tables with ad-hoc SQL unless explicitly requested for diagnosis.

Provisioner CLI contract is defined in:

`ProvisionerOptions.cs`

The tool supports:

* SQL_SERVER
* SQL_DATABASE
* MANAGED_IDENTITY_CLIENT_ID
* TARGET_TENANT_ID
* TARGET_USER_ID
* ALLOW_ALL_REGIONS
* ALLOW_ALL_STORES
* DRY_RUN
* CONFIRM_PRODUCTION_WRITE

Production provisioning should normally execute inside Azure using the runtime managed identity.

---

# Query planning architecture

Current planning pipeline:

Natural language
→ deterministic preprocessing
→ date / slot intent resolution
→ semantic representations
→ embedding candidate retrieval
→ semantic discrimination
→ completeness gate
→ deterministic canonical assembly OR partial Qwen
→ canonical validation
→ deterministic Query Builder

Important backend integration files are under:

`src/CrmAnalytics.Infrastructure/Integrations/`

Core components include:

* `SemanticEmbeddingResolver`
* `SemanticEmbeddingIndex`
* `SemanticRepresentations`
* `SemanticCompletenessGate`
* `SemanticPlanningState`
* `SemanticCanonicalRequestAssembler`
* `OllamaStructuredPlanningClient`
* `OllamaCanonicalContract`
* `CrmAnalyticsSqlProductionClient`

Do not redesign this pipeline unless explicitly requested.

---

# Model responsibilities

Planning model:

`qwen3:8b`

Embedding model:

`qwen3-embedding:0.6b`

The LLM is NOT an SQL generator.

The model may only assist with semantic planning / unresolved candidate discrimination.

It must not choose:

* physical table
* physical view
* physical column
* SQL
* join strategy
* authorization
* data scope
* execution policy

---

# Semantic catalog

`SemanticCatalogRegistry` is the authoritative semantic source.

Do not create a second independent metric/dimension catalog.

Model-facing schema, semantic retrieval and backend compatibility must derive from the authoritative catalog.

Do not add full user sentences as aliases.

Aliases must describe concepts, not test prompts.

Do not introduce test-specific:

* if statements
* regex mappings
* sentence mappings
* thresholds
* hard-coded business values

---

# Safety invariants

These rules must not be weakened:

1. Unsupported semantic concept must never reach Query Builder.
2. Unsupported request must generate no SQL.
3. Unsafe semantic substitution is forbidden.
4. Deterministic bypass requires semantic completeness.
5. Candidate-constrained Qwen cannot select outside the candidate set.
6. Resolved semantic slots are immutable during partial-Qwen resolution.
7. Unknown semantic keys are rejected.
8. Metric/dimension/filter/source compatibility is backend validated.
9. Data scope remains backend enforced.
10. Query Builder is the only production SQL-generation point.
11. SQL must remain parameterized and allow-listed.
12. Raw user prompts, raw model responses, SQL, tokens and credentials must not be logged.

Fail closed.

When uncertain, clarification is preferable to an incorrect query.

---

# Source contracts

DWH is for analytical/aggregated reporting.

OLTP is for supported operational-detail queries.

Source selection must come from semantic compatibility, not model preference.

Do not invent source compatibility.

---

# Date handling

Prefer deterministic date resolution before LLM use.

Relevant code:

`src/Crm.Analytics.Sql/Nlu/RelativeDateResolver.cs`

Supported generic relative-date handling includes concepts such as:

* today / yesterday
* current / previous week
* current / previous month
* current / previous year
* last N days
* last N weeks
* last N months
* last N years

Do not add sentence-specific date mappings.

---

# Production SQL objects

Do not invent database objects.

Known DWH semantic layer uses approved MART views.

Physical mappings must come from repository contracts / allow-lists.

OLTP production access is restricted by its operational contract.

Before changing a physical mapping, inspect the existing contract and tests.

---

# Azure principles

Production resources already exist.

Do not recreate infrastructure unless explicitly requested.

For ordinary code tasks:

* no Bicep deployment
* no Azure resource creation
* no production deployment
* no image push

unless the task explicitly asks for deployment.

Use managed identity instead of credentials where already supported.

Never enable ACR admin credentials merely to make deployment easier.

Old Container Apps revisions containing background workers must be deactivated when replaced; 0% HTTP traffic alone does not stop background workers.

---

# Testing strategy

Start with the smallest relevant test project/filter.

Typical order:

1. targeted unit tests
2. affected project tests
3. backend/SQL integration tests if required
4. Release build
5. full suite only for cross-cutting or release-ready changes

For semantic-planning work also verify:

* unsupported safety
* deterministic bypass correctness
* candidate constraints
* clarification behavior
* no SQL on rejected requests

Do not rerun expensive local-model quality suites after every small code edit.

Use them only after implementation stabilizes.

---

# Evaluation rules

Do not optimize production code against known holdout sentences.

Once an evaluation set has been inspected, treat it as regression data, not unseen data.

For new quality certification create a separate unseen set after implementation is frozen.

Always verify Release binary/config parity before quality evaluation.

Do not use stale `--no-build` binaries.

---

# Git safety

Preserve existing user changes.

Never run destructive:

* reset --hard
* checkout .
* clean -fd

unless explicitly authorized.

Do not commit or push unless explicitly requested.

Before editing record:

* branch
* SHA
* git status

Do not stop merely because the worktree is dirty if the existing changes are clearly part of the current user workflow. Preserve them and avoid unrelated edits.

---

# Task routing

For semantic planning / Ollama / embedding tasks, start with:

* `docs/architecture/semantic-catalog-driven-planning.md`
* relevant files under `src/CrmAnalytics.Infrastructure/Integrations/`
* relevant catalog/NLU files under `src/Crm.Analytics.Sql/`

For Query Builder tasks, start with:

* `src/Crm.Analytics.Sql/QueryBuilder/`
* canonical contracts
* semantic catalog
* SQL tests

For Data Scope tasks, start with:

* `tools/CrmAnalytics.DataScopeProvisioner`
* existing data-scope infrastructure
* do not inspect semantic-planning code

For Teams tasks, start with:

* `src/CrmAnalytics.Teams`
* `deploy/teams`

For API authorization tasks, start with:

* API authentication/authorization registration
* claims/app-role handling
* integration tests

For deployment tasks, inspect only:

* relevant deploy files
* relevant Container App/image
* current Azure resource state

Do not redeploy unrelated components.

---

# Legacy compatibility boundary

`crm-project/`, `crm-project.Tests/`, and
`.github/workflows/devops_lokman-crm-project.yml` are retained only for the
legacy Azure Web App deployment path. They are not canonical runtime source.
Do not modify or remove them unless the task explicitly targets the legacy
cutover.

---

# Definition of done

A code task is complete when:

* requested behavior is implemented
* relevant tests pass
* no safety invariant is weakened
* Release build passes when appropriate
* `git diff --check` passes
* no unrelated files were changed

Do not continue adding improvements after the requested goal is satisfied.

Stop and report.
