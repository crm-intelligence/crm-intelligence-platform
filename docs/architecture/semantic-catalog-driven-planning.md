# Semantic-catalog-driven query planning

## Security boundary and runtime flow

The system does not memorize user sentences. For `/planned` requests, Copilot Studio's
strict semantic JSON is the primary semantic interpretation:

`natural language -> Copilot Studio -> strict semantic JSON -> backend contract/catalog/security validation -> canonical request -> data scope -> deterministic Query Builder -> Fabric/OLTP`

The backend does not reconstruct metric, grouping, filters, date or ranking from the raw
prompt when this structurally valid plan is submitted. Natural-language resolution is an
advisory/fallback signal. Explicit literal date/year anchors may require clarification when
they conflict with a plan; linguistic disagreement alone does not override or reject it.

Teams clarification and revision actions use the same Copilot Studio planner as the initial
request. Teams first reads an ownership-filtered planning context from the API, sends that
semantic-only context to Copilot Studio, and posts the returned full plan to an explicit
planned endpoint:

- clarification: `GET /api/report-requests/{id}/planning-context` -> Copilot Studio ->
  `POST /api/report-requests/{id}/planned-clarification`
- revision: `GET /api/report-requests/{id}/planning-context` -> Copilot Studio ->
  `POST /api/report-requests/{id}/planned-revision`

The planned endpoints never perform semantic planning. They validate ownership, application
permission, current data-scope assignment, request lifecycle and the submitted full plan,
then enqueue the existing processing pipeline with that plan. Clarification resumes the same
logical request. Revision creates a child request with `PreviousRequestId` set to the source
request. Existing Teams action-token claims and notification-target transfer remain outside
the planner and preserve duplicate-click and proactive-notification behavior.

The Copilot Studio agent instructions for this contract are:

```text
INITIAL: Plan the user request normally and return one full semantic plan.

CLARIFICATION: Treat the current semantic plan as PRIMARY context. Use the user answer only
to complete the missing or ambiguous field. Preserve every field not explicitly changed.
Return a full semantic plan. Never interpret the answer as a standalone request.

REVISION: Treat the current accepted semantic plan as the baseline. Override only fields
explicitly changed by the revision instruction. Preserve all other metric, groupBy, filters,
date and ranking values. Return a full semantic plan, never delta JSON. Never reinterpret the
old prompt from scratch or interpret the revision instruction as a standalone request.
```

The structured context contains mode, original request, current semantic plan, clarification
question when applicable, and the user's answer or revision instruction. It contains no SQL,
physical schema, connection details, authorization scope, data scope or Teams OAuth bearer.

For requests without a submitted Copilot plan, `Ollama:PlanningMode` selects one of two
runtime flows. The active LLM-first flow is:

`natural language -> Qwen structured semantic extraction -> strict JSON validation -> semantic catalog/operation/compatibility validation -> deterministic source selection -> deterministic date calculation -> canonical validation -> data scope -> deterministic Query Builder -> Fabric/OLTP`

`EmbeddingFirst` retains the previous deterministic/embedding/discriminator pipeline as a
rollback mode. `LlmFirst` does not call that pipeline as its primary planner; embeddings may
later be used only to reduce catalog context.

The model never selects a table, view, column, join, authorization scope, timeout or execution policy. It never produces SQL. `DeterministicQueryBuilder` is the only SQL generation point and remains the security boundary before the guardrail pipeline.

The runtime `SemanticCatalogRegistry` is the authoritative source shared by the deterministic parser, Ollama prompt projection, Ollama JSON Schema, backend compatibility validation and Query Builder runtime. The LLM-first model-facing projection contains only semantic keys, labels, business descriptions, conceptual Turkish/English aliases and allowed semantic operations. Source names, source compatibility, physical objects, columns, SQL expressions and query-mapping references stay in the backend. Source is selected by intersecting authoritative catalog compatibility after model output validation.

The LLM-first structured output has the same three fail-closed outcomes (`accepted`,
`needs_clarification`, `unsupported`) but carries an extracted semantic intent instead of a
model-built `CanonicalRequest`. Accepted intent contains metric, group-by dimensions,
dimension/operator/literal filters, semantic date meaning and optional top-N ranking. Relative
date tokens such as `previous_month`, `last_n_days` and `current_year` are converted to exact
dates by `RelativeDateResolver`; Qwen does no calendar arithmetic. Resolver agreement is
secret-safe advisory telemetry after structured-plan validation. Turkish month names are
Unicode/casing normalized and tolerate a single substitution, insertion, deletion or adjacent
transposition for fallback/advisory resolution. Month names, minor typos, word order and
colloquial phrasing do not cause hard rejection of a valid structured plan.

## Extending the catalog

A new metric does not require model retraining, parser changes, a system-prompt edit or sentence rules. Normally the change consists of:

1. Add the semantic definition and conceptual aliases to the catalog.
2. Add its reviewed deterministic mapping reference/expression and allow-list-compatible source mapping.
3. Add catalog, compatibility and Query Builder validation tests.

The embedding index and every model constraint are derived at runtime. A request receives full-normalized, deterministic-date-masked, safe-filter-value-masked, clause and configurable 1..N-token semantic views. Metric and dimension scoring use separate catalog partitions. Date resolution remains deterministic and filter keys remain separate from literal values.

`SemanticSlotIntentDetector` detects only requested slot types (metric, grouping/dimension, date, filter, comparison, ranking and explicit source); it has no catalog and cannot choose a semantic key. Generic Turkish grouping forms such as `gore`, `bazinda`, `basina`, `kirilim`, distribution/comparison forms, repeated group nouns and plural group forms are treated as slot requirements rather than sentence aliases.

