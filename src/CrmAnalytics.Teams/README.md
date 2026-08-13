# CrmAnalytics Teams adapter

Bu host, Microsoft Teams SDK üzerinden aldığı mesajı mevcut CrmAnalytics
API'ye yeni bir rapor talebi olarak iletir. Backend işlemi terminal duruma
ulaştığında Teams hostundaki korumalı internal callback üzerinden aynı
konuşmaya proactive sonuç mesajı gönderilir.

## Yerel çalıştırma

Önce güçlü bir development anahtarı üretin:

```powershell
$notificationKey = [Convert]::ToBase64String(
    [Security.Cryptography.RandomNumberGenerator]::GetBytes(32)
)
$notificationKey
```

Üretilen değeri iki terminalde de aynı `$notificationKey` değeri olarak
kullanın. Teams terminali:

```powershell
$env:ReportNotifications__Enabled = "true"
$env:ReportNotifications__ApiKey = $notificationKey

dotnet run --project src/CrmAnalytics.Teams --launch-profile http
```

Backend terminali:

```powershell
$env:TeamsNotifications__Enabled = "true"
$env:TeamsNotifications__ApiKey = $notificationKey

dotnet run --project src/CrmAnalytics.Api --launch-profile https
```

Anahtar source code'a veya `appsettings*.json` dosyalarına yazılmamalıdır.
İki terminalde aynı anahtar kullanılmalıdır. PowerShell environment
variable'ları terminal kapandığında kaybolabilir.

Microsoft 365 Agents Playground'u kurun:

```powershell
npm install -g @microsoft/m365agentsplayground
```

Playground'u adapter endpoint'ine bağlayın:

```powershell
agentsplayground -e http://localhost:3978/api/messages -c emulator
```

Örnek mesaj:

> Marmara ve Ege bölgelerinin son çeyrek satışlarını karşılaştır.

İlk cevap:

```text
Rapor talebiniz alındı.
İşlem numarası: ...
```

İşlem tamamlandığında sonuç aynı Teams konuşmasına proactive mesaj olarak
gönderilir. Sonuç ayrıca backend'den her zaman kontrol edilebilir:

```http
GET https://localhost:7090/api/report-requests/{requestId}
```

## Adaptive Card sonuç bildirimi

Terminal rapor sonuçları aynı konuşmaya sade bir Adaptive Card olarak
gönderilir:

- `Completed` kartı rapor özetini, işlem numarasını, revizyon input'unu ve
  **Revize Et** eylemini gösterir. Backend sonucu geçerli, mutlak bir HTTPS
  Power BI URL içeriyorsa **Power BI’da Aç** düğmesi de korunur.
- `WaitingForClarification` kartı açıklama sorusunu, cevap input'unu ve
  **Açıklamayı Gönder** eylemini güvenli biçimde gösterir.
- `Failed` kartı teknik hata kodu veya iç servis detayı yerine güvenli genel bir
  hata mesajı gösterir.

## Etkileşimli kart akışları

`Completed` kartında kullanıcı çok satırlı revizyon metnini girip
**Revize Et** düğmesine basar. Kart `Action.Execute` ile backend revizyon
endpoint'ini çağırır; backend yeni bir `RequestId` üretir ve Teams host bu
yeni kimliği aynı conversation ile eşler. Rapor tamamlandığında sonuç yeni
bir proactive kart olarak gelir. Mevcut **Power BI’da Aç** eylemi korunur.

`WaitingForClarification` kartında kullanıcı istenen ek bilgiyi girip
**Açıklamayı Gönder** düğmesine basar. Backend aynı `RequestId` üzerindeki
talebi yeniden kuyruğa alır. Sonuç hazır olduğunda aynı conversation'a yeni
bir proactive kart gönderilir.

Her iki eylem de `AssociatedInputs.Auto` kullanan `Action.Execute` modelidir.
Invoke isteği yalnızca backend'in HTTP 202 kabul cevabını bekler; rapor
tamamlanana kadar açık tutulmaz. Kart input değerleri loglanmaz. Bu fazda
backend, doğrulanmış `oid`/`tid` sahipliği ve delegated `access_as_user`
scope'u uygular. Development ortamında Teams client bearer token göndermeden
backend'in sabit local principal'ı ile çalışır. Entra modunda Teams host SDK
token service'ten access token alır ve token'ı yalnızca ilgili backend
isteğine Bearer olarak ekler.

