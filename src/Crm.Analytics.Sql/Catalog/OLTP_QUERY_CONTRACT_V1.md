# Fabric OLTP Query Contract V1

Contract version: `fabric-oltp-operational-orders-v1`<br>
Physical read object: `dbo.vw_operational_orders`<br>
Source: Fabric SQL Database OLTP<br>
Discovery date: 2026-08-05

## Metadata evidence

Metadata was read with the current user's existing Entra session. Only
`sys.schemas`, `sys.views`, `sys.columns`, `sys.types`, `sys.computed_columns`,
`sys.extended_properties`, and `OBJECT_DEFINITION` were queried. No data row was
read and no Fabric object, permission, or data was changed.

The view exists and exposes 19 columns. Its definition starts from
`dbo.orders AS o`; item and payment inputs are each grouped by `order_id` before
joining to the order row. This proves an order-level grain and makes `order_id`
the stable unique tie-breaker. `order_purchase_timestamp, order_id` is the v1
deterministic ordering combination.

| # | Column | SQL type | Nullable | Lineage/capability decision |
|---:|---|---|---|---|
| 1 | `order_id` | `varchar(50)` | no | Direct `o.order_id`; selectable, equality-filterable, sortable; order-grain identifier |
| 2 | `customer_id` | `varchar(50)` | no | Excluded: identity/security classification not proven |
| 3 | `customer_unique_id` | `varchar(50)` | no | Excluded: identity/security classification not proven |
| 4 | `customer_city` | `varchar(100)` | no | Direct lineage; selectable, equality-filterable, sortable |
| 5 | `customer_state` | `char(2)` | no | Direct lineage; selectable, equality-filterable, sortable, runtime scope column |
| 6 | `order_status` | `varchar(30)` | no | Selectable/sortable only; enum values were not proven, so filtering is disabled |
| 7 | `order_purchase_timestamp` | `datetime2(6)` | no | Direct lineage; selectable, date-range-filterable, sortable; primary stable-order column |
| 8 | `approved_at` | `datetime2(6)` | yes | Metadata-proven; allowlisted object column, not exposed as a v1 intent term |
| 9 | `shipped_at` | `datetime2(6)` | yes | Metadata-proven; allowlisted object column, not exposed as a v1 intent term |
| 10 | `delivered_at` | `datetime2(6)` | yes | Selectable/sortable; null-intent mapping is not enabled |
| 11 | `cancelled_at` | `datetime2(6)` | yes | Metadata-proven; allowlisted object column, not exposed as a v1 intent term |
| 12 | `total_quantity` | `int` | yes | Excluded from v1 Query Builder: derived measure not needed by minimum detail contract |
| 13 | `product_total` | `decimal(38,2)` | yes | Excluded from v1 Query Builder: no new business metric approved |
| 14 | `freight_total` | `decimal(38,2)` | yes | Excluded from v1 Query Builder: no new business metric approved |
| 15 | `order_total` | `decimal(38,2)` | yes | Excluded from v1 Query Builder: no new business metric approved |
| 16 | `payment_total` | `decimal(38,2)` | yes | Excluded from v1 Query Builder: no new business metric approved |
| 17 | `payment_status` | `varchar(30)` | yes | Excluded: enum and operational semantics not documented |
| 18 | `payment_type` | `varchar(30)` | yes | Excluded from minimum operational detail surface |
| 19 | `payment_installments` | `int` | yes | Excluded from minimum operational detail surface |

No column extended descriptions and no computed-column flags were present.

## Query capabilities

- Metrics: none. V1 does not create `COUNT(*)`, `SUM`, `AVG`, or a business KPI.
- Query shape: one SELECT statement, fixed schema-qualified view, explicit
  allowlisted columns, bounded TOP, parameters, and allowlisted ORDER BY.
- Default/max rows: 100/1000.
- Command timeout: 15 seconds maximum.
- Grouping/aggregation: disabled for the OLTP catalog.
- Status intents such as “open” are clarification-only until an approved enum
  contract exists. “Not delivered” is also clarification-only until an approved
  null/terminal-state rule exists.
- Hour-level relative time is unsupported by the DateOnly canonical v1 contract
  and returns clarification rather than widening to a day.
- Filter values are parameters. End dates use an exclusive next-day parameter so
  a `datetime2` day range includes the complete requested day.

## Source selection and revision

An explicit source is validated only against that source catalog. Without an
explicit source, exactly one catalog must resolve the complete request; both or
neither produce the existing clarification status. DWH and OLTP catalogs are
never combined in a Query Builder context.

Canonical requests carry `source`. A revision without source preserves the
previous source. A requested source change is reparsed as a complete request
against the new catalog and never carries old metrics/objects across. Revision
supports bounded limit, sortable catalog keys, direction, stable tie-breaker,
and explicit single-value filter add/replace.

## Isolation and security

The only OLTP object is `dbo.vw_operational_orders`. Base tables and DWH `mart.*`
objects are absent. The DWH contract remains `fabric-dwh-object-mapping-v1` and
does not contain the OLTP view. Upstream and backend policies reject DML, DDL,
EXEC, SELECT INTO, multiple statements, unqualified/wrong-schema/three-part
objects, dynamic table references, and confusable identifiers. Bracketed
`[dbo].[vw_operational_orders]` canonicalizes to the same approved object.

## Fabric compatibility

Current-user metadata access: successful. Current-user no-row compile checks are
successful for detail, parameterized date filter, parameterized city filter, and
Top N deterministic ordering. `sys.dm_exec_describe_first_result_set` also
accepted the parameterized date/Top N shape. Every execution used `TOP (0)` and
`WHERE 1=0`; no data row was read. Status filtering was intentionally not tested
because the v1 contract disables it. Runtime UAMI authentication is not tested
here; it is a post-deployment smoke test.
