# Pilot Azure Key Vault configuration

Bu belge `infra/azure/bicep/main.bicep` ile oluşturulan API ve Teams
Container App'lerinin secret yapılandırmasını açıklar. Şablon Key Vault veya
secret oluşturmaz ve secret değerini ARM/Bicep parametresi, output, GitHub
Actions ya da `appsettings` içine taşımaz.

## Kimlik modeli

Her Container App iki managed identity kullanır:

- Mevcut user-assigned `runtimeIdentityResourceId` korunur. ACR image pull ve
  SQL, Service Bus, Fabric ve Power BI gibi Azure servislerinde uygulama
  kimlik doğrulaması için `runtimeIdentityClientId` kullanılmaya devam eder.
- API ve Teams'e ayrı system-assigned identity eklenir. Mevcut Bicep akışında
  Container Apps Key Vault referansları runtime UAMI'yi kullanır; bu sayede ilk
  deployment'ta system identity oluşumu ile secret çözümleme arasında döngü
  oluşmaz.

Key Vault'un Azure RBAC permission modelini kullanması gerekir. Bicep, built-in
**Key Vault Secrets User** rolünü (`4633458b-17de-408a-b874-0445c86b69e6`)
vault genelinde değil, ihtiyaç duyulan mevcut secret üzerinde atar. Bu proje
için secret düzeyi scope, tek vault içinde API/Teams ayrımını ve gerçekten ortak
olan internal API key paylaşımını uygular. Microsoft genel olarak uygulama ve
ortam başına vault önerir; pilot sonrasında ayrı vault modeline geçilirse aynı
eşlemeler vault scope RBAC ile korunabilir.

Kaynaklar:

- [Container Apps Key Vault secret references](https://learn.microsoft.com/azure/container-apps/manage-secrets)
- [Key Vault RBAC guide and secret-scope assignments](https://learn.microsoft.com/azure/key-vault/general/rbac-guide)

## Secret sınıflandırması ve eşlemeleri

Container App environment variable adlarında ASP.NET Core nested
configuration için çift alt çizgi kullanılır.

| Configuration path | Environment variable | Uygulama | Sınıf | Key Vault secret adı |
| --- | --- | --- | --- | --- |
| `ConnectionStrings:CrmAnalytics` | `ConnectionStrings__CrmAnalytics` | API | Secret: application database connection string | `crm-analytics-application-db` |
| `ConnectionStrings:QueryDwh` | `ConnectionStrings__QueryDwh` | API | Secret: DWH connection string | `crm-analytics-query-dwh` |
| `ConnectionStrings:QueryOltp` | `ConnectionStrings__QueryOltp` | API, yalnız `queryOltpEnabled=true` | Secret: optional OLTP connection string | `crm-analytics-query-oltp` |
| `TeamsNotifications:ApiKey` | `TeamsNotifications__ApiKey` | API | Secret: internal callback API key | `crm-analytics-internal-api-key` |
| `BackendApi:InternalApiKey` | `BackendApi__InternalApiKey` | Teams | Secret: internal API key | `crm-analytics-internal-api-key` |
| `ReportNotifications:ApiKey` | `ReportNotifications__ApiKey` | Teams | Secret: callback API key | `crm-analytics-internal-api-key` |
| `Teams:ClientSecret` | `Teams__ClientSecret` | Teams, yalnız `teamsClientSecretIntegrationEnabled=true` | Secret: Teams/Bot application credential | `crm-analytics-teams-client-secret` |
| `CopilotStudio:DirectLineSecret` | `CopilotStudio__DirectLineSecret` | Teams, yalnız `copilotStudioDirectLineSecretIntegrationEnabled=true` | Secret: Copilot Studio Direct Line credential | `crm-analytics-copilot-direct-line-secret` |
| `Messaging:AzureServiceBus:ConnectionString` | `Messaging__AzureServiceBus__ConnectionString` | API | Secret türüdür; pilotta kullanılmaz | Yok; Production/Staging validator managed identity zorlar |

`crm-analytics-internal-api-key` bilinçli olarak ortaktır: Teams, API'nin
internal endpoint'lerine çağrı yaparken `BackendApi:InternalApiKey`; API, Teams
callback endpoint'ine çağrı yaparken `TeamsNotifications:ApiKey` kullanır ve
Teams aynı değeri `ReportNotifications:ApiKey` ile doğrular. Diğer secret'lar
paylaşılmaz.

Pilot parameter set'inde `teamsClientSecretIntegrationEnabled=false` değeridir.
Bu bayrak `crm-analytics-teams-client-secret` Key Vault referansını,
`Teams__ClientSecret` eşlemesini ve ilgili secret-scope rol atamasını birlikte
etkinleştirir. Secret yalnız Teams Container App'e bağlanır; API Container App'in
secret listesine eklenmez. Bu bayrak internal API key paylaşımını değiştirmez.

Teams/Bot App Registration `crm-analytics-teams-bot`, Application Client ID
`3e277fe2-0da0-4149-80da-2168ac44e7b9`, Application Object ID
`eeed61b7-4c9d-444f-ac82-efb3d76de126` ve service principal Object ID
`a8e37340-3f61-4e32-af40-f7eeea1478b7` ile single-tenant
(`AzureADMyOrg` / `SingleTenant`) olarak hazırlanmıştır. Credential
`2026-08-04T21:38:47.3659122Z` tarihinde oluşturulmuş ve
`2027-01-31T21:38:47.3659122Z` tarihinde sona erecektir. Key Vault secret
`crm-analytics-teams-client-secret` hedef Key Vault'ta mevcut değildir. Değeri
yeniden üretilmez veya tahmin edilmez. `crm-analytics-teams-oauth` Azure Bot
üzerinde mevcut OAuth connection adıdır.

DWH ve OLTP connection string'leri pilotta
`Authentication=Active Directory Managed Identity` kullanmalı; kullanıcı adı,
parola veya başka credential içermemelidir. Yine de connection string sınıfında
oldukları için Key Vault referansı olarak tutulurlar. Service Bus, Fabric ve
Power BI doğrudan managed identity kullandığından onlar için client secret veya
connection string oluşturulmaz.

## Normal Container App ayarları

Aşağıdaki deployment/ortam değerleri secret değildir ve Bicep tarafından
normal environment variable olarak verilir.

| Configuration path | Environment variable | Uygulama | Kaynak |
| --- | --- | --- | --- |
| `CrmAnalyticsAuthentication:Mode` | `CrmAnalyticsAuthentication__Mode` | API | Sabit `Entra` |
| `AzureAd:TenantId` | `AzureAd__TenantId` | API | `azureAdTenantId` |
| `AzureAd:ClientId` | `AzureAd__ClientId` | API | `azureAdClientId` |
| `AzureAd:Audience` | `AzureAd__Audience` | API | `azureAdAudience` |
| `Persistence:Provider` | `Persistence__Provider` | API | Sabit `SqlServer` |
| `QueryExecution:Provider` | `QueryExecution__Provider` | API | Sabit `SqlClient` |
| `QueryExecution:Dwh:Enabled` | `QueryExecution__Dwh__Enabled` | API | Sabit `true` |
| `QueryExecution:Dwh:AuthenticationMode` | `QueryExecution__Dwh__AuthenticationMode` | API | Sabit `ManagedIdentity` |
| `QueryExecution:Dwh:ManagedIdentityClientId` | `QueryExecution__Dwh__ManagedIdentityClientId` | API | `runtimeIdentityClientId` |
| `QueryExecution:Oltp:Enabled` | `QueryExecution__Oltp__Enabled` | API | `queryOltpEnabled` |
| `QueryExecution:Oltp:AuthenticationMode` | `QueryExecution__Oltp__AuthenticationMode` | API | Sabit `ManagedIdentity` |
| `QueryExecution:Oltp:ManagedIdentityClientId` | `QueryExecution__Oltp__ManagedIdentityClientId` | API | `runtimeIdentityClientId` |
| `Messaging:Provider` | `Messaging__Provider` | API | Sabit `AzureServiceBus` |
| `Messaging:AzureServiceBus:AuthenticationMode` | `Messaging__AzureServiceBus__AuthenticationMode` | API | Sabit `ManagedIdentity` |
| `Messaging:AzureServiceBus:FullyQualifiedNamespace` | `Messaging__AzureServiceBus__FullyQualifiedNamespace` | API | `serviceBusNamespace` |
| `Messaging:AzureServiceBus:QueueName` | `Messaging__AzureServiceBus__QueueName` | API | `serviceBusQueueName` |
| `Messaging:AzureServiceBus:ManagedIdentityClientId` | `Messaging__AzureServiceBus__ManagedIdentityClientId` | API | `runtimeIdentityClientId` |
| `Analytics:Provider` | `Analytics__Provider` | API | `analyticsProvider`; pilotta `Direct` |
| `Analytics:AllowDirectInProtectedEnvironments` | `Analytics__AllowDirectInProtectedEnvironments` | API | Pilot `Direct` seçimi için açık onay parametresi |
| `Analytics:Fabric:WorkspaceId` | `Analytics__Fabric__WorkspaceId` | API | `fabricWorkspaceId` |
| `Analytics:Fabric:ItemId` | `Analytics__Fabric__ItemId` | API | `fabricItemId` |
| `Analytics:Fabric:JobType` | `Analytics__Fabric__JobType` | API | `fabricJobType` |
| `Analytics:Fabric:ManagedIdentityClientId` | `Analytics__Fabric__ManagedIdentityClientId` | API | `runtimeIdentityClientId` |
| `Reporting:Provider` | `Reporting__Provider` | API | Sabit `PowerBi` |
| `Reporting:PowerBi:WorkspaceId` | `Reporting__PowerBi__WorkspaceId` | API | `powerBiWorkspaceId` |
| `Reporting:PowerBi:ReportId` | `Reporting__PowerBi__ReportId` | API | `powerBiReportId` |
| `Reporting:PowerBi:SemanticModelId` | `Reporting__PowerBi__SemanticModelId` | API | `powerBiSemanticModelId` |
| `Reporting:PowerBi:ManagedIdentityClientId` | `Reporting__PowerBi__ManagedIdentityClientId` | API | `runtimeIdentityClientId` |
| `TeamsNotifications:Enabled` | `TeamsNotifications__Enabled` | API | Sabit `true` |
| `TeamsNotifications:BaseUrl` | `TeamsNotifications__BaseUrl` | API | Deterministic Teams app name + Container Apps Environment `defaultDomain` |
| `BackendApi:BaseUrl` | `BackendApi__BaseUrl` | Teams | Deterministic internal API FQDN (`<api>.internal.<defaultDomain>`) |
| `ReportNotifications:Enabled` | `ReportNotifications__Enabled` | Teams | Sabit `true` |
| `Teams:SkipAuth` | `Teams__SkipAuth` | Teams | Sabit `false` |
| `Teams:TenantId` | `Teams__TenantId` | Teams | `teamsTenantId` |
| `Teams:AppType` | `Teams__AppType` | Teams | `teamsAppType`; pilotta `SingleTenant` |
| `Teams:ClientId` | `Teams__ClientId` | Teams | `teamsClientId` |
| `TeamsUserAuthentication:Mode` | `TeamsUserAuthentication__Mode` | Teams | Sabit `Entra` |
| `TeamsUserAuthentication:OAuthConnectionName` | `TeamsUserAuthentication__OAuthConnectionName` | Teams | `teamsOAuthConnectionName` |

Tenant ID, client ID, workspace/report/item/semantic model ID, endpoint, queue
adı, provider seçimi, timeout ve limitler Key Vault'a konulmaz.

## appsettings ile sağlanan diğer normal ayarlar

Bu ayarlar da configuration değerleridir; pilot Bicep bunları değiştirmez ve
repository'deki güvenli production varsayılanları kullanılır. Her path'in
environment karşılığı `:` yerine `__` yazılarak elde edilir.

| API configuration path'leri | Tür |
| --- | --- |
| `CrmAnalyticsAuthentication:RequiredScope` | Scope adı |
| `AzureAd:Instance` | Endpoint |
| `ReportAuthorization:RolePermissions:<role>:<index>` | Yetki eşleme listesi |
| `ReportDataAccess:Provider`, `ReportDataAccess:Assignments:<index>:*` | Provider ve data-scope eşlemeleri |
| `Persistence:SqlServer:CommandTimeoutSeconds`, `EnableRetryOnFailure`, `MaxRetryCount`, `MaxRetryDelaySeconds` | Timeout/retry |
| `QueryExecution:Dwh:ConnectionStringName`, `CommandTimeoutCeilingSeconds`, `MaxRows`, `MaxColumns`, `MaxCellCharacters`, `MaxResultBytes`, `MaxRetryCount`, `RetryBaseDelayMilliseconds` | DWH routing/limit/retry |
| `QueryExecution:Oltp:ConnectionStringName`, `CommandTimeoutCeilingSeconds`, `MaxRows`, `MaxColumns`, `MaxCellCharacters`, `MaxResultBytes`, `MaxRetryCount`, `RetryBaseDelayMilliseconds` | OLTP routing/limit/retry |
| `SqlProduction:Provider`, `SqlVersionName`, `Source`, `ConfidenceThreshold` | Provider ve guardrail seçimi |
| `Analytics:AllowDirectInProtectedEnvironments` | Güvenlik bayrağı |
| `Analytics:Fabric:ScenarioKey`, `PollingIntervalSeconds`, `TimeoutSeconds`, `MaxRetries`, `AuthenticationMode` | Fabric routing/timeout/retry |
| `Reporting:PowerBi:RefreshBeforeReturn`, `RefreshPollingEnabled`, `RefreshPollingIntervalSeconds`, `RefreshTimeoutSeconds`, `MaxRetries`, `AuthenticationMode` | Power BI refresh/timeout/retry |
| `Messaging:AzureServiceBus:QueueName`, `PrefetchCount`, `MaxConcurrentCalls`, `MaxAutoLockRenewalMinutes`, `TryTimeoutSeconds`, `TransportType` | Queue ve tüketici ayarları |
| `Messaging:AzureServiceBus:Retry:MaxRetries`, `DelaySeconds`, `MaxDelaySeconds`, `Mode` | Service Bus retry |
| `OutboxDispatcher:Enabled`, `BatchSize`, `PollingIntervalSeconds`, `LockDurationSeconds`, `MaxAttempts`, `BaseRetryDelaySeconds`, `MaxRetryDelaySeconds`, `PublishedRetentionDays` | Outbox kontrol/limitleri |
| `TeamsNotifications:TimeoutSeconds`, `TargetNotReadyRetryCount`, `TargetNotReadyInitialDelayMilliseconds` | Callback timeout/retry |
| `Logging:LogLevel:*`, `AllowedHosts` | Host/log ayarları |

| Teams configuration path'leri | Tür |
| --- | --- |
| `BackendApi:TimeoutSeconds` | Timeout |
| `Logging:LogLevel:*`, `Logging:Microsoft.Teams:Level`, `AllowedHosts` | Host/log ayarları |

`appsettings.Development.json` içindeki Mock/InMemory provider'lar, development
identity fixture'ları ve local endpoint'ler yalnız `Development` ortamında
override edilir. `docker-compose.local.yml` yalnız
`ASPNETCORE_ENVIRONMENT=Development` ve Teams için
`BackendApi__BaseUrl=http://api:8080` verir; Key Vault gerektirmez.

## Secret hazırlama ve deployment sırası

1. Mevcut veya ayrı bir adımda oluşturulmuş Key Vault'ta Azure RBAC permission
   modelinin etkin olduğunu doğrulayın.
2. Secret değerlerini güvenli operatör ortamından ekleyin. Secret değer
   dosyalarını repository dışında tutun. Örnek komut değer içermez:

   ```powershell
   az keyvault secret set `
     --vault-name '<key-vault-name>' `
     --name 'crm-analytics-application-db' `
     --file '<path-outside-repository>' `
     --encoding utf-8 `
     --output none
   ```

3. Aynı işlemi gereken DWH, optional OLTP, internal API key ve Teams client
   secret için yapın. İsterseniz `deploy/scripts/set-pilot-secrets.example.ps1`
   dosyasını yalnız açık `-Apply` onayıyla kullanın; script değer içermez ve
   varsayılan çalışmada Azure'a yazmaz.
4. Gerçek değer içermeyen deployment parameter dosyasını repository dışında
   hazırlayın. `keyVaultIntegrationEnabled=true`, vault/resource group adı ve
   secret adlarını verin. Secret URI'leri Bicep version olmadan üretir.
5. `az bicep build` ve Azure erişimi olan kontrollü ortamda `az deployment
   group validate`/`what-if` çalıştırın. Bu repository hazırlığı Azure login,
   validation deployment veya deployment çalıştırmaz.
6. Deployment, runtime UAMI principal ID'si için yalnız gereken secret-scope
   role assignment'larını Container App'ten önce uygular. RBAC propagation
   kısa süre gecikebilir; ilk secret çözümleme hatasında identity/scope ve RBAC
   modelini kontrol edin.

## Container Apps secret çözümleme ve rotation

Bicep'teki Container App secret kaydı `keyVaultUrl` ve runtime UAMI resource ID'sini
kullanır. Container environment variable değeri secret değildir; yerel
Container App secret adına `secretRef` verir. ASP.NET Core environment provider
bu değeri mevcut `IConfiguration`/Options path'ine taşır; uygulama Key Vault SDK
veya `SecretClient` çağırmaz.

Secret URI'leri version içermez. Yeni Key Vault version'ı eklenirken kod veya
Bicep parameter değişikliği gerekmez. Container Apps version'sız referansı
yeniden çözer; çalışan revision'ın değeri hemen alması gerekiyorsa secret
güncellemesinden sonra revision'ı kontrollü biçimde yeniden başlatın. Microsoft
Container Apps dokümantasyonuna göre çalışan container'ın yeni secret değerini
görmesi için revision restart gerekir.

## Fail-fast ve bilinen deployment ön koşulları

- API; Entra tenant/client, SQL persistence connection, etkin DWH connection,
  Teams callback key, Service Bus namespace ve provider ayarlarını mevcut
  `ValidateOnStart` validator'larıyla doğrular.
- Teams; backend URL/internal key, callback key, OAuth connection ve artık
  `Teams:ClientId`, `Teams:TenantId`, `Teams:AppType=SingleTenant` ve
  `Teams:ClientSecret` değerlerini
  Production/Staging başlangıcında doğrular.
- `keyVaultIntegrationEnabled=false` güvenli varsayılandır: Key Vault secret
  referansı ve RBAC oluşturulmaz. Production uygulamalarının boş zorunlu
  değerlerle çalışması beklenmez; fail-fast davranışı bunu görünür kılar.
- Mevcut `AnalyticsOptionsValidator`, onaylı Fabric staging/parameter contract
  repository'de bulunmadığı için `FabricJob` provider'ını bilinçli olarak
  fail-closed tutar. Pilot `analyticsProvider=Direct` ve açık
  `allowDirectAnalyticsInProtectedEnvironments=true` onayı kullanır; böylece
  FabricJob seçilmez. İleride FabricJob seçilecekse staging contract blocker'ı
  ayrıca çözülmelidir; Key Vault hazırlığı bu güvenlik kararını değiştirmez.

## CI ve güvenlik notları

Mevcut GitHub Actions dosyalarında Container Apps/Bicep deployment job'u yoktur.
Eski Web App workflow'u Azure login için OIDC client/tenant/subscription ID
repository secret'larını kullanır, fakat uygulama secret değeri taşımaz. İleride
pilot deployment workflow'u eklenirse yalnız normal Bicep parametreleri ve Key
Vault/secret adlarını geçmeli; connection string, API key veya client secret'ı
workflow input'u ya da GitHub secret'ından Container App'e kopyalamamalıdır.