Manuel testte başarılı eylemden sonra input içermeyen acknowledgement kartı
görülmeli, ardından worker tamamladığında yeni proactive sonuç kartı
gelmelidir. Action.Execute ve replacement kart davranışı Agents Playground'a
ek olarak gerçek Teams desktop, web ve mobile istemcilerinde test edilmelidir.

Manuel smoke testte beklenen akış:

1. Kullanıcı bir rapor talebi gönderir.
2. İlk düz cevap işlem numarasını (`RequestId`) içerir.
3. Backend mock processing işlemi terminal duruma getirir.
4. Aynı konuşmaya proactive Adaptive Card gelir.
5. Kart özet ile işlem numarasını gösterir.
6. Power BI URL geçerliyse **Power BI’da Aç** düğmesi görünür.

Kart görünümü ve `OpenUrl` davranışı Agents Playground'a ek olarak gerçek
Teams desktop, web ve mobile istemcilerinde ayrıca manuel test edilmelidir.

`Teams:SkipAuth` yalnızca yerel Development mesaj intake testi içindir.
Internal report notification endpoint'indeki API key doğrulamasını atlamaz.
Kod, ayar `true` olsa bile Production ortamında Teams authentication
atlamasını etkinleştirmez.

## Production sınırlamaları

Bu fazdaki `RequestId -> ConversationId` hedef store'u ve teslim
deduplication store'u process-local ve in-memory'dir. Teams host yeniden
başladığında kayıtlar kaybolur; birden fazla Teams replica'sı kayıtları
paylaşmaz. Production'da hedef ve teslim kayıtları kalıcı bir veri
tabanında tutulmalıdır.

Kart action idempotency store'u da process-local ve in-memory'dir. Teams host
restart olduğunda duplicate invoke engeli kaybolur. Conversation eşlemesi MVP
seviyesinde minimum güvenlik sağlar. Backend Entra access token doğrulaması ve
user/tenant rapor sahipliği uygular. Teams adapter Entra modunda kullanıcı
access token'ını iletmeye hazırdır; ancak gerçek deployment için aşağıdaki
Entra, Azure Bot, public HTTPS endpoint ve Teams sideload kurulumu
tamamlanmalıdır. Kalıcı action idempotency ve notification target storage
gerekir. Backend queue ve notification outbox davranışları da
volatile/in-memory'dir.

API key service-to-service authentication için geçici bir MVP çözümüdür.
Faz 6/8'de Entra ID service-to-service authentication veya managed identity
ile değiştirilmelidir. Notification teslimi için ileride kalıcı event/outbox
ve gerekiyorsa Azure Service Bus eklenmelidir. Proactive notification
başarısız olsa bile rapor sonucu backend GET endpoint'i üzerinden
erişilebilir kalır.

Power BI URL'nin ilgili kullanıcı yetkilerine uygun üretilmesi backend/report
service sorumluluğudur. Karttaki `OpenUrl` düğmesi authorization sağlamaz.
Kart URL'sine embed token, access token veya başka bir secret konulmamalıdır.
Adaptive Card'ın görünümü ve davranışı gerçek Teams desktop, web ve mobile
istemcilerinde doğrulanmalıdır.

Gerçek Teams tenant bağlantısı sonraki dağıtım adımında Teams Developer CLI
login, public HTTPS tunnel, uygulama create/register ve sideload işlemlerini
gerektirir. Bu adım tenant kaydı, Entra ID, kalıcı storage veya Service Bus
eklemez.

## Teams kullanıcı authentication mode'ları

`TeamsUserAuthentication` bölümü iki açık mode destekler:

- `Development` yalnızca `ASPNETCORE_ENVIRONMENT=Development` olduğunda
  kullanılabilir. OAuth connection gerekmez, backend isteğine Authorization
  header eklenmez ve mevcut backend Development principal akışı korunur.
  Agents Playground akışı bu mode içindir.
