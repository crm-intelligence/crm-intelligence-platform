# Gerçekleşen Backend ve Microsoft Teams Mimarisi

Bu belge, 2 Ağustos 2026 tarihli repository içeriğine göre **as-built** durumunu
anlatır. Hedef mimari taslağındaki adlar yerine çalışan kodun route, domain,
persistence ve provider sözleşmeleri esas alınmıştır. Bu belge bir production
hazır olma onayı değildir; gerçek dış kaynak kanıtı bulunmayan alanlar açıkça
`Not implemented`, `Not run` veya `Blocked` olarak işaretlenmiştir.

## 1. Sistem özeti

Microsoft Teams kullanıcı arayüzüdür. Teams host, Teams SDK üzerinden mesaj,
sign-in ve Adaptive Card action activity'lerini alır; kullanıcının rapor talebini
ASP.NET Core API'ye iletir. ASP.NET Core API kimlik doğrulama, rol/izin ve veri
kapsamı yetkilendirmesi, conversation/request sahipliği, persistence,
orchestration, audit, outbox ve worker yaşam döngüsünü yönetir.

Create, revision ve clarification çağrılarının ilk başarılı HTTP cevabı `202
Accepted` olur. Bu cevap raporun tamamlandığı anlamına gelmez; request ile onu
işleme alacak outbox kaydının transaction içinde kabul edildiğini ifade eder.
SQL üretimi, sorgu çalıştırma, analytics ve Power BI işlemleri HTTP isteği açık
tutularak yapılmaz. Son durum `Completed`, `WaitingForClarification`, `Rejected`
veya `Failed` olduğunda notification outbox üzerinden aynı Teams conversation'a
proactive Adaptive Card gönderilir.

As-built worker ayrı bir executable veya ayrı Container App değildir. Azure
Service Bus consumer, outbox dispatcher ve işleme handler'ları
`CrmAnalytics.Api` process'i içinde çalışan hosted service'lerdir. Bicep iki
uygulama tanımlar: internal ingress'li API/worker ve public ingress'li Teams host.

### Teknoloji tabanı

| Bileşen | Repository sürümü |
|---|---|
| .NET SDK / target framework | SDK `10.0.301` (`latestFeature` roll-forward), `net10.0` |
| ASP.NET Core OpenAPI | `10.0.9` |
| EF Core SQL Server/Design | `10.0.10` |
| Microsoft Identity Web | `4.14.1` |
| Azure Service Bus | `7.20.2` |
| Microsoft.Data.SqlClient | `7.0.2` |
| Microsoft Teams ASP.NET Core plugin | `2.0.9` |

`CrmAnalytics.Infrastructure` ayrıca repository dışında beklenen
`Crm.Analytics.Sql.csproj` sibling project'ine `ProjectReference` taşır. Bu kaynak
mevcut workspace'te yoktur; temiz build/CI için onaylı harici repository
konfigürasyonu gerekir.

## 2. Gerçek uçtan uca akış

1. Kullanıcı Teams'e prompt gönderir. Teams host conversation ID'yi activity'den
   alır ve prompt/conversation uzunluklarını doğrular.
2. Entra modunda Teams SDK OAuth connection üzerinden delegated SSO access
   token'ını alır. Token yoksa sign-in başlatılır; ilk prompt saklanmaz ve
   kullanıcıdan sign-in sonrasında talebi yeniden göndermesi istenir.
3. Teams host token'ı yalnız ilgili backend `HttpRequestMessage` nesnesine
   `Authorization: Bearer` olarak ekler. API, Microsoft Identity Web ile imza,
   issuer/audience ve token geçerliliğini; `Reports.Access` requirement'ı ile
   `access_as_user`, `oid`, `tid` ve kullanıcı kimliği semantiğini doğrular.
4. Operation policy, token'daki exact/case-sensitive app role değerini gerekli
   application permission'a çevirir. Create/revision/clarification ayrıca güncel
   `tid + oid` data-scope assignment'ını fail-closed çözer. GET/history için
   permission ve sahiplik uygulanır; güncel assignment aranmaz.
5. API aynı kısa application transaction'ında `ReportRequest`, conversation
   ilişkisi, application audit event'i ve `ReportProcessingRequested` outbox
   mesajını yazar. Request ilk olarak `Received` durumundadır.
