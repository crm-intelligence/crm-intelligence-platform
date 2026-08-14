# Operations runbook

## Health and correlation

Use `/health/live` only for process liveness. `/health/ready` covers registered SQL application database, outbox, messaging runtime, query sources, and other readiness checks. Public output contains check names and statuses only. Do not place connection strings, hosts, database names, workspace/item/report IDs, tokens, exception details, prompts, SQL, parameter values, or user data in logs or tickets.

Start an investigation with request ID, correlation ID, UTC interval, deployment revision, and safe error code. Application audit is the status/authorization trail; platform logs are the technical trail. Token and external response bodies are never diagnostic attachments.

## Outbox and messaging

For Pending outbox records, inspect attempt count, next-attempt time, lock expiry, message type, and last safe error code. Confirm dispatcher and messaging readiness before retrying. For DeadLettered records, open an incident, establish whether the consumer defect/configuration is fixed, then use an approved replay procedure; do not edit payloads in the database.

For Service Bus DLQ, inspect message ID, delivery count, enqueued time, dead-letter reason, and correlation metadata. Do not copy application bodies into chat/tickets. Duplicate delivery is expected under at-least-once delivery; durable request transitions, notification delivery IDs, and card-action claims provide idempotency boundaries, not global exactly-once execution.

For capability-routed submissions, `deterministic` must produce one
`ReportProcessingRequested` record. `agentic_required` and `unsupported` must produce none;
an agentic request remains `Processing` while the external Copilot SQL Reasoning Agent uses
the request ID, V2 intent and returned context fingerprint with `/api/sql-agent/tools/*` and
`/api/sql-agent/candidates`. Do not repair a routing error by deleting an outbox record,
cancelling a queue message or changing request fields directly.

During Copilot Studio migration use `planned-routed`, `planned-revision-routed` and
`planned-clarification-routed`, each with the full V1 plan and V2 intent in one request. Never
chain the legacy `/planned` endpoint to `/api/sql-agent/capabilities/analyze`; the legacy call
commits deterministic dispatch before capability analysis and therefore reintroduces the
race.

## Migrations and rollback

Startup must not migrate the database. Download the idempotent SQL artifact, have a DBA review target, backup/recovery posture, locks, and rollback implications, then apply through the approved database pipeline. Record script SHA and migration IDs.

Container Apps uses multiple revisions. Keep the previous known-good revision, validate the new revision's platform health and smoke tests, then move traffic with approval. Roll back by moving traffic to the previous healthy revision; a database migration may require a separately reviewed forward fix and must never be automatically reversed.

## Teams

Check the public `/api/messages` Bot/Teams endpoint, Teams app manifest IDs held by DevOps, OAuth connection, callback API-key Key Vault reference, proactive target registration, and delivery store. A 401 on an unauthenticated internal callback is expected. Rotate non-managed credentials in Key Vault, create a new revision, validate it, and then retire the old secret version.

## Fabric and Power BI

Fabric activation is blocked until an approved durable result staging and job parameter contract exists. For an approved job failure, use workspace/item/job-instance metadata and status codes only; never log notebook output or response bodies. Check managed identity workspace permissions, throttling, timeout, and the pre-created item without creating or modifying it.

For Power BI, check configured GUIDs, managed identity/service principal workspace access, report existence, HTTPS `app.powerbi.com` web URL, and optional refresh history. Refresh failure is explicit and produces a failed report with current configuration. Do not generate embed tokens or append filters, identity data, or credentials to the link.

## Identity rotation

Managed identity is preferred and has no application secret to rotate. For Key Vault-backed Bot/internal API values, add a new version, verify identity `Secrets User` access, deploy a revision, run smoke tests, shift traffic, then disable the old version. Federated GitHub credentials are owned by DevOps and should restrict issuer, repository, branch/environment subject, and audience.