- `Entra` Development, Staging veya Production ortamında seçilebilir.
  `OAuthConnectionName` zorunludur ve `Teams:SkipAuth` kesinlikle `false`
  olmalıdır. Her normal mesaj ve her revizyon/açıklama kart eylemi için SDK
  token service yeniden çağrılır.

Options startup sırasında doğrulanır. Production veya Staging'de
`Mode=Development`, Entra mode'da boş OAuth connection veya Entra mode ile
`Teams:SkipAuth=true` host başlangıcını durdurur. Internal proactive callback
API-key doğrulaması bu mode'dan bağımsızdır ve değiştirilmemiştir.

Yerel tokensız varsayılan:

```json
"TeamsUserAuthentication": {
  "Mode": "Development",
  "OAuthConnectionName": ""
}
```

Development environment altında gerçek Teams SSO testi için örnek override:

```powershell
$env:TeamsUserAuthentication__Mode = "Entra"
$env:TeamsUserAuthentication__OAuthConnectionName = "crm-analytics-sso"
$env:Teams__SkipAuth = "false"
```

`crm-analytics-sso` yalnızca örnek addır; Azure Bot OAuth connection adıyla
tam eşleşmelidir.

Kurulu Microsoft Teams SDK 2.0.9 assembly'lerinde app authentication
credential binding adları `Teams:ClientId`, `Teams:ClientSecret` ve
`Teams:TenantId` olarak doğrulanmıştır. Environment variable karşılıkları:

```powershell
$env:Teams__ClientId = "<entra-application-client-id>"
$env:Teams__TenantId = "<directory-tenant-id>"
$env:Teams__ClientSecret = "<secret-from-secure-store>"
```

Secret appsettings, source code, README veya Teams manifestine yazılmamalıdır.
Local/pilot kullanımında environment variable veya user-secrets; production'da
ise desteklenen certificate ya da managed identity yaklaşımı ayrıca
değerlendirilmelidir.

## Entra mode kullanıcı akışı

1. Kullanıcı Teams'e rapor talebini yazar.
2. SDK cached token bulamazsa OAuth/sign-in kartını gösterir; backend
   çağrılmaz, `RequestId` üretilmez ve ilk prompt saklanmaz.
3. Kullanıcı kurumsal hesabıyla sign-in işlemini tamamlar.
4. `OnSignIn` kullanıcıya talebini yeniden göndermesini söyler. Event token'ı
   okunmaz, gösterilmez, loglanmaz veya bir store'a yazılmaz.
5. Kullanıcının ikinci mesajında SDK cached SSO access token'ını döndürür.
6. Teams adapter token'ı değiştirmeden yalnızca o CrmAnalytics.Api
   `HttpRequestMessage` nesnesine Bearer olarak ekler.
7. Backend imza, issuer, audience, delegated `access_as_user` scope'u ile
   `oid` ve `tid` claim'lerini doğrular ve rapor sahipliğini bu principal ile
   uygular.
8. Mevcut queue/worker akışı raporu işler ve proactive sonuç kartı aynı
   conversation'a gelir.
9. Revize ve açıklama eylemlerinde SDK token service yeniden çağrılır. Token
   yoksa güvenli sign-in-required acknowledgement kartı döner; action token
   processed yapılmaz ve kullanıcı sign-in sonrasında eylemi yeniden dener.

İlk prompt'un sign-in sonrasına taşınmaması bilinçli veri minimizasyonudur.
Pending-message store yoktur ve sign-in tamamlanınca eski talep otomatik
işlenmez. Sign-in failure halinde yalnızca güvenli genel mesaj gösterilir;
SDK'nin teknik failure message değeri kullanıcıya veya loga taşınmaz. Güvenli
operation adı ve failure code loglanabilir.

Backend 401 veya 403 döndürürse response body gösterilmez. Normal mesaj güvenli
bir yeniden sign-in mesajına, kart eylemi retry edilebilir güvenli
acknowledgement kartına dönüşür. Bu durumda card action idempotency token'ı
processed yapılmaz.

## Teams SSO ve backend access token kurulumu

Bu MVP tek App Registration kullanır. Azure Bot identity, Teams
`webApplicationInfo.id`, exposed API resource ve CrmAnalytics.Api
`AzureAd:ClientId` aynı Application Client ID değeridir. Application ID URI
`api://<application-client-id>`, delegated scope
`api://<application-client-id>/access_as_user` olur. Teams'in aldığı token
doğrudan CrmAnalytics.Api audience'ına yöneldiği için OBO exchange veya
Microsoft Graph çağrısı yoktur.