6. Transaction commit edildikten sonra API `202 Accepted` döner. Teams host
   request ID ile conversation ID eşlemesini API'nin internal target endpoint'ine
   yazar ve kullanıcıya işlem numarasını içeren kabul mesajı gönderir.
7. Outbox dispatcher pending processing mesajını claim eder ve seçilen messaging
   provider'a yayınlar. Production varsayılanı Azure Service Bus'tır; Development
   için in-memory channel seçilebilir. Service Bus mesajı request ID, correlation
   ID ve zaman metadata'sı taşır; prompt, token, SQL, scope listesi veya sonuç
   satırları taşımaz.
8. Azure Service Bus consumer PeekLock/manual-complete ile envelope'u doğrular.
   Kalıcı/geçersiz mesajları DLQ'ya, transient hataları retry için abandon'a
   yönlendirir. Handler request'i yeniden yükler ve güncel data-scope assignment'ı
   tekrar çözer.
9. Processing service request'i `Validating` durumuna geçirir. Gerçek SQL
   production adapter'ı prompt, güvenli scope contract'ı ve varsa önceki
   canonical context ile `Crm.Analytics.Sql` servisini çağırır. Guardrail kararı
   `Accepted`, `NeedsClarification`, `Rejected` veya teknik failure olarak
   yorumlanır. Clarification/rejection terminal veya bekleme mutation'ları da
   audit ve notification outbox ile aynı transaction sınırına katılır.
10. `Accepted` kararındaki `SqlExecutionPlan` SQL, typed parameters, uygulanmış
    scope filter, timeout, sonuç şekli ve `Source` içerir. Executor `Source`
    değerine göre yalnız DWH veya yalnız OLTP connection'ını açar; diğer kaynağa
    fallback yapmaz. Timeout, satır/kolon/hücre/byte limitleri kaynağa özgüdür.
11. Bounded sorgu sonucu analytics provider'a gider. `Direct` yalnız row count,
    source ve truncation metadata'sıyla özet üretir. `FabricJob` HTTP/token/retry/
    poll koduna sahip olsa da gerçek sonuç staging contract'ı olmadığı için
    query result mevcutken `FABRIC_STAGING_CONTRACT_MISSING` ile fail-closed olur.
12. Analytics tamamlanırsa report provider çağrılır. `PowerBi` provider isteğe
    bağlı semantic model refresh'inden sonra yalnız yapılandırılmış report'un
    ID'sini ve credential içermeyen `https://app.powerbi.com/...` `webUrl`'ini
    kabul eder.
13. `Completed`, `WaitingForClarification`, `Rejected` veya `Failed` mutation'ı,
    application audit ve `ReportNotificationRequested` outbox kaydı aynı kısa
    transaction içinde yazılır.
14. Notification outbox publisher kalıcı request-to-conversation target'ını
    yükler; request + status + update time'dan deterministik delivery ID üretir
    ve SQL delivery store'da claim eder. Ardından Teams host'un
    `POST /api/internal/report-notifications` callback'ini API key ile çağırır.
15. Teams host Adaptive Card'ı proactive olarak conversation'a gönderir. Send
    başarılı olduktan sonra backend delivery kaydı `Delivered` yapılır. Teslimat
    at-least-once'dur: Teams send ile `Delivered` kaydı arasındaki crash duplicate
    kart oluşturabilir; sistem global exactly-once garantisi vermez.

## 3. Gerçek API endpoint'leri

### 3.1 CrmAnalytics.Api kullanıcı endpoint'leri

Tüm kullanıcı endpoint'leri controller seviyesinde `Reports.Access` ister:
authenticated delegated bearer principal, required `access_as_user` scope,
`oid` ve `tid`. Aşağıdaki operation policy buna eklenir. Development modu yalnız
Development ortamında fake principal üretir ve gerçek authentication değildir.

