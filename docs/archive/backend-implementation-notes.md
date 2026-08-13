# CrmAnalytics

## Optional local Ollama canonical planning

The deterministic production parser remains the first planner. When it returns
`CL001` or `CL002`, Development can optionally ask local Ollama/Qwen3 for a strict
`CanonicalRequest` JSON document:

```powershell
$env:Ollama__Enabled = 'true'
$env:Ollama__BaseUrl = 'http://localhost:11434'
$env:Ollama__Model = 'qwen3:4b'
dotnet run --project src/CrmAnalytics.Api
```

Defaults are `Enabled=false`, `BaseUrl=http://localhost:11434`, `Model=qwen3:4b`,
`TimeoutSeconds=45`, `Temperature=0`, and `KeepAlive=10m`. The production default
is disabled. In production the model endpoint must be hosted behind the internal
network boundary; this repository change does not deploy or expose a model.

The model never produces SQL and never selects tables, views, or columns. It only
produces canonical JSON whose metric, dimension, filter and source values come
from the embedded repository catalogs. The existing validator, Query Builder,
allow-list, data-scope injection and guardrails remain the security boundary.
Invalid JSON, schema/catalog validation failure, HTTP failure, timeout, or a
disabled provider preserves the deterministic clarification response.

After installing Ollama manually and pulling `qwen3:4b`, the real-model smoke test
is separate from the unit suite and does not execute SQL:

```powershell
$env:Ollama__Enabled = 'true'
$env:Ollama__BaseUrl = 'http://localhost:11434'
$env:Ollama__Model = 'qwen3:4b'
dotnet run --project tools/CrmAnalytics.OllamaSmoke/CrmAnalytics.OllamaSmoke.csproj -c Release
```

The smoke tool prints only outcome, source, metric/dimension counts, date-range
kind, clarification presence and duration. It does not print prompts, model
responses, canonical filter values, user identifiers, data scope, or SQL.

## Teams app installation package

Pilot Teams uygulama paketi `deploy/teams/crm-analytics-pilot-teams-app.zip`
dosyasindadir. Paket Microsoft'un en guncel genel kullanima acik Teams app
manifest semasi olan `1.28` ile olusturulmustur ve ZIP kokunde yalniz
`manifest.json`, `color.png` ve `outline.png` bulunur.

Manifest app ID'si `2dd37671-1b40-467e-9143-61bf104b67da`, Azure Bot App ID'si
`3e277fe2-0da0-4149-80da-2168ac44e7b9` degerinden bilerek farklidir. Paket
credential, secret, Graph/RSC/device izni veya `webApplicationInfo` icermez.
Developer Portal validation ve Teams'e manuel yukleme adimlari icin
[`docs/deployment/TEAMS_APP_INSTALLATION.md`](../deployment/TEAMS_APP_INSTALLATION.md)
belgesini kullanin.

Repository'de gerçekleşen backend, worker, DWH/OLTP, Microsoft Teams, audit ve
production-readiness sınırlarının tek as-built görünümü için
[`AS_BUILT_ARCHITECTURE_2026-08-02.md`](architecture/AS_BUILT_ARCHITECTURE_2026-08-02.md) belgesine bakın.

## Backend authentication modes

`CrmAnalytics.Api` iki authentication modu destekler:

- `Development`, yalnızca `ASPNETCORE_ENVIRONMENT=Development` altında sabit
  ve sahte bir local principal üretir. `appsettings.Development.json` içindeki
  GUID'ler yalnızca yerel geliştirme içindir; gerçek tenant veya kullanıcı
  kimlikleri değildir. Bu mod gerçek güvenlik sağlamaz.
- `Entra`, Microsoft Entra ID tarafından API için verilmiş bearer access
  token'ını `Microsoft.Identity.Web` ile doğrular. Gerekli delegated scope
  `access_as_user` değeridir. API access token yerine ID token kabul edilmez.

Kullanıcı kimliği `oid`, tenant kimliği `tid`, delegated izinler `scp` ve iş
uygulaması rolleri `roles` claim'inden alınır. Display name, e-posta, UPN,
`sub`, directory role (`wids`) veya request body authorization için
kullanılmaz. `Mode=Development` Production veya Staging ortamında options
validation nedeniyle uygulamanın başlamasını engeller.

Entra yapılandırması için daha sonra gerçek ortamdan şu değerler sağlanmalıdır:

- Tenant ID
- API Application Client ID
- API Application ID URI/Audience
- Delegated scope: `access_as_user`