### A. Entra App Registration

1. Microsoft Entra admin center'da single-tenant bir App Registration
   oluşturun. Application (client) ID ve Directory (tenant) ID değerlerini
   güvenli deployment kayıtlarına alın.
2. **Expose an API** altında Application ID URI değerini
   `api://<client-id>` yapın ve delegated `access_as_user` scope'unu ekleyin.
3. Entra application manifestinde access token sürümünü v2 olacak biçimde
   ayarlayın (`requestedAccessTokenVersion: 2`).
4. **Authorized client applications** altında Teams desktop/mobile
   (`1fec8e78-bce4-4aaf-ab1b-5451cc387264`) ve Teams web
   (`5e3ce6c0-2b1f-4285-8d4b-75ee78787346`) istemcilerini exposed scope için
   pre-authorize edin.
5. Public global Bot Framework token service kullanılıyorsa Web redirect URI
   olarak `https://token.botframework.com/.auth/web/redirect` ekleyin.
   Data-residency veya sovereign cloud kullanılıyorsa aynı bölgesel OAuth
   endpoint'in redirect URI'sini seçin; global ve bölgesel adresleri
   karıştırmayın.
6. Local/pilot için client credential gerekiyorsa secret değerini yalnızca
   environment variable, user-secrets veya secret store'da tutun. Secret'ı
   source code'a, README'ye veya manifest paketine yazmayın.