| Method | Route | Policy ve varsayılan roller | Request modeli | Başarılı response | Diğer kodlanmış sonuçlar | Sınıf |
|---|---|---|---|---|---|---|
| `POST` | `/api/report-requests` | `Reports.Create`; `Report.User`, `Report.Admin` | `CreateReportRequestRequest`: `Prompt` 3–2000, `ConversationId` 1–256, optional `PreviousRequestId` max 64 | `202 CreateReportRequestResponse` (`RequestId`, raw `Received` status, message) | model `400`; auth `401/403`; eksik data-scope `403`; global safe `409/500` mümkün | Public/user-facing |
| `GET` | `/api/report-requests/{requestId}` | `Reports.ReadOwn`; `Report.User`, `Report.Viewer`, `Report.Admin`; aynı `oid+tid` ownership | Yok | `200 GetReportRequestResponse` | bulunamayan veya başka kullanıcıya ait kayıt `404`; auth `401/403`; global `500` | Public/user-facing |
| `POST` | `/api/report-requests/{requestId}/revise` | `Reports.ReviseOwn`; `Report.User`, `Report.Admin`; ownership + data-scope | `ReviseReportRequestRequest`: `Prompt` 3–2000 | `202 ReviseReportRequestResponse`; yeni request ID, `PreviousRequestId` ve raw `Received` status | model `400`; auth/data-scope `401/403`; `404`; yalnız completed kaynak revize edilebildiği için `409`; global `500` | Public/user-facing |
| `POST` | `/api/report-requests/{requestId}/clarifications` | `Reports.ClarifyOwn`; `Report.User`, `Report.Admin`; ownership + data-scope | `SubmitReportClarificationRequest`: `Response` 3–2000 | `202 SubmitReportClarificationResponse`; aynı request ID ve worker yeniden alana kadar raw `WaitingForClarification` status | model `400`; auth/data-scope `401/403`; `404`; aktif clarification yoksa/cevap zaten verildiyse `409`; global `500` | Public/user-facing |
| `GET` | `/api/conversations/{conversationId}/report-requests` | `Reports.ViewOwnHistory`; `Report.User`, `Report.Viewer`, `Report.Admin`; history `oid+tid` ile filtrelenir | Yok | `200 GetConversationReportRequestsResponse` | boş veya kullanıcıya ait kaydı olmayan conversation `404`; auth `401/403`; global `500` | Public/user-facing |

Role listesi varsayılan `appsettings.json` mapping'idir; authorization kodu role
adlarını hard-code ederek değil configuration'daki exact permission mapping'iyle
uygular. `Report.Admin` ownership bypass etmez.

### 3.2 CrmAnalytics.Api internal Teams endpoint'leri

Controller `[AllowAnonymous]` taşır; dolayısıyla ASP.NET bearer policy'si yoktur.
Bunun yerine her action, `X-CrmAnalytics-Notification-Key` değerini SHA-256 +
fixed-time comparison ile doğrular. Boş yapılandırılmış key de çağrıyı reddeder.
Bu endpoint'lerde app role veya user policy yoktur.

| Method | Route | Request modeli | Başarılı response | Diğer response'lar | Sınıf |
|---|---|---|---|---|---|
| `PUT` | `/api/internal/teams-targets/{requestId}` | `RegisterTeamsTargetRequest { ConversationId }` | `204` | `400`, `401`, target değiştirilmeye çalışılırsa `409` | Internal/S2S |
| `GET` | `/api/internal/teams-targets/{requestId}` | Yok | `200 TeamsTargetResponse` | `400`, `401`, `404` | Internal/S2S |
| `POST` | `/api/internal/teams-actions/{actionToken}/claim` | `ClaimTeamsActionRequest { RequestId, ActionType, LockOwner }` | `200 ClaimTeamsActionResponse { Result, ResultRequestId }` | `400`, `401`, identity conflict'te `409` | Internal/S2S |
| `POST` | `/api/internal/teams-actions/{actionToken}/complete` | `CompleteTeamsActionRequest { LockOwner, ResultRequestId? }` | `204` | `400`, `401`, geçersiz/expired claim'de `409` | Internal/S2S |
| `POST` | `/api/internal/teams-actions/{actionToken}/release` | `ReleaseTeamsActionRequest { LockOwner }` | `204` | `400`, `401`, geçersiz claim'de `409` | Internal/S2S |

### 3.3 Controller dışındaki host route'ları