Teams adapter'ı Faz 6.2 itibarıyla Microsoft Teams SDK token service üzerinden
kullanıcının delegated `access_as_user` access token'ını her kullanıcı
işleminde alır ve yalnızca ilgili CrmAnalytics.Api isteğine Bearer olarak
ekler. Token process-local bir store'a dahi yazılmaz. Tek App Registration
MVP yaklaşımında Teams SSO, Azure Bot kimliği ve API audience aynı Application
Client ID değerini kullanır; bu nedenle OBO yapılmaz. Faz 6.3'te app role ve
bölge/mağaza veri kapsamı politikaları backend'e bağlanmıştır. App role tek
başına veri erişimi vermez; processing başlatan her işlem için aynı `tid` ve
`oid` çiftine ait bir backend data-scope assignment gerekir. Boş region/store
listeleri hiçbir zaman tüm erişim anlamına gelmez.

Report ve conversation kayıtları seçilen persistence provider'a göre
in-memory veya SQL Server/Azure SQL üzerinde tutulur. Notification target ve
idempotency kayıtları in-memory, queue/outbox ise volatile durumdadır.
Development principal, gerçek Entra güvenliği veya kalıcı storage yerine
kullanılamaz.

Teams host authentication mode'ları, gerçek SSO kullanıcı akışı ve secretsiz
Entra/Azure Bot kurulum runbook'u için
[`src/CrmAnalytics.Teams/README.md`](../../src/CrmAnalytics.Teams/README.md)
dosyasına bakın.

## Entra App Roles ve veri kapsamı

API, token'ın `roles` claim'indeki app role değerlerini
`ReportAuthorization:RolePermissions` configuration'ı üzerinden uygulama içi
izinlere dönüştürür. Eşleşme exact ve case-sensitive'dir. Varsayılan model:

- `Report.User`: `Create`, `ReadOwn`, `ReviseOwn`, `ClarifyOwn`,
  `ViewOwnHistory`
- `Report.Viewer`: `ReadOwn`, `ViewOwnHistory`
- `Report.Admin`: `Create`, `ReadOwn`, `ReviseOwn`, `ClarifyOwn`,
  `ViewOwnHistory`

`Report.Admin` başka bir kullanıcının veya tenant'ın raporlarını göremez.
Ownership kontrolü bütün roller için aynıdır. Geniş region/store erişimi yalnız
data-scope assignment ile verilir.

App Registration üzerinde aşağıdaki app role'ları manuel oluşturun:

| Display name | Value | Allowed member types | Description |
|---|---|---|---|
| Report User | `Report.User` | Users/Groups | Kendi rapor taleplerini oluşturabilir, okuyabilir, revize edebilir ve açıklama gönderebilir. |
| Report Viewer | `Report.Viewer` | Users/Groups | Kendi mevcut raporlarını ve konuşma geçmişini okuyabilir. |
| Report Admin | `Report.Admin` | Users/Groups | Mevcut rapor operasyonlarını kullanabilir; veri kapsamı ayrıca backend policy store tarafından belirlenir. |

Role `Value` değerleri configuration ile karakter karakter eşleşmelidir.
Kullanıcıyı veya security group'u Enterprise Applications → Users and groups
üzerinden role atayın. Security group'un app role'a atanması desteklenir,
ancak uygulama `groups`, `hasgroups`, `_claim_names` veya `_claim_sources`
claim'lerini doğrudan yorumlamaz ve Graph fallback yapmaz. Entra directory
role'ları uygulama rolü değildir; `wids` kullanılmaz.

Atamadan sonra yeni access token alınmalıdır. Cached token yeni `roles`
claim'ini hemen taşımayabileceğinden yeniden sign-in gerekebilir. Role ataması
Entra yöneticisinin sorumluluğundadır. App role tek başına data erişimi vermez;
aynı `oid`/`tid` için aşağıdaki assignment da bulunmalıdır.

### Data-scope configuration

Secretsiz örnek:

```json
{
  "ReportDataAccess": {
    "Assignments": [
      {
        "TenantId": "00000000-0000-0000-0000-000000000001",
        "UserId": "00000000-0000-0000-0000-000000000002",
        "AllowAllRegions": false,
        "AllowAllStores": false,
        "AllowedRegions": [
          "SP",
          "RJ"
        ],
        "AllowedStoreIds": [
          "STORE-001",
          "STORE-002"
        ]
      }
    ]
  }
}
```

`TenantId` token'daki `tid`, `UserId` token'daki `oid` ile eşleşir; e-posta
ve UPN kullanılmaz. Lookup yalnız tam tenant+user çiftiyle yapılır; global
assignment veya cross-tenant fallback yoktur. `AllowAllRegions` ve
`AllowAllStores` açık boolean'lardır. Aynı dimension için allow-all ile açık
liste birlikte kullanılamaz. Boş liste tüm erişim değildir ve `*` wildcard
desteklenmez. Region/store kodları production'da kurumsal canonical katalogla
doğrulanmalıdır.

Development fake kullanıcı ve ona ait allow-all assignment
`appsettings.Development.json` içindeki aynı sahte GUID'leri kullanır.
`appsettings.json` production assignment içermez. Production değerleri
environment/deployment/external configuration üzerinden sağlanabilir.