When every required slot is resolved and source/compatibility checks pass, `SemanticCompletenessGate` permits `SemanticCanonicalRequestAssembler` to create a canonical request. High embedding confidence alone is insufficient: deterministic bypass also requires generic lexical evidence from the catalog projection. Medium-confidence candidates can be sent only to candidate-constrained partial Qwen; this does not lower the deterministic bypass threshold. Missing or ambiguous metric/dimension slots with candidates use the same partial path while resolved metric/date/dimensions remain immutable. Missing filters, dates, ranking context, sources, or candidate-free slots clarify. Unsupported slots stop before Qwen, canonical validation, Query Builder and SQL generation.

Aliases describe concepts, not full requests. Complete example sentences exist only in test projects and the local quality suite. They are not copied into production catalog aliases or prompt-builder code.

## Semantic status from repository contracts

The classifications below are based on `metric_catalog.olist.json`, `metric_catalog.fabric-oltp-v1.json`, their allow-lists, `olist_views.contract.sql`, and the repository's 2026-08-05 Fabric metadata evidence. No live data/schema mutation or deployment was performed.

| Classification | Semantic concepts | Reason |
|---|---|---|
| Supported | `freight_total`, `order_count`, `item_count`, `payment_total`, `avg_installments`, `customer_count`, `avg_monetary`, `avg_frequency` | A deterministic expression, allow-listed source and compatible dimensions/filters are present. |
| Ambiguous | `item_sales`, `customer_paid_total`, `avg_basket` | Query mappings exist, but the business definition of "net sales" (freight included or excluded) is still explicitly pending; no `net_sales` key is invented. |
| Unsupported | `recency_days`, campaign performance, profitability/margin concepts | The reference-date/business contract or required source data is missing. No substitute metric is selected. |
| Source-specific | payment metrics with `payment_type`/`payment_order_timestamp`; RFM customer metrics with `rfm_customer_state`/`rfm_customer_city`; OLTP operational dimensions | Compatibility is limited to the metadata-proven physical source. Cross-source combinations fail backend validation. |

## Clarification and follow-up

`SemanticPlanningState` carries the typed intent, resolved metric/dimensions/date/filters/source, candidate gaps and a `SemanticCompletenessResult` containing required, resolved, missing, ambiguous and unsupported slots plus reason codes. Missing is not treated as unsupported: missing required information produces a backend-owned dynamic clarification, while an explicitly requested catalog-absent concept produces the unsupported response.

The model-facing `PlanningResult` has exactly four fields: `outcome`, `canonicalRequest`, `unresolvedConcepts`, and `clarification`. Its three schema-enforced branches are:

- `accepted`: canonical request is required; unresolved concepts are empty; clarification is null.
- `needs_clarification`: canonical request is null; the typed clarification slot is present; Query Builder is not called.
- `unsupported`: canonical request is null; at least one typed unresolved concept is present; clarification is null; Query Builder is not called.

Unresolved concepts carry only a `kind` from `metric`, `dimension`, `filter`, `date`, or `source`. Raw prompt fragments are not part of this type. User-facing clarification wording remains backend-owned.

Clarification questions are derived from missing semantic fields: metric, date range, required breakdown dimension or source. The original request, clarification question and the single answer are sent as separate messages on retry, so the user does not have to repeat prior values. A second ambiguous result remains a safe clarification; it is not converted into a technical failure.

Model confidence is advisory. Accepted envelopes with any unresolved concept, null canonical requests, legacy canonical unresolved terms, or clarification data are rejected independently of confidence. Unsupported envelopes with a canonical request and needs-clarification envelopes without a backend-safe clarification slot are also rejected. Unknown keys, incompatible metric/dimension/filter/source selections, non-filterable fields and invalid dates fail closed before SQL generation. Filter values remain separate from dimension keys, are parameterized, and data-scope enforcement is applied by the backend guardrail.

## Logging and operations

Planning telemetry contains `SubmittedPlan`, `LlmFirst`, `DeterministicParser`, `EmbeddingDeterministic`, `EmbeddingPlusQwen`, `Clarification` or `Unsupported`, plus outcome, reason code, schema/backend validation flags, typed unresolved kinds, representation type, timings and token counts. Raw semantic representations, unresolved expressions, prompts, model responses, SQL, filter values and user identity are excluded from audit/log contracts. The `LlmFirstSmall` smoke suite contains eight supported regression cases plus U23 and X01/X03/X06; it reports only case identifiers, semantic keys and safety/correctness flags.

Submitted-plan telemetry also records semantic source `copilot`, deterministic resolver
agreement, a bounded divergence category, explicit-anchor conflict and final validation
decision. It never records the prompt or filter literals.

Calibration examples and unsupported-calibration examples are separate test-only data. H01-H12 are explicitly the regression set, not unseen data. A separate `NewUnseen` set is kept only in the evaluation tool and is not projected into aliases or prompts. The configured `.70` deterministic minimum similarity and `.02` margin are not lowered; `.55` is only a partial-Qwen candidate floor and still requires generic lexical evidence.

Every evaluation startup reports the active thresholds, candidate count, planning model, embedding model, Release build timestamp, binary SHA-256 and repository fingerprint. The runner refuses non-Release or stale `--no-build` binaries before model or evaluation work.

This semantic change does not create Azure resources or mutate database schemas/data. API
revisions use the existing image and Container Apps deployment path; Teams is independent.