| Host | Method/route | Authentication | Request/response ve görünürlük |
|---|---|---|---|
| API | `GET /health`, `GET /health/live`, `GET /health/ready` | Anonymous | Safe status/check-name JSON; healthy `200`, unhealthy ready/all check `503`; public operational |
| API | `GET /openapi/v1.json` | Anonymous, yalnız Development | Üretilen OpenAPI document; Development-only |
| Teams | `POST /api/messages` | Teams SDK/Bot authentication; yalnız Development'ta açık `SkipAuth` mümkün | Teams Activity envelope; message, sign-in ve Adaptive Card invoke aynı SDK endpoint'inden işlenir; public Bot endpoint |
| Teams | `POST /api/internal/report-notifications` | `X-CrmAnalytics-Notification-Key` | `InternalReportStatusNotificationRequest`; `204`, `400`, `401`, disabled ise `404`, legacy target hazır değilse `409`, send hatasında `503`; internal/S2S |
| Teams | `GET /health`, `GET /health/live` | Anonymous | Statik healthy JSON, `200`; public operational |
| Teams | `GET /health/ready` | Anonymous | Backend API readiness; healthy `200`, unhealthy `503`; public operational |

## 4. Internal ve kullanıcı-facing durum modeli

### Domain durumları ve transition'lar

| Internal status | İzin verilen sonraki durumlar | Mevcut pipeline kullanımı |
|---|---|---|
| `Received` | `Validating`, `Failed` | Create/revision ilk durumu; clarification cevabı aynı waiting request üzerinde kalır ve worker resume eder |
| `Validating` | `Queued`, `Processing`, `WaitingForClarification`, `Failed`, `Rejected` | SQL production/guardrail öncesi aktif kullanılır |
| `Queued` | `Running`, `Failed` | Domain'de vardır; mevcut outbox/Service Bus worker bunu set etmez |
| `Processing` | `WaitingForClarification`, `Completed`, `Failed`, `Rejected` | Analytics/report aşamasından önce aktif kullanılır |
| `Running` | `WaitingForClarification`, `Completed`, `Failed`, `Rejected` | Domain'de vardır; mevcut worker bunu set etmez |
| `WaitingForClarification` | `Validating`, `Processing`, `Failed` | Soru persist edildiğinde kullanılır; cevap submit edilince worker yeniden `Validating` yapar |
| `Completed` | Yok | Terminal |
| `Rejected` | Yok | Kontrollü guardrail/policy reddi; terminal |
| `Failed` | Yok | Teknik failure; terminal |

Fiili normal yol `Received → Validating → Processing → Completed` şeklindedir.
Clarification ve rejection/failure dalları `Validating` veya `Processing`
aşamasından ayrılır. `Queued` ve `Running` enum/transition contract'ının
parçasıdır, ancak broker queue state'ini domain request'e yansıtan kod yoktur.

### UI sadeleştirmesi ve public API gerçeği

| Internal durum(lar) | Kullanıcı-facing anlam |
|---|---|
| `Received` | Teams'te “talebiniz alındı” acknowledgement |
| `Validating`, `Queued`, `Processing`, `Running` | Sade UI semantiğinde `Processing`; bu durumlar için proactive kart üretilmez |
| `WaitingForClarification` | `NeedsClarification` anlamı; Teams kartında “Ek bilgi gerekiyor / Açıklama bekleniyor” mesajı ve cevap alanı |
| `Completed` | Doğrudan completed card; safe summary, revision input ve güvenliyse Power BI `OpenUrl` |
| `Rejected` | Doğrudan safe rejected card; teknik/guardrail ayrıntısı yok |
| `Failed` | Doğrudan generic failed card |

`NeedsClarification` kodda bir `ReportRequestStatus` veya public API status
değeri değildir. Public GET ve conversation history response'ları bugün internal
enum adını `ToString()` ile **aynen** döndürür; yani API seviyesinde
`Validating/Queued/Processing/Running → Processing` dönüşümü uygulanmamıştır.
Yukarıdaki mapping UI/read-model ayrımıdır ve mevcut public contract'ı değiştirmez.

## 5. DWH/OLTP sorumluluk sınırı

- SQL production request factory'nin default source'u `Dwh`'dır. Gerçek SQL
  production servisi tarafından döndürülen accepted `SqlExecutionPlan.Source`,
  executor için nihai routing bilgisidir. Query execution katmanı bunu yeniden
  yorumlamaz veya kullanıcı isteğinden seçmez.