Güncel client pre-authorization ve bot SSO ayrıntıları için Microsoft'un
[Entra bot SSO registration](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/how-to/authentication/bot-sso-register-aad)
ve [OAuth URL listesi](https://learn.microsoft.com/en-us/azure/bot-service/ref-oauth-redirect-urls?view=azure-bot-service-4.0)
kontrol edilmelidir.

### B. CrmAnalytics.Api

Gerçek değerleri environment variable veya deployment secret/configuration
store üzerinden verin:

```powershell
$env:CrmAnalyticsAuthentication__Mode = "Entra"
$env:CrmAnalyticsAuthentication__RequiredScope = "access_as_user"
$env:AzureAd__TenantId = "<directory-tenant-id>"
$env:AzureAd__ClientId = "<same-application-client-id>"
$env:AzureAd__Audience = "api://<same-application-client-id>"
```

API Client ID, Teams SSO ve Azure Bot için kullanılan Application Client ID ile
aynı olmalıdır. Backend'in Faz 6.1 JWT doğrulaması issuer, signature, audience,
scope, `oid` ve `tid` kontrollerinin tek authorization kaynağıdır. Teams host
JWT parse etmez ve claim'e dayalı karar vermez.

### C. Azure Bot resource

1. Azure-managed bir Bot resource oluşturun veya mevcut Teams-managed botu
   Azure-managed yapıya geçirin.
2. Bot identity'yi aynı App Registration Application Client ID ve aynı
   single-tenant Tenant ID ile ilişkilendirin.
3. Messaging endpoint'i public HTTPS Teams host
   `https://<host>/api/messages` adresine ayarlayın.
4. Bot resource altında Microsoft Entra ID provider kullanan bir OAuth
   connection oluşturun.
5. Connection name'i `TeamsUserAuthentication:OAuthConnectionName` ile tam
   eşleştirin.
6. Client ID ve Tenant ID olarak aynı App Registration değerlerini kullanın.
7. Scope'u `api://<client-id>/access_as_user` yapın.
8. Credential'ı source code'a yazmayın; portal secret/certificate
   configuration veya desteklenen workload identity yöntemini kullanın.
9. Azure portal üzerinden OAuth connection testini çalıştırıp başarılı
   olduğunu doğrulayın.

### D. Teams manifest

Repository'de şu anda `manifest.json` veya `appPackage` klasörü yoktur. Bu
nedenle deployment manifesti tahmin edilerek oluşturulmamıştır. Gerçek manifest
eklendiğinde mevcut template variable biçimini koruyarak bot ID'yi aynı
Application Client ID ile yapılandırın ve şu semantiği ekleyin:

```json
"webApplicationInfo": {
  "id": "<ENTRA_APPLICATION_CLIENT_ID>",
  "resource": "api://<ENTRA_APPLICATION_CLIENT_ID>"
}
```

Public global token service için `validDomains` listesine
`token.botframework.com` ekleyin. Bölgesel OAuth endpoint kullanılıyorsa
Microsoft'un bölgesel token domain'ini kullanın. `webApplicationInfo.resource`
Expose an API altındaki Application ID URI ile karakter karakter eşleşmelidir.
Manifestte gerçek tenant ID, secret veya access token bulunmamalıdır.
Microsoft'un güncel
[Teams SSO manifest rehberi](https://learn.microsoft.com/en-us/microsoftteams/platform/bots/how-to/authentication/bot-sso-manifest)
paketlemeden önce kontrol edilmelidir.

Manifest paketini tenant'a sideload edin. Bu repository adımı portal, CLI,
tunnel veya sideload işlemini otomatik çalıştırmaz.

### E. Gerçek test

Teams user authentication Agents Playground üzerinden uçtan uca
doğrulanamaz. Gerçek test için public HTTPS endpoint, Azure Bot messaging
endpoint ayarı ve gerçek Teams tenant'ına sideload edilmiş app gerekir. İlk
kullanımda consent gösterilebilir.

En az şu kontrolleri yapın:

1. One-to-one chat'te ilk mesaj, sign-in, talebi yeniden gönderme ve proactive
   sonuç akışını tamamlayın.
2. Group chat'te aynı akışı ve kullanıcıya özgü token davranışını doğrulayın.
3. Revizyon ve clarification kart eylemlerini hem cached token varken hem
   yeniden sign-in gerekirken deneyin.
4. Channel kapsamının SSO desteğini kullanılan Teams istemcisi ve güncel
   Microsoft dokümantasyonuna göre ayrıca değerlendirin.
5. Backend'de yanlış audience/scope token'larının 401/403 ile reddedildiğini
   ve kullanıcıya teknik response body taşınmadığını doğrulayın.

Entra App Registration, Azure Bot resource, public endpoint ve tenant
sideload bu repository çalışmasında oluşturulmadığı için gerçek SSO smoke testi
yapılmış sayılmaz.

## Güvenlik ve production sınırları

- Access token yalnızca method çağrı zincirinde geçici
  `BackendApiAuthorization` nesnesiyle taşınır. Singleton, dictionary,
  notification target, queue, card action data veya idempotency store'a
  yazılmaz; hash'i dahil loglanmaz.
- `HttpClient.DefaultRequestHeaders.Authorization` kullanılmaz. Her create,
  revise ve clarification çağrısı ayrı `HttpRequestMessage` oluşturur ve
  Bearer header yalnızca o mesaja eklenir.
- Teams host token'ı parse etmez; `oid`, `tid`, `scp`, `roles`, audience veya
  signature üzerinde karar vermez. ID token ya da Graph token backend'e
  iletilmez; user/tenant ID için ayrı client header eklenmez.
- Kalıcı token cache ve pending prompt store eklenmemiştir. SDK token service
  her kullanıcı işleminde çağrılır.
- Development mode gerçek authentication sağlamaz.
- Tek App Registration bu MVP'nin tercihidir. Teams ve backend ileride ayrı
  security boundary olursa OBO gerekebilir; bu fazda OBO/MSAL/Graph yoktur.
- Production'da certificate veya managed identity, client secret'a tercih
  edilmek üzere deployment mimarisiyle ayrıca değerlendirilmelidir.
- User/tenant ownership backend'de doğrulanmaya devam eder. App role ve
  region/store data-scope kararları yalnız backend'de verilir.
- Request/conversation/target/idempotency kayıtları hâlâ in-memory;
  queue/outbox hâlâ volatile'dır.

## Entra App Roles ve veri kapsamı

Backend App Registration üzerinde `Report.User`, `Report.Viewer` ve
`Report.Admin` app role'larını oluşturun. Display name, allowed member type ve
manuel atama adımlarıyla data-scope configuration örneği root
[`README.md`](../../README.md#entra-app-roles-ve-veri-kapsamı) dosyasındadır.
Role value değerleri exact ve case-sensitive'dir. Kullanıcı veya security group
Enterprise Applications → Users and groups üzerinden role atanabilir.

- Report User — value `Report.User`, member types Users/Groups: kendi raporunu
  oluşturma, okuma, revize etme ve açıklama gönderme.
- Report Viewer — value `Report.Viewer`, member types Users/Groups: kendi
  mevcut raporunu ve konuşma geçmişini okuma.
- Report Admin — value `Report.Admin`, member types Users/Groups: mevcut rapor
  operasyonları; veri kapsamı ayrıca backend policy store tarafından belirlenir
  ve başka kullanıcı/tenant ownership bypass edilmez.

Teams adapter access token'ı backend'e per-request Bearer olarak iletir; JWT
parse etmez, `roles`, `groups` veya `wids` claim'inden karar üretmez ve
region/store scope kabul etmez. Backend, delegated `access_as_user`, `oid`,
`tid`, operation app role'u ve processing işlemlerinde aynı `oid`+`tid`
assignment'ını birlikte doğrular. Rol atamasından sonra cached token yerine
yeni token almak veya yeniden sign-in olmak gerekebilir.

Backend 403 cevabı normal mesajda, revizyonda ve clarification işleminde
operation-specific güvenli metne çevrilir. Backend response body, rol ve
region/store ayrıntıları gösterilmez. Başarısız kart action token'ı processed
işaretlenmez; yetki düzeltildikten sonra kullanıcı yeniden deneyebilir. Mevcut
401 sign-in akışı ayrı kalır.

## Rejected bildirimi ve SQL kapsamı

Kontrollü guardrail/policy reddi, teknik `Failed` kartından ayrı bir
`Rejected` kartı üretir. Kart başlığı “Rapor talebi işlenemedi”, durumu
“Reddedildi” olur; güvenli `RejectionMessage`, işlem numarası ve UTC güncellenme
zamanını gösterir. ReasonCode/RejectionCode, ErrorCode, prompt, SQL,
schema/view/column veya parameter bilgisi karta eklenmez. Rejected kartında
input, ExecuteAction veya Power BI action yoktur. Aynı request/status/timestamp
için mevcut notification dedup davranışı korunur.

Backend'in SQL production sözleşmesi store-level kapsamı desteklemediği sürece
store-limited kullanıcılar fail-closed edilir. Store kısıtı yok sayılmaz,
region filtresine indirgenmez ve Unrestricted'a dönüştürülmez. Region-only
kapsamda değerlerin `customer_state` canonical kodları (örneğin `SP`, `RJ`)
olması gerekir; Teams bu değerleri almaz, dönüştürmez veya prompt/action
data'sına taşımaz.
# Durable target, notification ve action akışı

Production Teams host EF Core veya Infrastructure referansı taşımaz. Target
kayıtları ve card action idempotency state'i `BackendApi` HttpClient üzerinden,
`X-CrmAnalytics-Notification-Key` API key'iyle korunan backend internal
endpoint'lerine gider. Normal create ve başarılı revision yeni request-to-
conversation mapping'i kaydeder; clarification aynı request mapping'ini kullanır.

Backend notification outbox callback'i doğrulanmış internal `DeliveryId` ve
`ConversationId` taşır. Kart mevcut `IReportNotificationCardFactory` ile
üretilir; 204 başarı, 503 retry edilebilir send hatasıdır. Development'taki
process-local target/delivery/action store'ları yalnız geriye uyumluluk fallback'i
olarak kalır. Production ayarlarında internal API key boş bırakılamaz; secret
configurasyon/secret store üzerinden sağlanmalıdır. API key geçici S2S
mekanizmasıdır ve Entra workload identity/managed identity ile değiştirilmesi
önerilir.

Teslimat at-least-once'dur, exactly-once değildir: Teams send ile backend
Delivered kaydı arasındaki crash duplicate kart üretebilir. Action mutation ile
completion kaydı arasındaki crash de retry doğurabilir; backend domain/status
kontrolleri ikinci mutation'ı reddetmelidir. Release öncesinde Adaptive Card ve
SSO akışları gerçek Teams desktop, web ve mobile istemcilerinde test edilmelidir.
