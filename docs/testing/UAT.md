# User acceptance testing

Status values are `Not run`, `Passed`, `Failed`, or `Blocked`. A row changes to `Passed` only when its evidence link is attached. Safe user messages must contain no prompt, SQL, parameters, scope values, tokens, or external response bodies.

| # | Scenario | Prerequisite | Steps | Expected state | User message | Audit evidence | Result |
|---:|---|---|---|---|---|---|---|
| 1 | Teams sales analysis | Assigned user and Teams SSO | Send an approved sales request; wait for card | Completed | Safe summary and Power BI action | request/correlation IDs and status transitions | Not run |
| 2 | Customer segmentation | Assigned user; supported intent | Submit segmentation request | Completed | Safe summary and link | accepted plan and completion audit | Not run |
| 3 | Category performance | Assigned user; supported category intent and DWH available | Submit a category-performance request | Completed | Safe summary and link | source=DWH, completion audit | Not run |
| 4 | Clarification | Ambiguous supported request | Submit; answer clarification | WaitingForClarification then accepted | Predefined clarification text | clarification and resubmission events | Not run |
| 5 | Revision | Owned completed request | Select revise; submit delta | New request completes; original unchanged | Revision acknowledgement | previousRequestId chain | Not run |
| 6 | Completed link | Configured Power BI access | Complete a request; open action | Completed | HTTPS app.powerbi.com link | report completion, no URL credentials | Not run |
| 7 | Rejected card | Policy-unsafe request | Submit unsafe request | Rejected | Safe rejection message | rejection decision without SQL | Not run |
| 8 | Failed card | Inject technical provider failure | Submit otherwise valid request | Failed | Generic safe failure | error code and correlation only | Not run |
| 9 | Tokenless API | API reachable | GET report without bearer token | 401 | Authentication challenge only | HTTP status/correlation | Not run |
| 10 | Wrong scope | Token lacks delegated scope | Call protected endpoint | 403 | No report data | authorization failure audit | Not run |
| 11 | Wrong app role | Token lacks allowed app role | Call protected endpoint | 403 | No report data | role-policy failure | Not run |
| 12 | Other user's report | Two assigned users | User B reads user A request ID | 404 | Not found | ownership denial | Not run |
| 13 | No assignment | Valid identity without assignment | Create request | 403 | Access denied | assignment resolution failure | Not run |
| 14 | Unsupported store scope | Store-restricted user; unsupported SQL mapping | Submit request | Failed closed | Generic unsupported-scope failure | scope compatibility code, no values | Not run |
| 15 | Unsafe SQL | Fake/approved test plan with unsafe SQL | Process plan | Rejected | Safe rejection | guardrail decision | Not run |
| 16 | SQL secrecy | Any terminal request | Inspect GET/history/card/logs | No SQL/parameters exposed | Normal safe response | redaction review | Not run |
| 17 | URL credential safety | Completed Power BI response | Inspect returned URL/query | Completed only if credential-free | Safe report link | URL validation evidence | Not run |
| 18 | DWH routing | DWH plan | Process request | DWH client invoked | Normal processing message | source routing audit | Not run |
| 19 | OLTP routing | OLTP enabled and plan | Process request | OLTP client invoked | Normal processing message | source routing audit | Not run |
| 20 | No fallback | Selected source unavailable | Process request | Failed | Generic failure | one selected source, no fallback | Not run |
| 21 | OLTP tighter bounds | DWH/OLTP config available | Compare timeout/row/result limits | OLTP limits stricter | N/A | configuration review | Not run |
| 22 | Query bound exceeded | Fake result exceeds bound | Execute query | Failed | Generic bounded-result failure | limit error code | Not run |
| 23 | Fabric timeout/failure | Approved contract and fake or real job | Force timeout/failure | Failed | Generic analytics failure | bounded attempts/status only | Blocked: contract missing |
| 24 | Power BI report absent | Fake API returns 404 | Process valid request | Failed | Configured report unavailable | safe provider code/status | Not run |
| 25 | Outbox retry | Transient publisher failure | Dispatch twice | Pending then Published | No duplicate user data | attempt timestamps/status | Not run |
| 26 | Service Bus duplicate | Same message ID twice | Deliver duplicates | One effective transition | Single terminal notification | duplicate handling record | Not run |
| 27 | Notification retry | Teams target initially unavailable | Dispatch until ready | Published after bounded retry | One eventual card | outbox attempts/delivery key | Not run |
| 28 | Card-action duplicate | Submit same action twice | Observe claims/results | One action result | Idempotent acknowledgement | durable action claim | Not run |
| 29 | Restart persistence | SQL provider and request state | Restart API; read state | State preserved | Same status/history | database row and post-restart GET | Not run |
| 30 | DLQ operations | Poison Service Bus message | Exhaust delivery; inspect DLQ | DLQ entry recorded | No false completion | message ID and operational ticket | Not run |

UAT sign-off requires product owner, security owner, data owner, and operations owner. External rows require actual resource identifiers supplied outside source control and evidence from that environment.