- Backend doğru named connection/configuration'a route eder: `Dwh` yalnız DWH,
  `Oltp` yalnız OLTP seçeneklerini kullanır. Seçilen source disabled/unavailable
  ise işlem safe failure olur; otomatik DWH↔OLTP fallback yoktur.
- DWH analitik/tarihsel sorguların varsayılanıdır ve Production/Staging'de
  enabled olmak zorundadır. Repository production template'inde OLTP disabled'dır.
- OLTP yalnız iş birimi ve Data Engineering tarafından önceden onaylanmış güncel
  senaryolarda; ayrı read-only principal, dar view allow-list, daha kısa timeout,
  düşük sonuç limitleri ve operasyonel performans onayıyla açılmalıdır.
- API veya Teams SQL, source, timeout ya da parameter kabul etmez. Service Bus
  envelope'u execution plan taşımaz; worker request'i ve güncel policy'yi yeniden
  yükleyip planı yeniden üretir.
- Read-only view'lar, scope/RLS kolonları, database `GRANT SELECT` ve principal
  provisioning bu repository'nin değil Data Engineering/DBA sorumluluğudur.
  `ApplicationIntent=ReadOnly` tek başına yetki sınırı değildir.

`SqlProduction:Source` configuration alanı doğrulanmaktadır; fakat mevcut
registration/factory kodu bu değeri request factory'ye geçirmemekte, factory
default `Dwh` kullanmaktadır. As-built source routing yine accepted
`SqlExecutionPlan.Source` ile yapılır. Bu ayrıntı configuration'ın runtime source
override'ı gibi yorumlanmamalıdır.

## 6. Teams entegrasyonu

### SSO ve bearer forwarding

- Entra modunda Teams SDK token service her normal mesaj ve revise/clarification
  action için çağrılır. OAuth connection adı zorunludur; `SkipAuth` kapalıdır.
- Token Teams host'ta parse edilmez, claim/role kararı üretilmez, store'a veya
  loga yazılmaz. Yalnız create/revise/clarification backend request'ine
  request-local bearer header olarak forward edilir; OBO/Graph çağrısı yoktur.
- Token yoksa sign-in başlatılır. Pending prompt store olmadığı için kullanıcı
  başarılı sign-in sonrasında ilk talebi yeniden gönderir.
- Backend `401/403` response body Teams'e yansıtılmaz; kullanıcıya safe genel
  authentication/permission mesajı gösterilir.

### Conversation, revision ve clarification

- `ConversationId` Teams Activity'den gelir; create request'e ve sonrasında
  durable target kaydına yazılır. Conversation history aynı user+tenant'a göre
  filtrelenir.
- İlk create'te `PreviousRequestId = null` olur. Revision yalnız owned/completed
  source request'ten yeni request oluşturur; yeni kaydın `PreviousRequestId`'si
  source request ID'dir ve aynı conversation korunur.
- Clarification yeni request oluşturmaz. Cevap aynı
  `WaitingForClarification` request'ine yazılır, processing outbox'a
  `clarification` generation ile tekrar eklenir ve aynı notification target
  kullanılır.

### Proactive notification ve dayanıklılık

- `crm.TeamsNotificationTargets`, request ID → conversation ID eşlemesini kalıcı
  tutar. Production Teams host bunu internal backend endpoint'i üzerinden yönetir.
- `crm.TeamsNotificationDeliveries`, deterministik delivery ID, status/update
  time, attempt, lock ve delivered state tutar. Claim/release/delivered akışı
  retry'lerde deduplication sınırı sağlar.
- `crm.TeamsCardActionSubmissions`, SHA-256 action token, request, action type,
  lock owner/state ve optional result request ID tutar. `claim/complete/release`
  card-action idempotency sağlar. Revision yeni result request ID'yi kaydeder;
  clarification aynı request üzerinde tamamlanır.
- Protected environment'ta Teams process-local fallback store'ları no-op'tur;
  authoritative state backend SQL store'dadır. Development/test in-memory
  fallback kullanabilir ve restart'ta kaybolur.