### İşlem ve production sınırları

Create, revise ve clarification güncel assignment gerektirir; assignment
bulunmazsa mutation ve queue çağrısından önce güvenli `403 ACCESS_DENIED`
döner. GET ve history yalnız permission+ownership uygular; assignment'ı daha
sonra kaldırılmış bir kullanıcı kendi eski sonucunu okuyabilir.

Query Planning, submission anında çözülen immutable scope snapshot'ını
(`oid`, `tid`, doğrulanmış roles, allow-all flag'leri ve explicit listeler)
alır. Mock canonical JSON prompt, role veya region/store değerlerini tekrarlamaz;
yalnız allow-all flag ve adet metadata'sı taşır. Analytics ve report generation
isteklerine scope kopyalanmaz.

Configuration assignment store read-only bir MVP implementasyonudur ve çok
sayıda kullanıcı için appsettings/environment configuration ölçeklenmez.
Production'da `IUserDataAccessAssignmentStore` Azure SQL veya merkezi policy
store ile değiştirilmelidir. Queue item submission anındaki snapshot'ı taşır;
queue'da beklerken erişim kaldırılırsa eski snapshot kullanılabilir.
Production worker işlemden önce policy'yi yeniden çözmeli veya policy version
doğrulamalıdır. Kalıcı Service Bus mesajında access token değil, yalnız güvenli
identity/policy reference taşınmalıdır.

Report request ve conversation kayıtları için Faz 7.1'de SQL Server/Azure SQL
provider'ı eklenmiştir. Notification target ve action dedup kayıtları hâlâ
in-memory; queue/outbox hâlâ volatile'dır. Merkezi policy store, audit,
transaction orchestration ve güvenilir queue/outbox sonraki Faz 7 adımlarında
ele alınmalıdır.

## Crm.Analytics.Sql entegrasyon sözleşmesi

SQL production servisi kullanıcı veri kapsamı üretmez. Backend önce Entra
`oid`/`tid`, app role ve merkezi policy store üzerinden kapsamı çözer; kapsam
değerleri prompt'a eklenmez. SQL guardrail katmanının bu kapsamı parse edilmiş
sorgu ağacındaki bütün ilgili bloklara uygulaması beklenir.

Mevcut hazırlık katmanında yalnız şu scope biçimleri güvenli kabul edilir:

| Backend scope | SQL production uyumluluğu |
|---|---|
| Bütün region'lar + bütün store'lar, explicit liste yok | Unrestricted |
| Explicit region kodları + bütün store'lar, store listesi yok | Region kısıtlı |
| Store-only, region+store birlikte kısıtlı veya herhangi bir store kısıtı | Unresolved / fail-closed |
| Boş ya da tutarsız allow-all/liste birleşimi | Unresolved / fail-closed |

Region değerleri Olist `customer_state` canonical kodlarıyla (örneğin `SP`,
`RJ`) uyumlu olmalıdır. Adapter `Marmara → SP`, `Ege → RJ` veya
store→region gibi anlam dönüşümü yapmaz. SQL servisinin store-level kapsam
sözleşmesi olmadığı sürece store kısıtı kaybedilmez ve kullanıcı
Unrestricted'a genişletilmez.

`CanonicalRequestJson` yalnız Crm.Analytics.Sql
`CanonicalRequestSerializer` çıktısı için ayrılmış domain alanıdır. Backend
metni trim etmez, normalize etmez, prompt'a katmaz ve GET/history/notification
payload'larına koymaz. Gerçek serializer projesi bu repository'de bulunmadığı
için serialize/deserialize adapter'ı henüz bağlı değildir. Follow-up revision
aynı user+tenant'a ait önceki kaydın canonical değerini kullanır. Clarification
cevabı ise original prompt ile birleştirilmeden delta prompt olarak, aynı
request'in current canonical değeriyle gönderilir; canonical yoksa işlem
fail-closed olur.

Her report kendi `CreatedAt` UTC tarihinden türetilen `ReferenceDate` değerini
saklar. Worker'ın çalıştığı gün kullanılmaz; queue gecikmesi göreli tarih
ifadelerinin anlamını değiştirmez. Revision yeni request olduğu için yeni
reference date alır, clarification aynı request üzerinde bu değeri korur.

`Rejected`, guardrail veya iş/policy kararından doğan kontrollü terminal
rettir; `Failed` timeout, geçersiz entegrasyon cevabı, execution provider
eksikliği ve diğer teknik hatalardır. Rejected kullanıcıya yalnız güvenli
mesajı gösterir; reason code gösterilmez. Accepted SQL/parameter/execution plan
public contract değildir, loglanmaz ve kalıcı store'a yazılmaz.

Bu repository'de `Crm.Analytics.Sql.csproj`, `ENTEGRASYON.md` ve `KAPSAM.md`
bulunmadığından gerçek `SqlProductionFactory`, serializer, parameter,
ResultShape ve audit-writer adapter'ı ile Infrastructure `ProjectReference`
eklenememiştir. Gerçek query executor da yapılandırılmamıştır; fail-closed
executor çağrılırsa işlem Completed olmaz. Development ve standart test hostu
mevcut deterministic Mock query-planning hattını kullanmayı sürdürür. Gerçek
adapter için Crm.Analytics.Sql proje yolu ya da yayımlanmış paket referansı
gereklidir.

Data Engineer tarafında kalan bağımlılıklar `vw_sales`, `vw_customer_rfm`,
`vw_payment`, read-only bağlantı ve RLS'dir. Bu faz view veya RLS oluşturmaz ve
gerçek DWH sorgusu çalıştırmaz. Gerçek executor ayrı bir entegrasyon adımında
eklenecektir. Olist verisi 2018'de bittiği için “bu yıl” gibi ifadeler boş sonuç
üretebilir; demo taleplerinde mutlak 2018 tarihleri kullanılmalıdır.

Faz 7.1 migration'ı şu alanları map etmelidir:

| Alan | Önerilen SQL tipi |
|---|---|
| `ReferenceDate` | `date`, required |
| `CanonicalRequestJson` | `nvarchar(max)`, nullable |
| `RejectionCode` | `nvarchar(128)`, nullable |
| `RejectionMessage` | `nvarchar(1000)`, nullable |
| `ReportRequestStatus.Rejected` | Mevcut status string mapping'inde `Rejected` |

## SQL Server / Azure SQL persistence

`Persistence:Provider` değeri `InMemory` veya `SqlServer` olabilir. Varsayılan
production ayarı `SqlServer`, Development ayarı `InMemory`'dir. In-memory
provider yalnız Development ve test ortamlarında kullanılabilir; Production
ve Staging başlangıç validation'ında reddedilir. Development'ın varsayılan
çalışması için SQL Server kurulumu gerekmez, ancak uygulama kapanınca report
ve conversation kayıtları kaybolur.

Local SQL Server provider'ını PowerShell ile seçmek için gerçek bağlantı
değerini yalnız environment variable üzerinden sağlayın:

```powershell
$env:Persistence__Provider = "SqlServer"
$env:ConnectionStrings__CrmAnalytics = "<LOCAL_SQL_SERVER_CONNECTION_STRING>"

dotnet run `
  --project src/CrmAnalytics.Api `
  --launch-profile https
```

SQL provider'da command timeout 1–300 saniye, retry count 0–10 ve retry delay
1–120 saniye aralığında doğrulanır. Retry kapatılabilir. Bağlantı dizesi boşsa
host başlamaz. EF sensitive data logging açılmaz; bağlantı dizesi, SQL,
parametre değerleri ve rowversion loglanmaz.

Local migration araçlarını ve migration listesini doğrulamak için:

```powershell
dotnet tool restore

dotnet ef migrations list `
  --project src/CrmAnalytics.Infrastructure `
  --startup-project src/CrmAnalytics.Api `
  --context CrmAnalyticsDbContext
```

Yalnız development veritabanını güncellemek için:

```powershell
dotnet ef database update `
  --project src/CrmAnalytics.Infrastructure `
  --startup-project src/CrmAnalytics.Api `
  --context CrmAnalyticsDbContext
```

Uygulama startup sırasında `Database.Migrate`, `EnsureCreated`,
`EnsureDeleted` veya otomatik schema değişikliği çalıştırmaz. Pilot/production
için idempotent script üretilmeli, script incelenmeli ve DBA ya da deployment
pipeline tarafından uygulanmalıdır:

```powershell
dotnet ef migrations script `
  --idempotent `
  --project src/CrmAnalytics.Infrastructure `
  --startup-project src/CrmAnalytics.Api `
  --context CrmAnalyticsDbContext `
  --output deploy/sql/CrmAnalytics.Initial.sql
```

Production bağlantı dizesi Key Vault/deployment secret üzerinden
sağlanmalıdır. Azure SQL aynı EF Core SQL Server provider'ını ve retry
configuration'ını kullanabilir. Managed Identity henüz eklenmemiştir; parola
yerine Managed Identity sonraki deployment aşamasında değerlendirilmelidir.

Faz 7.0 alanlarından `ReferenceDate` audit/replay için kayıt zamanından
türetilir. `CanonicalRequestJson` olduğu gibi saklanır ve kullanıcıya
gösterilmez. `RejectionCode` internal kalır; `RejectionMessage` güvenli
kullanıcı mesajıdır. `Rejected` kontrollü iş/policy reddini, `Failed` teknik
hatayı ifade eder.

## Application transaction sınırları

SQL provider'da create/revision/clarification ile terminal durum,
canonical-request ve clarification-request mutation'ları ilgili conversation
değişikliği ve application audit kaydıyla aynı kısa transaction'a katılır.
Runner, aktif transaction yoksa yapılandırılmış EF execution strategy üzerinden
transaction açar; nested çağrı aktif transaction'a katılır ve iç çağrı
commit/rollback yapmaz. Report üretimi, SQL production, analytics, HTTP, Teams
ve diğer uzun dış servis çağrıları transaction içinde çalışmaz.

Faz 7.3A ile processing isteği aynı transaction'da outbox'a append edilir;
submission servisi commit sonrasında Channel veya Service Bus'a doğrudan enqueue
yapmaz. Dispatcher publish sırasında database transaction tutmaz.

InMemory transaction runner yalnızca development/test kolaylığıdır: operation'ı
doğrudan çalıştırır ve çok adımlı mutation rollback garantisi vermez.

## SQL data-access assignment store

Production varsayılanı `ReportDataAccess:Provider=SqlServer` değeridir ve
`Persistence:Provider=SqlServer` gerektirir. Store yalnızca normalize edilmiş
exact `TenantId + UserId` ve aktif assignment arar; cross-tenant/global fallback
ve role tabanlı implicit unrestricted erişim yoktur. Eksik assignment mevcut
`403 ACCESS_DENIED`, bozuk assignment ise ayrıntı sızdırmayan
`500 DATA_ACCESS_POLICY_ERROR` üretir.

`Configuration` provider yalnızca Development/test ortamlarında kabul edilir.
Assignment yönetim API'si yoktur; kayıtlar incelenmiş deployment işlemi veya DBA
tarafından sağlanır. Üç güvenli örnek
`deploy/sql/CrmAnalytics.DataAccess.Example.sql` dosyasındadır.

## Application audit

`crm.ApplicationAuditEvents` append-only yaşam döngüsü event tablosudur. Audit
mutation ile aynı SQL transaction'a katılır; deterministik SHA-256 `EventId`
retry/idempotency çakışmalarını azaltır. Prompt, clarification cevabı, canonical
JSON, SQL/parametre, summary, Power BI URL, conversation ID, scope listeleri,
mesaj içerikleri, token ve exception ayrıntıları saklanmaz. Audit okuma HTTP
API'si yoktur. Bu tablo SQL Server'ın server-level Audit özelliğinin yerine
geçmez; retention/archive politikası henüz uygulanmamıştır.

## Faz 7.3A bilinen sınırlar

- InMemory runner rollback garantisi vermez ve restart'ta audit kaybolur.
- Azure Service Bus at-least-once teslimat sağlar; external processing
  exactly-once değildir.
- Teams notification target, notification delivery ve card-action dedup
  store'ları hâlâ in-memory'dir.
- Worker güncel data-scope assignment'ı yeniden doğrular; ara processing crash
  recovery için tam lease/state-machine mekanizması henüz yoktur.
- Audit retention/archive ve data-assignment provisioning servisi yoktur.
- Faz 7.3B kalan Teams store persistence ve durable notification işlerini kapsar.

SQL Server rowversion için manuel smoke testte aynı kaydı iki DbContext ile
okuyun, ilk context ile güncelleyin ve ikinci context'in stale update'inde
`CONCURRENCY_CONFLICT` bekleyin. SQLite relational testleri mapping ve sınırlı
concurrency davranışını doğrular; SQL Server rowversion veya SQL Server
collation garantisinin yerini tutmaz.

## DWH/OLTP Query Execution Bağımlılıkları

Faz 7.4 yalnız guardrail/SQL production katmanının `Accepted` kararıyla ürettiği
`SqlExecutionPlan` nesnesini çalıştırır. API veya Teams üzerinden SQL, kaynak,
timeout ya da parametre kabul edilmez. Service Bus envelope'u bu bilgileri
taşımaz; worker report'u `requestId` ile yükler, güncel data scope'u yeniden
çözer ve SQL production sonucundaki açık `Dwh`/`Oltp` kaynağını aynen kullanır.
Kaynaklar arasında otomatik fallback yoktur.

Production varsayılanı `QueryExecution:Provider=SqlClient`, Development
varsayılanı `Mock` değeridir. DWH ve OLTP'nin connection, authentication,
command-timeout, retry, maksimum satır/kolon/hücre ve toplam byte politikaları
ayrıdır. Production ve Staging'de `Mock` ile `ConnectionString` authentication
reddedilir; DWH enabled ve güvenli named query connection zorunludur.
`ManagedIdentity` connection'da `Authentication=Active Directory Managed
Identity`, `DefaultAzureCredential` ise `Authentication=Active Directory
Default` gerektirir. User-assigned identity için kaynak bazında
`ManagedIdentityClientId` verilebilir. Runtime kullanıcı/Teams/HTTP token'ını
SQL'e forward etmez ve OBO SQL token'ı almaz.

Data Engineering ekibi DWH/Fabric için TDS/SQL endpoint'ini, warehouse/database
adını, read-only managed identity principal'ını, allow-listed `vw_sales`,
`vw_customer_rfm`, `vw_payment` view'larını, gerekli scope kolonlarını,
canonical region/store kodlarını, RLS kararını ve veri güncellik SLA'sını
sağlamalıdır. OLTP yalnız iş birimi ve Data Engineering tarafından onaylanan
güncel kullanım senaryolarında; ayrı read-only principal, dar view allow-list,
sıkı timeout, düşük satır limiti ve operasyonel performans onayıyla açılmalıdır.

Runtime principal yalnız onaylı view'larda `SELECT` almalıdır. `INSERT`,
`UPDATE`, `DELETE`, `ALTER` ve stored procedure kullanılmadığı için `EXECUTE`
verilmemelidir. RLS ve database izinleri backend tarafından oluşturulmaz.
`ApplicationIntent=ReadOnly` ek savunmadır; `GRANT SELECT` ve read-only
principal yetkilendirmesinin yerine geçmez.

Local read-only SQL doğrulaması yalnız açık environment override ile yapılır;
gerçek credential source control'a yazılmaz:

```text
QueryExecution__Provider=SqlClient
QueryExecution__Dwh__Enabled=true
QueryExecution__Dwh__AuthenticationMode=ConnectionString
ConnectionStrings__QueryDwh=<local-read-only-connection>
```

Readiness'te yalnız enabled kaynak için `query-dwh`/`query-oltp` connection'ı
açılır; kullanıcı SQL'i, `SELECT 1`, view veya tablo sorgusu çalıştırılmaz.
Connection, server ve database ayrıntıları health response'una yazılmaz.

Gerçek `Crm.Analytics.Sql` adapter'ı henüz yoktur; planlar bugün test/fake SQL
production provider'larıyla üretilebilir. DWH/OLTP view'ları, read-only izinler
ve RLS Data Engineering bağımlılığıdır. Query result yalnız processing süresince
bellekte satır/kolon/hücre/byte limitleriyle tutulur; kalıcı staging, global
dictionary veya Service Bus/outbox payload'ı yoktur. Büyük sonuçlar için sonraki
fazda Fabric/Blob/Parquet staging gerekebilir.

Query execution exactly-once değildir. Worker/process crash sonrasında read-only
`SELECT` tekrar çalışabilir; veri mutasyonu yaratmaz fakat kaynak yükünü tekrar
oluşturabilir. Ara processing state recovery bütünüyle çözülmemiştir. Gerçek SQL
adapter'ından sonra ResultShape/Fabric aktarımı ayrıca doğrulanmalıdır. Teams
target/delivery/action dedup persistence işi (Faz 7.3B) hâlâ beklemektedir.

## Azure Service Bus durable processing queue

Faz 7.3A, yukarıdaki Faz 7.2 doğrudan Channel enqueue açıklamasının yerini
alır. Create, revision ve clarification mutation'ları artık güvenli
`ReportProcessingRequested` envelope'unu report/conversation/audit ile aynı SQL
transaction'ında `crm.OutboxMessages` tablosuna ekler. Commit sonrasında ayrı
dispatcher mesajı yayınlar; SQL transaction açıkken Service Bus çağrısı
yapılmaz. Development varsayılanı `Messaging:Provider=InMemory` olup aynı
outbox-dispatcher yolundan mevcut Channel consumer'a gider. In-memory outbox
process restart'ında kaybolur.

Mesaj yalnız `requestId`, operasyonel `correlationId`, UTC `requestedAt` ve
`schemaVersion=1` taşır. Prompt, clarification cevabı, canonical JSON, SQL,
parametreler, sonuç, kullanıcı/tenant kimliği, roller, region/store scope,
conversation ID ve credential mesaj gövdesine veya application property'lerine
konmaz. Worker report'u `requestId` ile SQL'den yükler ve report'taki user/tenant
üzerinden güncel data-scope assignment'ı tekrar çözer. Submission sırasında app
role authorization yapılır; worker token taşımadığı için app role yeniden
doğrulanmaz ve sahte rol üretilmez. Assignment kaldırılmışsa işlem fail-closed
`DATA_SCOPE_UNAVAILABLE`, bozuksa `DATA_ACCESS_POLICY_ERROR` sonucuna gider.

Queue kod tarafından oluşturulmaz. Portal, CLI veya deployment pipeline ile
önceden oluşturulmalıdır. Önerilen ad `crm-report-processing`; Peek-Lock,
duplicate detection, expiration dead-lettering açık; sessions kapalı olmalıdır.
Duplicate detection penceresi, max delivery count ve partitioning kapasite ve
operasyon ihtiyacına göre belirlenir. Runtime bu ayarların tek başına güvenlik
garantisi olduğunu varsaymaz.

Managed identity için en az ayrı `Azure Service Bus Data Sender` ve
`Azure Service Bus Data Receiver` rolleri, mümkünse queue seviyesinde atanır.
Production runtime'a `Data Owner` verilmez. Örnek secretsiz ayarlar:

```text
Messaging__Provider=AzureServiceBus
Messaging__AzureServiceBus__AuthenticationMode=ManagedIdentity
Messaging__AzureServiceBus__FullyQualifiedNamespace=<namespace>.servicebus.windows.net
Messaging__AzureServiceBus__QueueName=crm-report-processing
```

Production ve Staging'de InMemory provider, kapalı dispatcher ve connection
string authentication startup validation ile reddedilir. Local gerçek broker
testinde `DefaultAzureCredential` ile `az login`/IDE identity ve RBAC
kullanılabilir. `ConnectionString` yalnız Development için desteklenir ve hiçbir
gerçek connection string source control'a yazılmaz.

Publisher aynı deterministik 64 karakter SHA-256 kimliği hem outbox primary key
hem Service Bus `MessageId` olarak kullanır. Broker mesajı subject
`report-processing-requested.v1`, content type `application/json`, application
properties `schemaVersion=1` ve `messageType=ReportProcessingRequested` ile
gönderilir. Consumer `ServiceBusProcessor` ile Peek-Lock ve manual settlement
kullanır: başarı/terminal duplicate complete, transient processing failure
abandon, malformed veya kalıcı mesaj güvenli kısa reason ile built-in DLQ'ya
dead-letter edilir.

Outbox `DeadLettered`, broker'a hiç yayınlanamayan veya publish öncesi geçersiz
mesajdır. Service Bus DLQ ise broker'a ulaşmış ama tüketilememiş mesajdır;
uygulama ayrı DLQ queue oluşturmaz. DLQ kendiliğinden temizlenmez. Operasyon ekibi
mesajı inceleyip kök nedeni gidermeli ve kontrollü manuel replay yapmalıdır; bu
fazda replay/admin API yoktur. Published outbox satırları varsayılan yedi günlük
retention sonrasında batch cleanup ile silinir; Pending, Processing ve
DeadLettered satırlar otomatik silinmez.

Teslimat at-least-once'dur; external processing exactly-once değildir.
`MessageId` duplicate detection yardımcı savunmadır, application idempotency'nin
yerine geçmez. SQL/analytics/report servisleri yeniden çağrılabilir. Terminal
report duplicate delivery'de yeniden işlenmez; ancak nonterminal ara durumda
process crash recovery tam otomatik değildir ve ileride processing lease/state
machine iyileştirmesi gerekebilir. Gerçek broker smoke testi kullanılabilir bir
Azure kaynağı olmadan çalıştırılamaz.

Faz 7.3B'deki process-local Teams state sınırlaması Faz 7.6 ile aşağıda
belgelenen SQL store ve durable notification outbox yapısına taşınmıştır.
# Faz 7.5 + 7.6: gerçek SQL üretimi ve durable Teams state

Infrastructure, `Crm.Analytics.Sql` kaynak projesine tek yönlü bir
`ProjectReference` taşır; Application, Domain, Contracts, API ve Teams bu
kütüphaneyi doğrudan referanslamaz. Varsayılan kaynak yerleşimi repository ile
aynı üst klasördeki `crm-project/Crm.Analytics.Sql/Crm.Analytics.Sql.csproj`
projesidir. Production/Staging için `SqlProduction:Provider=CrmAnalyticsSql`
zorunludur; `Mock` yalnız Development/test ortamlarında kabul edilir. Desteklenen
SQL sürümü `Sql150`, kaynaklar `Dwh` ve `Oltp`'dir.

`CrmAnalyticsSqlProductionClient`, önceki canonical talebi yalnız kütüphanenin
`CanonicalRequestSerializer` sözleşmesiyle açıp kaydeder. Region/store kapsamı
fail-closed eşlenir: tam yetki `Unrestricted`, yalnız kanonik iki harfli region
listesi `ForRegions`, store kısıtı veya tutarsız/boş kapsam `Unresolved` olur.
Kapsam prompt'a eklenmez. Accepted yanıtlar açık parameter/source/result-shape
mapping'inden sonra mevcut DWH/OLTP query executor'a gider; clarification ve
rejection sorgu çalıştırmaz. Teknik adapter/serialization hataları `Failed`
olur. Prompt, canonical JSON, SQL, ham parametre, scope listesi ve kimlikler
adapter log/audit'ine yazılmaz.

Teams hedefi artık backend'deki API-key korumalı
`PUT /api/internal/teams-targets/{requestId}` endpoint'iyle kaydedilir. Backend
SQL şemasında `crm.TeamsNotificationTargets`,
`crm.TeamsNotificationDeliveries` ve `crm.TeamsCardActionSubmissions` tabloları
bulunur. Aynı request için farklı conversation sessizce değiştirilemez; delivery
claim/lock ve action claim/complete/release kayıtları process restart sonrasında
da korunur. Action tablosunda revision veya clarification input metni tutulmaz.

Terminal durum mutation'ları (`WaitingForClarification`, `Completed`,
`Rejected`, `Failed`) aynı database transaction'ında minimal
`ReportNotificationRequested` outbox envelope'u yazar. Envelope yalnız
`RequestId`, `Status`, `ReportUpdatedAt` ve `SchemaVersion=1` taşır. Dispatcher,
processing mesajlarını Service Bus'a; notification mesajlarını Teams HTTP
callback'ine yollar. Callback'in 204 sonucu delivery'yi `Delivered` ve outbox'ı
`Published` yapar. Hedef hazır değilse, 5xx veya taşıma hatasında retry; 400/401
kalıcı hata uygulanır. API key geçici S2S çözümüdür; production hedefi Entra
workload identity/managed identity olmalıdır. Repository'ye gerçek secret
eklenmez.

Notification semantiği at-least-once'dur. Kart gönderildikten sonra Delivered
yazılmadan process çökerse duplicate kart oluşabilir. Benzer şekilde action
mutation'ı tamamlanıp completion kaydı yazılmadan çökme ikinci denemeye yol
açabilir; kalıcı claim ve backend domain/status kuralları riski azaltır fakat
distributed exactly-once garantisi vermez. Release öncesinde gerçek Teams
desktop/web/mobile smoke testi, gerçek SQL Server view/RLS/read-only bağlantısı
ve DWH/OLTP erişimi ayrıca doğrulanmalıdır.
## Faz 8 + 9: sonuç entegrasyonu ve teslim sınırları

Analytics `Mock`, `Direct`, veya `FabricJob`; reporting `Mock` veya `PowerBi` seçer. Production/Staging ortamları mock provider'ları reddeder. Direct yalnız açık demo kararıyla korunmuş ortamlarda açılır ve yalnız bounded sonuç metadata'sı üretir. Depoda onaylı Fabric staging/parametre sözleşmesi bulunmadığından `FabricJob` aktivasyonu fail-closed'dur; `QueryExecutionResult` satırları REST, outbox veya Service Bus'a taşınmaz.

## Application audit metadata migration

`20260805100943_AddApplicationAuditMetadata`, mevcut
`crm.ApplicationAuditEvents` tablosuna nullable `AuditMetadataJson`
(`nvarchar(max)`) ve SQL Server `ISJSON` check constraint'i ekler. Eski audit
kayıtları NULL metadata ile geçerlidir; migration backfill veya destructive DML
çalıştırmaz. İdempotent production artifact'ı
`deploy/sql/CrmAnalytics.Migrations.sql` dosyasıdır.

Metadata schema sürümü `application-audit-metadata-v1`'dir. Source mevcut
`DataSource` kolonunda kalır; JSON conversation ID, source-contract version,
guardrail doğrulanmış execution-plan object'i, SHA-256 parameterized query-shape
fingerprint'i, timeout/row limit ve attempt/optional delivery metadata taşır. Raw
SQL, prompt/result payload, parameter value, token, secret, API key veya connection
string saklanmaz. Production migration ayrı Entra operatör/DBA oturumuyla uygulanır;
runtime UAMI yalnız application DML yetkileriyle kalır ve migration/DDL yetkisi
almaz.

Power BI adapter'ı önceden tanımlı workspace/report GUID'lerini kullanır, yalnız Get Report `webUrl` alanını kabul eder ve URL'yi mutlak HTTPS `app.powerbi.com` adresi olarak doğrular. `embedUrl`, embed token, iframe, DAX ve dinamik URL filtresi yoktur. Refresh varsayılan olarak kapalıdır; açılırsa accepted yanıt ve isteğe bağlı bounded polling uygulanır.

Power BI bağlantısına sahip olmak veri yetkisi sağlamaz. Kullanıcı Power BI tarafında da Entra ve RLS yetkisine sahip olmalıdır. Backend ownership/data scope Power BI RLS'nin yerine geçmez; RLS ek savunma katmanıdır. URL içine user/tenant, region/store, token veya credential konulmaz.

Deployment, operasyon, UAT ve gerçek dış bağımlılık durumu için `docs/DEPLOYMENT.md`, `docs/OPERATIONS.md`, `docs/UAT.md` ve `docs/FINAL_ACCEPTANCE.md` belgelerine bakın.