- Delivery exactly-once değildir. Backend claim duplicate dispatch'i azaltır,
  fakat external Teams send ile durable `Delivered` update arasındaki crash
  duplicate kart üretebilir. Action mutation ile `complete` arasındaki crash de
  retry doğurabilir; domain status kontrolleri ikinci mutation'ı fail-closed
  reddeder.

### Adaptive Card ve Power BI

- Kartlar Adaptive Card `1.5` kullanır. Completed kartında revision input/action,
  clarification kartında response input/action bulunur. Rejected/Failed kartında
  mutation veya report action yoktur.
- Power BI action yalnız absolute HTTPS, exact `app.powerbi.com` host, boş
  `userinfo` ve well-formed URL için eklenir. URL uygun değilse completed kart
  gönderilir fakat `OpenUrl` eklenmez.
- Link bir authorization token'ı değildir. Son kullanıcının Power BI Entra/RLS
  erişimi ayrıca gerekir; backend ownership/data scope Power BI RLS'nin yerine
  geçmez.

## 7. Audit ve veri minimizasyonu

### Application audit'te saklanan alanlar

Kalıcı SQL modeli `crm.ApplicationAuditEvents` tablosudur. Event ID retry
idempotency için request ID + event type + UTC occurrence time + previous request
ID üzerinden deterministik SHA-256 üretilir.

| Kolon | İçerik |
|---|---|
| `EventId` | 64 karakterlik deterministik event kimliği |
| `EventType` | `ReportCreated`, `ReportRevisionCreated`, `ReportClarificationSubmitted`, `CanonicalRequestRecorded`, `ReportClarificationRequested`, `ReportCompleted`, `ReportRejected`, `ReportFailed`, `QueryExecutionSucceeded`, `QueryExecutionFailed` |
| `Outcome` | `Succeeded`, `Rejected` veya `Failed` |
| `OccurredAt` | UTC `datetimeoffset(7)` |
| `RequestId` | Report request kimliği |
| `PreviousRequestId` | Revision/context zinciri, varsa |
| `CorrelationId` | Trace/correlation kimliği |
| `ActorUserId` | `oid`, varsa |
| `TenantId` | `tid`, varsa |
| `ReportStatus` | Event anındaki internal status |
| `ReasonCode` | Safe rejection/failure kodu, varsa |
| `DataSource` | Query audit için `Dwh` veya `Oltp`, varsa |
| `DurationMilliseconds` | Query süresi, varsa |
| `RowCount` | Query row count metadata'sı, varsa |
| `ResultTruncated` | Query truncation metadata'sı, varsa |

Create, revision, clarification, canonical-record, terminal mutation ve query
execution event'leri bu modele yazılır. Audit okuma HTTP endpoint'i ve uygulanmış
retention/archive politikası yoktur.

### Application audit'e bilinçli olarak yazılmayanlar

- Prompt
- Clarification response
- Canonical JSON
- SQL
- Parameter değerleri
- Sonuç satırları
- Region/store listeleri
- Access token
- Connection string
- Power BI URL
- Conversation ID, summary, message body ve exception body/details

Bu liste yalnız **application audit tablosu** içindir. Örneğin prompt,
clarification response, canonical JSON ve Power BI URL iş akışının gereği olarak
`ReportRequests` aggregate'ında persist edilebilir; region/store assignment'ları
ayrı policy tablolarında tutulur. Buna rağmen bu içerikler audit event'ine
kopyalanmaz. SQL planı/parameter değerleri ve query result satırları application
DB, outbox veya Service Bus payload'ına yazılmaz.

Kararın gerekçesi least privilege ve veri minimizasyonudur. Ham prompt/cevap ve
sonuç satırları hassas müşteri/iş verisi taşıyabilir; SQL/canonical/parameter
içeriği şema, allow-list ve yetki sınırlarını ifşa edebilir; token/connection
string doğrudan credential riskidir; Power BI URL'nin audit'e kopyalanması ise
gereksiz yayılım ve ileride query/identity bilgisi eklenmesi riskini artırır.
Safe identifier, karar, durum, süre ve adet metadata'sı operasyonel izlenebilirlik
için yeterli tutulur; log güvenliği de aynı redaction sınırını izler.

> Kurum politikası ham prompt veya SQL saklanmasını zorunlu kılarsa bu veriler
> standart application audit tablosuna değil; şifreli, erişimi kısıtlı,
> maskelenmiş ve retention politikası belirlenmiş ayrı bir diagnostic evidence
> store'a yazılmalıdır.

Hedef dokümandaki “ham prompt ve SQL audit'e yazılmalıdır” yaklaşımı gerçekleşen
davranış değildir ve bu application audit şemasına eklenmemelidir.

## 8. Hedef doküman ile gerçekleşen mimari farkları

| Konu | Hedef doküman | Gerçekleşen sistem | Karar/gerekçe |
|---|---|---|---|
| Synchronous response | Rapor sonucunu ilk HTTP çağrısında üretme beklentisi | İlk cevap `202`; uzun iş outbox/broker/worker'da | HTTP timeout ve uzun transaction'dan kaçınma; retry/durability |
| Endpoint route'ları | Taslak route/alias adları | Yalnız bölüm 3'teki controller route'ları; alias yok | Public contract gerçek controller attribute'larıdır |
| `NeedsClarification` | Public/domain status gibi adlandırma | Domain ve API değeri `WaitingForClarification`; Teams mesajı needs-clarification anlamını verir | Internal persistence contract korunur, UI dili sadeleştirilir |
| Internal processing durumları | Tek `Processing` görünümü | Dokuz internal status; mevcut pipeline `Received/Validating/Processing` kullanır, `Queued/Running` domain'de var ama set edilmez | Domain genişliği ile kullanıcı görünümü ayrılır |
| Audit veri minimizasyonu | Ham prompt ve SQL audit'i | Audit yalnız safe lifecycle/query metadata'sı tutar | Least privilege, hassas veri ve şema/yetki ifşası riski |
| Transactional outbox | Doğrudan enqueue/notification varsayımı | Report mutation + audit + processing/notification outbox aynı transaction'da | DB commit ile mesaj niyeti arasında atomicity |
| Service Bus | In-process/synchronous işleme beklentisi | Production default Azure Service Bus, PeekLock, manual complete, abandon/DLQ | At-least-once durable broker ve transient retry |
| Durable Teams state | Process-local target/dedup | Production authoritative target, delivery ve action state'i SQL'de | Restart ve multi-replica dayanıklılığı; exactly-once değildir |
| DWH/OLTP execution | Backend'in source seçmesi veya fallback | Accepted `SqlExecutionPlan.Source` exact route edilir; DWH default, OLTP disabled; fallback yok | Guardrail kararını koruma ve OLTP riskini sınırlama |
| Power BI URL güvenliği | Genel rapor/embed URL yaklaşımı | Yalnız configured report'un credential-free exact `app.powerbi.com` HTTPS `webUrl`'i; embed token yok | Link data authority değildir; Entra/RLS zorunlu kalır |
| Worker deployment | Ayrı worker servisi varsayımı | Worker/consumer API Container App process'i içindeki hosted service | Repository'de ayrı Worker project/deployment yoktur |
| Fabric result transfer | Query reference'ın Fabric tarafından okunabildiği varsayımı | `qry_*` process-local; durable staging contract yok, activation fail-closed | Result satırlarını REST/outbox/Service Bus'a sızdırmama |

## 9. Doğrulanması gereken backlog maddeleri

| Soru | Sonuç | Repository kanıtı |
|---|---|---|
| API rate limiting uygulanmış mı? | **Not implemented** | API/Teams startup'ında `AddRateLimiter`, `UseRateLimiter` veya endpoint rate-limit policy'si yoktur. External provider'ın HTTP `429` retry davranışı inbound API rate limiting değildir. |
| Power BI link açma veya rapor erişimi application audit'e yazılıyor mu? | **Not implemented** | Audit event enum'unda access/open event'i yoktur; GET/history controller'ları audit writer çağırmaz; Adaptive Card `OpenUrl` client-side açılır ve callback üretmez. `ReportCompleted`, report access audit'i değildir. |
| Gerçek Fabric staging contract'ı var mı? | **Not implemented; activation Blocked** | Blob/OneLake/Delta lifecycle ve job parameter şeması yoktur. Validator her `FabricJob` seçimine missing-contract failure ekler; query result ile Fabric client fail-closed döner. |
| Gerçek Azure/Teams/Power BI smoke testi yapılmış mı? | **Not run / Blocked** | Beş smoke script'i vardır ancak `FINAL_ACCEPTANCE.md` yalnız parse edildiklerini, gerçek host/resource'a karşı çalıştırılmadıklarını söyler. Fabric script'i contract eksikliğinde her zaman `Blocked` döner. |
| UAT tamamlanmış mı? | **Not run / Blocked** | `docs/UAT.md` satırları `Not run`; Fabric senaryosu `Blocked: contract missing`. Product/security/data/operations sign-off kanıtı yoktur. |

Local automated test kanıtı gerçek smoke değildir. Repository'deki son başarılı
TRX çiftleri 568 unit + 125 integration testin (693 toplam) geçtiğini gösterir;
bir önceki başarısız deneme de saklanmıştır. Bu testler gerçek Azure SQL, Service
Bus, Teams tenant/Bot, Power BI veya Fabric kaynak doğrulamasının yerine geçmez.

## 10. As-built durum özeti

| Kategori | Durum | Kanıt ve sınır |
|---|---|---|
| Code Ready | **Partial** | API/domain/persistence/outbox/Service Bus/DWH-OLTP routing/Teams durable state/Power BI provider kodu ve 693 başarılı local test kanıtı var. Fabric staging contract yok; harici `Crm.Analytics.Sql` source workspace'te yok, dolayısıyla clean checkout bağımsız build-ready değildir. |
| Configuration Ready | **Partial / template ready** | Production provider seçimleri, validators, Bicep ve example parametreleri var. Gerçek Entra/Bot/SQL/Service Bus/Power BI değerleri boş; default Fabric seçimi contract nedeniyle bilerek startup'ı engeller. |
| Resource Ready | **No evidence / Not ready** | Bicep yalnız Log Analytics, Container Apps environment ve iki Container App'i tanımlar; deployment run kanıtı yok. SQL, Service Bus, Entra/Bot, Key Vault, ACR, DWH/OLTP, Power BI ve Fabric mevcut dış kaynak olarak beklenir. |
| Smoke Tested | **No — Not run / Blocked** | Script'ler parse edilmiş, gerçek endpoint/resource smoke sonucu yok; Fabric blocked. |
| UAT Passed | **No — Not run / Blocked** | UAT matrisi çalıştırılmamış ve owner sign-off bulunmuyor. |

Sonuç: kod tabanı Development mocks veya açıkça seçilmiş Direct analytics ile
local doğrulama açısından ileridir; gerçek Fabric/Power BI/Teams/Azure üretim
zinciri tamamlanmış veya kabul edilmiş değildir.

## Başlıca repository kanıtları

- API route/auth: `src/CrmAnalytics.Api/Controllers`,
  `src/CrmAnalytics.Api/Program.cs`, `src/CrmAnalytics.Api/Authentication`
- Domain/status: `src/CrmAnalytics.Domain/ReportRequests`
- Submission/processing/outbox: `src/CrmAnalytics.Application/ReportRequests`,
  `src/CrmAnalytics.Application/ReportProcessing`,
  `src/CrmAnalytics.Infrastructure/Outbox`,
  `src/CrmAnalytics.Infrastructure/Messaging`
- Query/analytics/reporting: `src/CrmAnalytics.Infrastructure/QueryExecution`,
  `src/CrmAnalytics.Infrastructure/Integrations`
- Teams: `src/CrmAnalytics.Teams/Hosting`,
  `src/CrmAnalytics.Teams/Messaging`,
  `src/CrmAnalytics.Teams/Notifications`,
  `src/CrmAnalytics.Infrastructure/Teams`
- Audit/schema: `src/CrmAnalytics.Application/Auditing`,
  `src/CrmAnalytics.Infrastructure/Auditing`,
  `src/CrmAnalytics.Infrastructure/Persistence/SqlServer/Migrations`
- Operasyon kanıtı: `README.md`, `docs/UAT.md`, `docs/DEPLOYMENT.md`,
  `docs/OPERATIONS.md`, `docs/FINAL_ACCEPTANCE.md`, `.github/workflows`,
  `scripts/smoke`, `TestResults`
