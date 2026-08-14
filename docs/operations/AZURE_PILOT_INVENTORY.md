# Azure pilot kaynak envanteri

## Targeted Teams legal-pages revision - 2026-08-05

This section is the current as-built state and supersedes older Teams runtime
status statements below. The repository was clean on branch
`feature/deploy-and-smoke-test` at SHA
`07c74bdf4e4bbf2edab9831aca9522f5d1abbd8d`; `git diff --check` passed and the
pilot parameter file was already pinned to the new Teams digest.

- No resource-group or `main.bicep` deployment ran. No image was built or pushed.
- Teams revision `crm-analytics-pilot-teams--legalpages1` was copied from
  `crm-analytics-pilot-teams--0000001` with only the image changed to
  `crmprojectacr634c.azurecr.io/crm-analytics-teams@sha256:580c683e896ddcdd0310c3369ea606d98620bb8086bf00c5c8339da9d301adbf`.
- Environment variables, secret-reference names, runtime/system identities,
  CPU/memory, probes, scale, ingress port 8080, backend API URL, and OAuth
  connection name matched the source revision. Secret values were not read or
  changed.
- The new revision is active, running, healthy, 1/1 ready, restart count 0, and
  receives 100 percent traffic. The old revision receives zero traffic and was
  deactivated only after all health and HTTP gates passed; it was not deleted.
- Public GET checks for `/`, `/privacy`, `/terms`, `/health/live`, and
  `/health/ready` returned HTTP 200. HEAD `/api/messages` returned HTTP 405,
  confirming the POST-only route without attempting an anonymous POST.
- API revision `crm-analytics-pilot-api--uamifix1` remains healthy with one
  replica and the unchanged approved digest
  `sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`.
- Azure Bot messaging endpoint remains
  `https://crm-analytics-pilot-teams.mangodesert-3e89f5b7.swedencentral.azurecontainerapps.io/api/messages`;
  the Teams channel is enabled and OAuth connection
  `crm-analytics-teams-oauth` remains provisioned. ACR admin access remains
  disabled.

Before/after hashes were identical for the API (`30d772d1...f22`), managed
environment (`95aa4f2e...d10`), Azure Bot (`e178d241...273`), OAuth connection
(`fd54067d...9b2`), Teams channel (`e346aad6...064`), Key Vault role assignments
(`7712ef44...4da`), SQL inventory (`bbf3c037...a3e`), Service Bus inventory
(`d0ad1c8b...363`), and Fabric inventory (`4f53cda1...945`). Azure Activity Log
write records in the deployment window targeted only
`Microsoft.App/containerApps/crm-analytics-pilot-teams`.

The validated sideload package is
`deploy/teams/crm-analytics-pilot-teams-app.zip`; installation remains a
manual Developer Portal and Teams client action.

## UAMI connection selection recovery - 2026-08-05

- Application DB connection configuration was updated in Key Vault so
  `Active Directory Managed Identity` explicitly selects runtime UAMI client ID
  `da156325-7c70-46fc-b899-024f9d73b994`. A new enabled secret version retained
  the existing connection-string content type and tag metadata. The secret value
  was not printed, logged, written to a repository file, or included in this
  inventory.
- The API Key Vault reference remains versionless and continues to use the API
  system-assigned identity. Teams still has only the internal API key and bot
  secret references; it has no SQL, DWH, or OLTP secret access.
- Existing API revision `crm-analytics-pilot-api--0000002` was restarted after
  secret sync. Recovery revision `crm-analytics-pilot-api--uamifix1` was then
  created from the same approved digest to guarantee latest-secret resolution.
  It remained unhealthy because Azure SQL continued to reject the
  token-identified principal. Teams revision
  `crm-analytics-pilot-teams--0000001` also remained unhealthy. The four health
  endpoint HTTP 200 checks therefore did not pass.
- Application SQL runtime authorization did not pass. Fabric DWH, Fabric OLTP,
  and Service Bus runtime message smokes were not started because a healthy API
  runtime was unavailable. Service Bus remained at zero active, dead-letter, and
  scheduled messages.
- No old revision was deactivated or deleted because no replacement revision
  became healthy. API traffic currently targets the unhealthy recovery revision;
  Teams traffic remains on its unhealthy latest revision.
- The only SQL firewall rule remains pilot rule `AllowAllWindowsAzureIps`
  (`0.0.0.0` to `0.0.0.0`). Production hardening debt remains: prove a custom
  VNet/NAT Gateway or private-endpoint route and then remove this rule.

## Pilot recovery runtime inventory - 2026-08-05

This section supersedes the older pre-deployment inventory statements below.
Deployment `crm-pilot-recovery-20260805-144323` completed with `Succeeded` in
tenant `2e010224-86ea-4b34-93ea-f9833137c80e`, subscription
`634ccf7f-1073-4965-9385-9ae3bbef1533`, and resource group `crm-project-rg`.

- `crm-analytics-pilot-env` remains the existing Sweden Central environment.
- API remains internal on port 8080 and pinned to the approved API digest.
- Teams remains external on port 8080 and pinned to the approved Teams digest.
- Runtime URLs are now derived from deterministic app names and the environment
  `defaultDomain`. The internal API form includes Azure Container Apps' required
  `.internal` label. No pilot URL parameter override or hostname placeholder remains.
- Non-secret outputs now include the environment/app names, both FQDNs, and both
  latest revision names.
- The only SQL firewall rule is the pilot-only `AllowAllWindowsAzureIps` rule
  (`0.0.0.0` to `0.0.0.0`). It was required because the Consumption environment
  does not provide a stable outbound IP and Azure SQL previously rejected the API.
  SQL remains Microsoft Entra-only and existing database authorization was not
  changed.
- The deployment created API revision `crm-analytics-pilot-api--0000002` and
  Teams revision `crm-analytics-pilot-teams--0000001`. They did not become healthy,
  so no old revision was deactivated or deleted.
- Azure SQL network rejection is gone. The API now reaches the server and obtains
  a managed-identity token, but database login fails for the token-identified
  principal. The app has both system-assigned and user-assigned identities; the
  database user is the runtime UAMI. Microsoft.Data.SqlClient requires explicit
  user-assigned client-ID selection. The existing application DB Key Vault secret
  was not read or changed during this recovery, so this remains the blocker.
- Key Vault sync succeeded for both apps after deployment. The historical Teams
  KEDA `internal-api-key` resolution warning did not recur. Teams system identity
  retains `Key Vault Secrets User` only at the internal API key and bot-secret
  scopes; it has no SQL/DWH/OLTP secret access.
- Application SQL runtime smoke failed at database authentication. Fabric DWH,
  Fabric OLTP, and Service Bus runtime smokes were not started because the required
  healthy API runtime was not available. No diagnostic Service Bus message was
  sent; active and dead-letter counts remained zero when checked.

This firewall rule is not a production network solution. Production hardening
requires a workload-profile Container Apps Environment, a custom VNet, NAT Gateway
or private endpoint connectivity, and removal of `AllowAllWindowsAzureIps` after
the private path is proven.

Envanter tarihi: 2026-08-05

Son OLTP Query Builder adımında yalnız değişen API runtime image'ı build/push
edildi ve pilot API parametresi yeni OCI index digest'ine sabitlendi. Teams image
ve digest'i değiştirilmedi. Bu adım Container Apps deployment, Azure validation
veya what-if çalıştırmadı.

Bu belge, `C:\Users\ramaz\source\repos\crm-project` repository'si için Azure CLI
ile doğrulanan pilot envanteridir. Önceki bootstrap'a ek olarak Fabric DWH
managed-identity connection secret'ı ile API–Teams internal authentication
secret'ı hazırlandı ve sağlanan Entra API, DWH ve Power BI non-secret değerleri
pilot parametrelerine geçirildi. Resource group ve subscription değiştirilmedi;
`az login`, Azure validation/what-if, Fabric/Power BI değişikliği ve Container Apps
deployment çalıştırılmadı. Son image adımında yalnız değişen Teams runtime image'ı
build/push edildi; API image referansı değiştirilmedi. Secret değerleri okunmadı,
çıktılanmadı veya belgelenmedi.

## Durum sözlüğü

| Durum | Anlam |
|---|---|
| `Existing` | Kaynak veya güvenli metadata doğru subscription'da görüldü. |
| `Missing` | Subscription genelindeki ARM aramasında kaynak bulunmadı. |
| `PermissionDenied` | İstenen güvenli metadata için yetki yok. |
| `ConfigurationMismatch` | Azure durumu ile repository sözleşmesi veya repository belgeleri çelişiyor. |
| `ConfigurationMappingRequired` | Gerçek veri nesnesi ile versionlanmış SQL contract arasında onaylı eşleme yok; tahmin yürütülmez. |
| `ManualInputRequired` | ARM'den güvenilir biçimde alınamayan karar/kimlik bilgisi gerekiyor. |
| `ReusableAsIs` | Hedef sözleşmede kontrol düzlemi kaynağı olarak doğrudan kullanılabilir. |
| `ReusableForLegacyOnly` | Mevcut Web App akışında kullanılabilir; hedef Container Apps sözleşmesini karşılamaz. |
| `MigrationRequired` | Yeniden kullanım için Bicep/configuration değişikliği veya iş yükü geçişi gerekir. |
| `InsufficientEvidence` | Güvenli metadata kesin sonuca yetmiyor. |
| `NotApplicable` | İlgili kaynak/sözleşme bu kapsamda uygulanabilir değil. |

## Doğru tenant/subscription

`az account show` sonucu istenen kapsamla birebir eşleşmiştir.

| Alan | Doğrulanan değer | Durum |
|---|---|---|
| Environment | `AzureCloud` | `Existing` |
| Tenant ID | `2e010224-86ea-4b34-93ea-f9833137c80e` | `Existing` |
| Home tenant ID | `2e010224-86ea-4b34-93ea-f9833137c80e` | `Existing` |
| Subscription name | `Azure for Students` | `Existing` |
| Subscription ID | `634ccf7f-1073-4965-9385-9ae3bbef1533` | `Existing` |
| Subscription state | `Enabled` | `Existing` |
| Default subscription | `true` | `Existing` |
| Aktif hesap türü | `user` | `Existing` |

Önceki yanlış tenant/subscription kapsamı tamamen kaldırılmış ve bu bölüm
yalnız doğrulanan aktif kapsamla değiştirilmiştir.

## Resource provider registration durumu

Provider registration öncesinde dört hedef namespace de `NotRegistered`
durumundaydı. Yalnız aşağıdaki registration komutları çalıştırıldı; bounded
polling'in ilk üç turunda durumlar `Registering`, dördüncü turunda ise tamamı
`Registered` olarak doğrulandı.

| Provider | Önceki state | Registration işlemi | Son state | Sonuç |
|---|---|---|---|---|
| `Microsoft.App` | `NotRegistered` | `az provider register --namespace Microsoft.App` | `Registered` | `Registered` |
| `Microsoft.ContainerRegistry` | `NotRegistered` | `az provider register --namespace Microsoft.ContainerRegistry` | `Registered` | `Registered` |
| `Microsoft.Sql` | `NotRegistered` | `az provider register --namespace Microsoft.Sql` | `Registered` | `Registered` |
| `Microsoft.ServiceBus` | `NotRegistered` | `az provider register --namespace Microsoft.ServiceBus` | `Registered` | `Registered` |

Provider registration blocker'ı çözülmüştür. `PermissionDenied`, `Failed` veya
`Timeout` sonucu oluşmadı. Bu işlem provider metadata'sını kaydetmiştir; henüz
hiçbir Azure kaynağı oluşturulmamıştır. Bu bootstrap öncesinde ayrıca
`Microsoft.ContainerRegistry` ve `Microsoft.ManagedIdentity` durumları
`Registered` olarak yeniden doğrulandı; provider register çalıştırılmadı.

## `crm-project-rg` kaynakları

Resource group `Existing` durumundadır; ARM location `westeurope`,
provisioning state `Succeeded`, tags boş/null'dır.

| Kaynak | ARM türü | Region | Durum |
|---|---|---|---|
| `ASP-crmprojectrg-9e36` | `Microsoft.Web/serverFarms` | France Central | `Existing` |
| `lokman-crm-project` | `Microsoft.Web/sites` | France Central | `Existing` |
| `lokmancrmkv01` | `Microsoft.KeyVault/vaults` | Sweden Central | `Existing` |
| `crm-project-insights` | `Microsoft.Insights/components` | Sweden Central | `Existing` |
| `Application Insights Smart Detection` | `microsoft.insights/actiongroups` | Global | `Existing` |
| `oidc-msi-9bde` | `Microsoft.ManagedIdentity/userAssignedIdentities` | France Central | `Existing` |
| `id-crm-analytics-runtime` | `Microsoft.ManagedIdentity/userAssignedIdentities` | Sweden Central | `Existing` (bu adımda oluşturuldu) |
| `crmprojectacr634c` | `Microsoft.ContainerRegistry/registries` | Sweden Central | `Existing` (bu adımda oluşturuldu) |
| `crmprojectsql634c` | `Microsoft.Sql/servers` | Sweden Central | `Existing` (`Ready`; Azure SQL bootstrap adımında oluşturuldu) |
| `CrmAnalytics` | `Microsoft.Sql/servers/databases` | Sweden Central | `Existing` (`Online`; Standard S0/10 DTU; Azure SQL bootstrap adımında oluşturuldu) |
| `crmprojectsb634c` | `Microsoft.ServiceBus/namespaces` | Sweden Central | `Existing` (`Succeeded`; Service Bus bootstrap adımında oluşturuldu) |
| `crm-report-processing` | `Microsoft.ServiceBus/namespaces/queues` | Sweden Central | `Existing` (`Active`; Service Bus bootstrap adımında oluşturuldu) |
| `596bb506-49a0-4d64-bbd2-ef4a3b613042` | `microsoft.insights/workbooks` | Sweden Central | `Existing` |
| `crm-project-alerts` | `microsoft.insights/actiongroups` | Global | `Existing` |
| `CRM Failed Requests Alert` | `microsoft.insights/metricalerts` | Global | `Existing` |

### App Service

| Alan | Doğrulanan değer | Durum |
|---|---|---|
| Ad | `lokman-crm-project` | `Existing` |
| State | `Running` | `Existing` |
| Runtime stack | `DOTNETCORE\|8.0` | `Existing` |
| İşletim sistemi | Linux (`app,linux`) | `Existing` |
| Deployment slot | Yok; yalnız production app | `Existing` |
| HTTPS only | Açık | `Existing` |
| Public network access | `Enabled` | `Existing` |
| Managed identity | Yok (`identity: null`) | `Missing` |
| App Service Plan | `ASP-crmprojectrg-9e36` | `Existing` |
| Health check path | Tanımlı değil | `Missing` |
| Deployment source türü | GitHub source-control integration; manual integration değil | `Existing` |
| App setting adları | Yok | `Existing` |
| Connection string adları | Yok | `Existing` |
| Key Vault reference | Yok | `Missing` |
| Çalışan uygulama güncel backend mi? | `Unknown`; repository/config adlarından kesin kanıtlanamadı | `InsufficientEvidence` |

Deployment source repository URL'si ve ayar değerleri belgeye alınmamıştır.
Kök workflow `lokman-crm-project` Web App'ine deploy edecek şekilde tanımlıdır;
bu, Azure'da çalışan binary/revision'ın repository'nin güncel backend'i olduğunu
tek başına kanıtlamaz.

### App Service Plan

| Alan | Değer | Durum |
|---|---|---|
| Ad | `ASP-crmprojectrg-9e36` | `Existing` |
| SKU/tier | `F1` / `Free` | `Existing` |
| Region | France Central | `Existing` |
| OS | Linux | `Existing` |
| Instance capacity | 1 | `Existing` |

### Application Insights, Log Analytics, workbook ve alert'ler

| Kaynak/alan | Değer | Durum |
|---|---|---|
| Application Insights | `crm-project-insights`, Sweden Central, `web`, `Succeeded` | `Existing` |
| Workspace-based | Evet; `WorkspaceResourceId` mevcut | `Existing` |
| Bağlı workspace | `managed-crm-project-insights-ws` | `Existing` |
| Workspace resource group | `ai_crm-project-insights_dc8b3de1-135d-462c-842f-3b1847ebce79_managed` | `Existing` |
| Workspace region/SKU | Sweden Central / `PerGB2018` | `Existing` |
| Workspace retention | 30 gün | `Existing` |
| Insights retention metadata | 90 gün | `Existing` |
| Ingestion/query public access | Her ikisi de `Enabled` | `Existing` |
| Workbook | ARM adı `596bb506-49a0-4d64-bbd2-ef4a3b613042`, display name `CRM Monitoring Dashboard` | `Existing` |
| Workbook source | `crm-project-insights` Application Insights | `Existing` |
| Metric alert | `CRM Failed Requests Alert`; `crm-project-insights` üzerinde `requests/failed`, Count > 0, 5 dakikalık pencere, 1 dakikalık değerlendirme, severity 3 | `Existing` |
| Alert action group | `crm-project-alerts` | `Existing` |
| Diğer action group | `Application Insights Smart Detection` | `Existing` |
| Scheduled query rule | Bulunmadı | `NotApplicable` |

Workbook serialized query içeriği ve action group receiver/credential ayrıntıları
okunmadı. Instrumentation key, Application Insights connection string ve Log
Analytics shared key okunmadı. Mevcut workspace Azure tarafından yönetilen ayrı
resource group'tadır. Güncellenen hedef Bicep, workspace tam resource ID'sini
veya ad/resource group çiftini kabul eder; pilot parametre örneği bu workspace'i
`ReusableAsIs` olarak seçer ve `${namePrefix}-logs` oluşturmaz. Shared key yalnız
deployment runtime `listKeys` ifadesinde çözülür; parametre veya output değildir.

## Key Vault ve secret-name durumu

### Vault yapılandırması

| Alan | Değer | Durum |
|---|---|---|
| Ad | `lokmancrmkv01` | `Existing` |
| Resource group | `crm-project-rg` | `Existing` |
| Region | Sweden Central | `Existing` |
| Permission model | Azure RBAC açık | `Existing` |
| Public network access | `Enabled` | `Existing` |
| Firewall/default action | `networkAcls: null`; özel kural yok, public erişim etkin | `Existing` |
| Private endpoint | Yok | `Missing` |
| Soft delete | Açık, 90 gün | `Existing` |
| Purge protection | Kapalı/null | `Missing` |
| Access policies | Boş; RBAC modeli kullanılıyor | `NotApplicable` |

Vault kapsamındaki salt-okunur RBAC kontrolünde workload/service-principal için
Key Vault data rolü görülmedi. App Service'in managed identity'si yoktur ve
`oidc-msi-9bde` kimliğinin mevcut tek rolü legacy Web App üzerindeki `Website
Contributor` rolüdür. Bicep'in API/Teams system-assigned identity'lerine secret
düzeyinde vereceği roller henüz mevcut değildir; Container App'ler de mevcut
olmadığından bu beklenen durum `Missing` olarak kaydedilmiştir.

Secret listeleme yetkisi vardı. Yalnız secret adları okundu; değerler ve
version'lar okunmadı.

| Secret adı | Azure durumu | Repository beklentisi |
|---|---|---|
| `ApplicationInsightsConnectionString` | `Existing` | Legacy/adı eşlenmemiş; hedef beşli içinde değil |
| `CrmConnectionString` | `Existing` | Legacy/adı eşlenmemiş; hedef beşli içinde değil |
| `crm-analytics-application-db` | `Existing`; enabled | API application DB secret'ı; değer/version belgelenmedi |
| `crm-analytics-query-dwh` | `Existing`; enabled; content type `application/vnd.microsoft.data.connection-string` | API DWH secret'ı; değer/version belgelenmedi |
| `crm-analytics-query-oltp` | `Existing`; enabled | API OLTP secret'ı; değer/version belgelenmedi |
| `crm-analytics-internal-api-key` | `Existing`; enabled | API ve Teams ortak callback key'i; değer/version belgelenmedi |
| `crm-analytics-teams-client-secret` | `Existing`; enabled; content type `application/vnd.microsoft.botframework.client-secret` | Teams/Bot application credential; değer/version belgelenmedi |

Vault'un RBAC modeli Bicep önkoşuluyla uyumludur ve vault kontrol düzlemi
`ReusableAsIs` sınıfındadır. Application DB, DWH, OLTP ve ortak internal API key
secret'ları ve Teams client secret hazırdır. Pilot Teams client secret referans
aktivasyonu açıktır. Container App system identity henüz oluşmadığından secret-scope
erişimi deployment aşamasında verilecektir. Public access/private endpoint
ve purge protection tercihleri için güvenlik onayı ayrıca gerekir; bu envanter
otomatik mimari kararı vermez.

## `fabric-rg` kaynakları

Resource group `Existing` durumundadır; ARM location `swedencentral`,
provisioning state `Succeeded`, tags boş/null'dır.

| Kaynak | ARM türü | Region | Durum |
|---|---|---|---|
| `lokmanfabric` | `Microsoft.Fabric/capacities` | Sweden Central | `Existing` |

### Fabric Capacity durumu

| Alan | Değer | Durum |
|---|---|---|
| Ad | `lokmanfabric` | `Existing` |
| SKU | `F2` | `Existing` |
| Region | Sweden Central | `Existing` |
| State | `Active` | `Existing` |
| Provisioning state | `Succeeded` | `Existing` |
| Capacity ARM ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/fabric-rg/providers/Microsoft.Fabric/capacities/lokmanfabric` | `Existing` |
| Service-specific `properties.capacityId` | ARM cevabında null/yok | `ManualInputRequired` |
| Administrator kimlik türü | `UserUPN` (1 üye; kimlik değeri kaydedilmedi) | `Existing` |

Capacity, pilot için kapasite katmanında `ReusableAsIs` adayıdır; workspace/item
erişimi ve iş yükü sözleşmesi doğrulanmadan uygulama açısından hazır sayılmaz.
Fabric Workspace, Lakehouse, SQL endpoint, pipeline/notebook ve Power BI
artefact'ları ARM capacity kaynağının alt kaynakları olarak görünmeyebilir.
`fabric-rg` ARM listesinde yalnız capacity bulundu. Workspace, Lakehouse, SQL
endpoint, Fabric item/job ve Power BI bilgileri `ManualInputRequired` durumundadır.

## Subscription genelindeki diğer ilgili kaynaklar

Arama yalnız `crm-project-rg` ile sınırlı tutulmadı; doğru subscription'daki tüm
resource group'lar ARM türüne göre tarandı.

| Kaynak türü | Bulunan kaynak | Durum |
|---|---|---|
| Azure Container Registry | `crm-project-rg/crmprojectacr634c`, Sweden Central | `Existing` |
| Container Apps Environment | Yok | `Missing` |
| Container Apps | Yok | `Missing` |
| User-assigned managed identity | `crm-project-rg/oidc-msi-9bde`, France Central; `crm-project-rg/id-crm-analytics-runtime`, Sweden Central | `Existing` |
| Azure SQL Server | `crm-project-rg/crmprojectsql634c`, Sweden Central | `Existing` |
| Azure SQL Database | `crmprojectsql634c/CrmAnalytics`, Standard S0/10 DTU | `Existing` |
| Service Bus namespace | `crm-project-rg/crmprojectsb634c`, Sweden Central | `Existing` |
| Service Bus queue | `crmprojectsb634c/crm-report-processing` | `Existing` |
| Azure Bot | Yok | `Missing` |

### Managed identity durumu

Runtime için yeni `id-crm-analytics-runtime` UAMI'si `crm-project-rg` içinde
Sweden Central'da oluşturuldu:

| Alan | Değer |
|---|---|
| Resource ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourcegroups/crm-project-rg/providers/Microsoft.ManagedIdentity/userAssignedIdentities/id-crm-analytics-runtime` |
| Client ID | `da156325-7c70-46fc-b899-024f9d73b994` |
| Principal ID | `ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` |
| Location | `swedencentral` |
| Tags | `environment=pilot`, `project=crm-analytics`, `purpose=container-app-runtime` |

Identity hiçbir Container App'e veya başka resource'a bağlanmadı. Önceki
`AcrPull` denemeleri `AuthorizationFailed`/`PermissionDenied` olmuştu; bu SQL
bootstrap adımındaki read-only doğrulamada exact ACR scope'unda doğrudan `AcrPull`
atamasının artık mevcut olduğu görüldü. `AcrPush` verilmedi. Service Bus
queue-scope Data Sender ve Data Receiver rolleri de daha önce tamamlandı.

Read-only doğrulamayla tamamlandığı görülen exact atama:

| Alan | Değer |
|---|---|
| Scope | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ContainerRegistry/registries/crmprojectacr634c` |
| Assignee object ID | `ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` |
| Assignee principal type | `ServicePrincipal` |
| Role | `AcrPull` |
| Built-in role definition ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/providers/Microsoft.Authorization/roleDefinitions/7f951dda-4ed3-4680-a7ca-43fe172d538d` |
| Durum | `Existing`; role assignment ID `dd2cb918-08c1-4f87-822b-8fb66c02006b` |

`oidc-msi-9bde` doğru tenant'ta mevcut bir UAMI'dir. Mevcut rol kanıtı yalnız
`lokman-crm-project` üzerinde `Website Contributor` olduğundan legacy GitHub/Web
App deployment kimliğiyle uyumludur. ACR pull, SQL, Service Bus, Fabric veya
Power BI erişimi görülmedi. Hedef Bicep'in `runtimeIdentityResourceId` ve
`runtimeIdentityClientId` girdisi olarak kullanılmasına ilişkin sahiplik ve
least-privilege onayı yoktur; runtime UAMI amacı için `InsufficientEvidence`
olarak değerlendirilir.

Hedef modelde her iki Container App aynı runtime UAMI'yi ACR image pull ve
SQL/Service Bus/Fabric/Power BI erişimi için bağlar. API ve Teams ayrıca ayrı
system-assigned identity alır; Key Vault reference'larında `identity: 'system'`
kullanılır. Container App kaynakları henüz olmadığından bu system identity'ler
`Missing` durumundadır. Mevcut App Service'te de managed identity yoktur.

## Existing/Missing kaynak matrisi

| Hedef bileşen | Azure kanıtı | Durum | Yeniden kullanım değerlendirmesi |
|---|---|---|---|
| Resource group | `crm-project-rg` | `Existing` | `ReusableAsIs` |
| Legacy Web App | `lokman-crm-project` | `Existing` | `ReusableForLegacyOnly` |
| Legacy App Service Plan | `ASP-crmprojectrg-9e36` | `Existing` | `ReusableForLegacyOnly` |
| Key Vault | `lokmancrmkv01` | `Existing` | `ReusableAsIs`; beklenen pilot secret adları hazır |
| Application Insights | `crm-project-insights` | `Existing` | Bicep existing ID veya ad/RG kabul ediyor; hedef uygulama SDK'sı olmadığı için application telemetry ayrıca `ManualInputRequired` |
| Log Analytics | Azure-managed `managed-crm-project-insights-ws` | `Existing` | Bicep existing ID veya ad/RG ile `ReusableAsIs` |
| Workbook/alerts | `CRM Monitoring Dashboard`, iki action group, bir metric alert | `Existing` | Telemetry hedefi değişirse `MigrationRequired` |
| Fabric capacity | `lokmanfabric` F2 | `Existing` | Capacity olarak `ReusableAsIs` |
| ACR | `crmprojectacr634c.azurecr.io` | `Existing` | Basic, Sweden Central, admin disabled, public network enabled, RBAC mode |
| Container Apps Environment | Bulunmadı | `Missing` | Bicep yeni environment oluşturuyor |
| API Container App | Bulunmadı | `Missing` | Bicep yeni app oluşturuyor |
| Teams Container App | Bulunmadı | `Missing` | Bicep yeni app oluşturuyor |
| Runtime UAMI | `id-crm-analytics-runtime` | `Existing` | Uygulama-runtime amaçlı; ACR `AcrPull` ve Service Bus queue-scope Sender/Receiver atamaları tamamlandı |
| API/Teams system identities | Container App'ler yok | `Missing` | App'lerle oluşturulması bekleniyor |
| Azure SQL Server/DB | `crmprojectsql634c` / `CrmAnalytics` | `Existing` | Entra-only, Standard S0/10 DTU; Bicep oluşturmaz |
| Service Bus namespace/queue | `crmprojectsb634c` / `crm-report-processing` | `Existing` | Standard, Sweden Central; Bicep FQDN ve queue adını girdi olarak kullanır |
| Azure Bot | Bulunmadı | `Missing` | Bicep oluşturmaz |
| Fabric Workspace/Lakehouse/SQL endpoint/item | ARM'den bulunamadı | `ManualInputRequired` | Fabric API/portal doğrulaması gerekir |
| Power BI workspace/report/model | ARM'den bulunamadı | `ManualInputRequired` | Fabric/Power BI doğrulaması gerekir |
| Entra API ve Teams/Bot uygulamaları | API existing; Teams/Bot `crm-analytics-teams-bot` oluşturuldu | `Existing` | Ayrı client ID'ler tenant düzleminde doğrulandı |
| Teams OAuth connection | `crm-analytics-teams-oauth` yalnız planlanan ad | `Missing` | Azure Bot oluşturulduktan sonra yapılandırılmalı |

## App Service ile Container Apps karşılaştırması

Mevcut App Service için sınıf: **`ReusableForLegacyOnly`**.

Gerekçe:

- `lokman-crm-project`, tek Linux Web App olarak .NET 8 çalıştırıyor; hedef ise
  API ve Teams için iki ayrı Container App bekliyor.
- Hedef API ingress'i internal, Teams ingress'i external; her ikisi port 8080,
  multiple revision, min 1/max 3 replica ve `/health/live` ile `/health/ready`
  probe'ları kullanıyor. Web App'te health check path yok ve slot yok.
- Bicep mevcut Web App veya App Service Plan'a `existing` referans vermiyor;
  existing Log Analytics'i yeniden kullanıp yeni Container Apps Environment
  oluşturuyor.
- Hedef image'lar ACR'dan UAMI ile çekiliyor. Subscription'da ACR yok ve mevcut
  Web App'te managed identity yok.
- Hedef Key Vault modeli API/Teams system identity'lerini kullanıyor. Web App'te
  Key Vault reference ve uygulama ayarı yok.
- Kök GitHub workflow'u Web App deployment'ıdır; backend workflow'u ayrı
  Container Apps/Bicep akışıdır.

Bu sınıflandırma Web App'in silinmesi veya taşınması kararı değildir. Legacy
akışı korumak ya da hedef Container Apps'e geçmek için mimari karar
`ManualInputRequired` durumundadır. Container Apps hedefi seçilirse iş yükü
geçişi ayrıca planlanmalıdır.

## Azure SQL durumu

`crmprojectsql634c` global ad kontrolünde kullanılabilir bulundu ve bu bootstrap
adımında Sweden Central'da oluşturuldu. SQL admin kullanıcı adı/parolası
sağlanmadı; Microsoft Entra-only authentication etkinleştirildi ve Azure CLI'da
giriş yapan `ramazanb` kullanıcısı (object ID
`eac148db-46b9-4895-9a5e-834ef358384b`) Microsoft Entra administrator yapıldı.

| Server alanı | Doğrulanan değer | Durum |
|---|---|---|
| Name | `crmprojectsql634c` | `Existing` |
| FQDN | `crmprojectsql634c.database.windows.net` | `Existing` |
| Region | `swedencentral` | `Existing` |
| State | `Ready` | `Existing` |
| Authentication | Microsoft Entra-only (`azureAdOnlyAuthentication: true`) | `Existing` |
| Microsoft Entra administrator | `ramazanb`; object ID `eac148db-46b9-4895-9a5e-834ef358384b`; principal type `User` | `Existing` |
| Public network access | `Enabled` | `Existing` |
| Minimum TLS | `1.2` | `Existing` |
| Server firewall rules | Boş liste; `Allow Azure services` / `0.0.0.0` kuralı oluşturulmadı | `Existing` |

| Database alanı | Doğrulanan değer | Durum |
|---|---|---|
| Name | `CrmAnalytics` | `Existing` |
| Region | `swedencentral` | `Existing` |
| Status | `Online` | `Existing` |
| Edition / service objective | `Standard` / current ve requested `S0` | `Existing` |
| DTU | `10` | `Existing` |
| Creation date | `2026-08-04T10:54:26.493000+00:00` | `Existing` |
| Backup storage redundancy | Current/requested `Geo`; short-term retention 7 gün, differential interval 24 saat; LTR kapalı | `Existing` |
| Maximum size | `268435456000` bytes (250 GiB) | `Existing` |

`ConnectionStrings:CrmAnalytics` bu application database'e ayrılmıştır. DBA
kontrollü artifact'ın ilk altı EF migration'ı uygulandı ve history kayıtları
doğrulandı. Mevcut `deploy/sql/CrmAnalytics.Migrations.sql` ayrıca uygulama kanıtı
olmayan ve ayrı DBA/operator onayı gerektiren
`20260809235416_AddSubmittedSemanticPlan` migration'ını içerir. `crm` şemasında on
uygulama tablosu oluştu; seed/test/müşteri verisi eklenmedi.

Runtime UAMI `id-crm-analytics-runtime`, object ID
`ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` ile `EXTERNAL_USER` contained database
user olarak oluşturuldu. Yalnız `crm` şeması üzerinde `SELECT`, `INSERT`, `UPDATE`
ve `DELETE` verildi; `EXECUTE`, DDL veya yönetici database rolü verilmedi.
`crm-analytics-application-db` Key Vault secret'ı oluşturuldu ve enabled metadata'sı
doğrulandı; secret değeri/version bilgisi repository'ye veya bu belgeye alınmadı.
Tek IPv4'e açılan geçici bootstrap firewall kuralı işlem sonunda kaldırıldı;
server firewall listesi boştur ve `Allow Azure services` kuralı yoktur.

`ConnectionStrings:QueryDwh` aşağıdaki Fabric DWH SQL endpoint'ine managed
identity ile bağlanmak üzere hazırlanmıştır.
`ConnectionStrings:QueryOltp` pilot parametresinde enabled durumundadır ve
`crm-analytics-query-oltp` Key Vault secret reference'ına bağlanır.

## Fabric DWH pilot durumu

| Alan | Doğrulanan/sağlanan değer |
|---|---|
| Server | `eqbaclxkqy2exe7k7gbtcn6iby-5es6c723oane3lbx5mhcfaa6q4.datawarehouse.fabric.microsoft.com` |
| Port | `1433` |
| Initial Catalog | `wh_crm_analytics` |
| Schemas | `mart`, `dwh` |
| Runtime UAMI Client ID | `da156325-7c70-46fc-b899-024f9d73b994` |
| Runtime UAMI read permission | Verildi |
| QueryDwh | enabled (`queryDwhEnabled=true`) |
| Key Vault secret adı / durumu | `crm-analytics-query-dwh` / enabled |
| Authentication | Active Directory Managed Identity; parola/token/client secret yok |

DWH rapor sorgulama yüzeyi yalnız aşağıdaki read-only `mart` view'larıdır:

- `mart.vw_sales_detail`
- `mart.vw_monthly_sales`
- `mart.vw_sales_by_region`
- `mart.vw_sales_by_category`
- `mart.vw_customer_rfm_segmented`
- `mart.vw_payment`
- `mart.vw_payment_summary`

`dwh.dim_customer`, `dwh.dim_product`, `dwh.dim_date` ve `dwh.fact_sales` base
tabloları allowlist'e eklenmemiştir. Mevcut SELECT-only guardrail değişmemiştir.

`ConfigurationMappingRequired` blocker'ı repository'deki merkezi SQL allowlist
sözleşmesine eklenen `fabric-dwh-object-mapping-v1` mapping'i ile çözülmüştür:

| Logical contract adı | Sabit production nesnesi |
|---|---|
| `vw_sales` | `mart.vw_sales` |
| `vw_customer_rfm` | `mart.vw_customer_rfm` |
| `vw_payment` | `mart.vw_payment` |

Logical lookup case-insensitive olabilir; ancak Query Builder ve DWH execution
yalnız sözleşmedeki sabit, schema-qualified fiziksel adı kullanır. Unqualified
production adı ve kullanıcı tarafından sağlanan schema/object adı kabul edilmez.
Mevcut report/use-case veya metric contract'ına açıkça bağlanan özel execution
view'ları `mart.vw_sales_detail`, `mart.vw_monthly_sales`,
`mart.vw_sales_by_region`, `mart.vw_sales_by_category`,
`mart.vw_customer_rfm_segmented` ve `mart.vw_payment_summary` ile sınırlıdır.
`mart.vw_payment` temel mapping'in fiziksel hedefidir. Base tablolar genel
NL2SQL/Query Builder veya execution allowlist'ine eklenmemiş, SELECT-only politika
korunmuş ve Fabric üzerinde view/table/schema/permission değişikliği yapılmamıştır.

## Power BI pilot kimlikleri

| Alan | Değer |
|---|---|
| Workspace ID | `7fe125e9-705b-4d1a-ac37-eb0e22801e87` |
| Report ID | `36b69ded-e421-412b-b187-70caa70d4292` |
| Semantic Model ID | `bdc46a8f-03cb-4bd8-a9c2-2dde70dd5432` |

Report page identifier'ları `84686876a120a0b04881`,
`033e576a7c43418160b7` ve `637aeb7338de115d4c9c` değerleridir. Bunlar Report
ID değildir ve Report ID ile birleştirilmemiştir. Uygulamada ayrı page mapping
configuration alanı bulunmadığından kod/config alanı eklenmemiş, yalnız burada
deployment metadata'sı olarak kaydedilmiştir.

## Fabric SQL Database OLTP pilot durumu

### Query Builder contract v1 update — 2026-08-05

The versioned OLTP SQL-production contract is
`fabric-oltp-operational-orders-v1`. Current-user Entra metadata discovery found
`dbo.vw_operational_orders` with 19 columns and proved order-level grain through
view-definition lineage. Customer identity columns are excluded, no business
metric is introduced, and the only Query Builder/execution object remains the
schema-qualified view. Limits are 15 seconds and 1000 rows (default 100).

Source is selected before SQL generation and is persisted in canonical/revision
state. DWH `mart.*` and OLTP surfaces are mutually isolated; there is no fallback.
Current-user Fabric no-row compile succeeded for detail, parameterized date and
city filters, and deterministic Top N; describe-first-result-set also succeeded.
This was not a runtime UAMI test. Immutable API image evidence is recorded after
the image verification step. Runtime UAMI database smoke remains a post-deployment
activity. No Container Apps deployment is part of this update.

| Alan | Doğrulanan değer |
|---|---|
| Server | `eqbaclxkqy2exe7k7gbtcn6iby-5es6c723oane3lbx5mhcfaa6q4.database.fabric.microsoft.com:1433` |
| Initial Catalog | `crm_oltp-3fc7fdf2-6c02-4c2f-8811-298d6b38c299` |
| Schema | `dbo` |
| Runtime UAMI Client ID | `da156325-7c70-46fc-b899-024f9d73b994` |
| Key Vault secret adı / durumu | `crm-analytics-query-oltp` / enabled |
| QueryOltp | enabled (`queryOltpEnabled=true`) |
| İzinli read view | `dbo.vw_operational_orders` |

Runtime UAMI'nin mevcut database permission envanteri değiştirilmedi: `SELECT`,
`INSERT` ve `UPDATE`, `dbo.customers`, `dbo.products`, `dbo.orders`,
`dbo.order_items` ve `dbo.order_payments` üzerinde; `SELECT` ise
`dbo.vw_operational_orders` üzerinde mevcuttur. `DELETE` verilmemiştir. Bu write
izinleri report query allowlist'ine taşınmamıştır. Current report execution yolu
OLTP için de SELECT-only kalır ve yalnız belirtilen view'ı sorgulama yüzeyi kabul
eder. Runtime managed-identity connection smoke testi Container Apps deployment
sonrasında yapılacaktır.

## Service Bus durumu

`crmprojectsb634c` adı global ad kontrolünde kullanılabilir bulundu ve namespace
bu bootstrap adımında oluşturuldu. Bicep namespace veya queue oluşturmaz;
aşağıdaki non-secret FQDN ve queue adını tüketir.

| Namespace alanı | Doğrulanan değer | Durum |
|---|---|---|
| Name | `crmprojectsb634c` | `Existing` |
| FQDN | `crmprojectsb634c.servicebus.windows.net` | `Existing` |
| Resource ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ServiceBus/namespaces/crmprojectsb634c` | `Existing` |
| Provisioning state | `Succeeded` | `Existing` |
| SKU | `Standard` | `Existing` |
| Region | `swedencentral` | `Existing` |
| Public network access | `Enabled` | `Existing` |
| Minimum TLS | `1.2` | `Existing` |
| Local authentication | `Enabled` (`disableLocalAuth=false`) | Geçici pilot durumu; managed identity smoke sonrasında kapatılacak |
| Tags | `environment=pilot`, `project=crm-analytics`, `managed-by=azure-cli` | `Existing` |

Repository sözleşmesi Peek-Lock/manual settlement, duplicate detection,
expiration dead-lettering ve sessions kapalı davranışını açıkça tanımlar. Lock
duration, max delivery count, default TTL, duplicate history window ve max size
için yeni uygulama davranışı icat edilmedi; Azure Standard queue varsayılanları
kullanıldı.

| Queue alanı | Doğrulanan değer | Kaynak |
|---|---|---|
| Name / status | `crm-report-processing` / `Active` | Repository / Azure |
| Resource ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ServiceBus/namespaces/crmprojectsb634c/queues/crm-report-processing` | Azure |
| Lock duration | `PT1M` | Azure Standard varsayılanı |
| Max delivery count | `10` | Azure Standard varsayılanı |
| Dead-letter on expiration | `true` | Repository sözleşmesi |
| Default TTL | `P10675199DT2H48M5.4775807S` | Azure varsayılanı |
| Duplicate detection | `true` | Repository sözleşmesi |
| Duplicate history window | `PT10M` | Azure varsayılanı |
| Session required | `false` | Repository sözleşmesi |
| Max size | `1024 MB` | Azure Standard varsayılanı |
| Partitioning | `false` | Azure varsayılanı |

Deterministik Service Bus `MessageId` ve uygulama seviyesindeki idempotency
mekanizması değiştirilmedi. Uygulamanın built-in DLQ davranışı korunur; ayrı DLQ
queue oluşturulmadı.

Runtime UAMI principal ID `ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` için iki rol de
yalnız queue resource scope'unda başarıyla oluşturuldu ve tekrar listelenerek
doğrulandı:

| Rol | Role definition ID | Scope | Durum |
|---|---|---|---|
| Azure Service Bus Data Sender | `69a216fc-b8fb-44d8-bc22-1f3c2cd27a39` | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ServiceBus/namespaces/crmprojectsb634c/queues/crm-report-processing` | `Existing` |
| Azure Service Bus Data Receiver | `4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0` | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ServiceBus/namespaces/crmprojectsb634c/queues/crm-report-processing` | `Existing` |

Service Bus için yetkili operatörün tamamlaması gereken rol ataması kalmamıştır;
namespace scope'una genişletme yapılmamıştır. Managed identity smoke testi bu
adımda bilerek çalıştırılmadı. Smoke başarıyla tamamlandıktan sonra local
authentication kapatılmalıdır.

## ACR durumu

`crmprojectacr634c` adı global ad kontrolünde kullanılabilir bulundu ve registry
önceki bootstrap adımında oluşturuldu. Son OLTP Query Builder adımında yalnız API
runtime image'ı benzersiz manuel tag ile build/push edildi. Teams image build/push
edilmedi ve mevcut digest'i korundu. Deployment referansları OCI index
digest'lerine sabitlendi.

| Alan | Değer | Durum |
|---|---|---|
| Resource ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ContainerRegistry/registries/crmprojectacr634c` | `Existing` |
| Login server | `crmprojectacr634c.azurecr.io` | `Existing` |
| Provisioning state | `Succeeded` | `Existing` |
| SKU | `Basic` | `Existing` |
| Location | `swedencentral` | `Existing` |
| Admin user | `Disabled` | `Existing` |
| Public network access | `Enabled` | `Existing` |
| Role assignment mode | CLI `rbac`; ARM `LegacyRegistryPermissions` | `Existing` |
| Resource identity | `null` | `NotApplicable` |
| Tags | `environment=pilot`, `project=crm-analytics`, `managed-by=azure-cli` | `Existing` |

Credential, admin password veya access token okunmadı. ACR admin user açılmadı.
Yalnız API image'ı `a94fc43c9c7c-oltp-qb-manual-20260805084130` tag'iyle
build/push edildi; Teams image ve digest'i değiştirilmedi. Önceki tag/manifestler
değiştirilmedi veya silinmedi ve `latest` tag oluşturulmadı. Azure Bot, OAuth
connection, Teams channel ve messaging endpoint henüz oluşturulmadı; Container
Apps henüz deploy edilmedi.

### Doğrulanmış immutable image envanteri

| İş yükü | Repository | Tag | OCI index digest | `linux/amd64` platform manifest digest | Tag durumu |
|---|---|---|---|---|---|
| API | `crm-analytics-api` | `b83b9712c652-audit-metadata-manual-20260805102107` | `sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a` | `sha256:cf97b6a0d383b25742d113a06164fcf2f5cbb086a5c2057c17b22234e0214103` | `linux/amd64`, 139733722 byte; `writeEnabled=false`, `readEnabled=true`, `listEnabled=true`, `deleteEnabled=true` (korundu) |
| Teams | `crm-analytics-teams` | `45cbd936556b-teams-bot-manual-20260804215550` | `sha256:91bd6b83283f0c66686e0219256e3a4a881b0ff74cdab86002529e9c4036650f` | `sha256:239e4caf3fc8b1460661439de55879c1a355c4615c19bd5120f5d04e6a411eee` | `writeEnabled=false`, `readEnabled=true`, `listEnabled=true`, `deleteEnabled=true` (korundu) |

Her iki OCI index digest'iyle yapılan salt-okunur pull/inspect başarılıdır.
Platform manifestleri `architecture=amd64`, `os=linux` olarak doğrulanmıştır. API
platform image boyutu `139726586` byte (compressed layer+config toplamı), registry
update zamanı `2026-08-05T08:43:04.9240382Z` değeridir. Teams platform image boyutu
`100182518` byte
(compressed layer+config toplamı), registry update zamanı
`2026-08-04T21:58:08.1824456Z` olarak doğrulanmıştır. Yeni Teams tag'i
write-disabled'dır; `deleteEnabled` değiştirilmemiştir. Pilot Container Apps
deployment'ı tag veya `latest` yerine aşağıdaki tam digest referanslarını kullanacaktır:

- `crmprojectacr634c.azurecr.io/crm-analytics-api@sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`
- `crmprojectacr634c.azurecr.io/crm-analytics-teams@sha256:91bd6b83283f0c66686e0219256e3a4a881b0ff74cdab86002529e9c4036650f`

Gelecek CI/CD build kimliği `<12-char-git-sha>-<pipeline-run-id>-<attempt>`
formatında benzersiz olacaktır. Bir tag yalnız bir kez push edilir; push sonrasında
registry digest'i çözülür ve Bicep/Container Apps deployment'ına tam digest
referansı verilir. Başarılı image doğrulamasından sonra tag write-disabled yapılır.
Tek başına Git SHA'nın build kimliği olarak yeniden kullanılmasına izin verilmez
ve `latest` kullanılmaz.

API image `a94fc43c9c7c9078a2738d33a937700b88a8e24e` taban commit'inden
ve bu commit'e henüz dahil olmayan doğrulanmış çalışma ağacı değişikliklerinden
üretildi. Teams image değiştirilmedi. Exact OCI index digest pull/inspect ve local
Development/mock health kontrolleri başarılıdır. Image environment, layer history,
uygulama root filesystem'i ve iki appsettings dosyasında secret-pattern bulgusu
yoktur. Sistem CA sertifika deposu yalnız public certificate baseline'ını içerir.

## Azure Bot durumu

Doğru subscription genelinde `Microsoft.BotService/botServices` bulunmadı:
`Missing`. Azure Bot kaynağı, Teams channel ve OAuth connection bu hazırlıkta
oluşturulmadı. Planlanan Azure Bot resource name placeholder'ı
`crm-analytics-teams-bot`, planlanan sabit OAuth connection adı
`crm-analytics-teams-oauth` değeridir. OAuth connection adı yalnız gelecekteki
Azure Bot yapılandırması için kaydedilmiştir; Azure üzerinde henüz mevcut değildir.
Messaging endpoint ve callback/public Teams Container App URL'si Container Apps
deployment'ından sonra belirlenecektir ve pilot parametresinde placeholder kalır.

### Teams/Bot Entra identity

| Alan | Değer | Durum |
|---|---|---|
| App Registration display name | `crm-analytics-teams-bot` | `Created` |
| Bot Application Client ID | `3e277fe2-0da0-4149-80da-2168ac44e7b9` | `Existing` |
| Application Object ID | `eeed61b7-4c9d-444f-ac82-efb3d76de126` | `Existing` |
| Service principal Object ID | `a8e37340-3f61-4e32-af40-f7eeea1478b7` | `Existing`; account enabled |
| Tenant ID | `2e010224-86ea-4b34-93ea-f9833137c80e` | Doğrulandı |
| signInAudience | `AzureADMyOrg` | Single-tenant |
| Microsoft App Type | `SingleTenant` | Pilot parametresine geçirildi |
| Credential display name | `crm-analytics-pilot-bot-secret` | Tek credential oluşturuldu |
| Credential oluşturma | `2026-08-04T21:38:47.3659122Z` | Non-secret metadata |
| Credential expiration | `2027-01-31T21:38:47.3659122Z` | 180 gün |
| Key Vault secret | `crm-analytics-teams-client-secret`; enabled | Değer/version belgelenmedi |
| OAuth connection name | `crm-analytics-teams-oauth` | Planlandı; oluşturulmadı |

Bot Client ID, mevcut API Client ID
`809393e5-ff46-4422-a4b3-084ed1b91d98` değerinden ayrıdır. API audience
`api://809393e5-ff46-4422-a4b3-084ed1b91d98` değiştirilmemiştir.

## Repository/Bicep uyumluluk analizi

| Kaynak | Bulgular | Durum |
|---|---|---|
| `infra/azure/bicep/main.bicep` | Existing Log Analytics/Application Insights kabul eder; API/Teams tam digest image string'lerini yeniden birleştirmeden Container App modülüne taşır; environment, API ve Teams Container App oluşturur; ACR/KV/UAMI/SQL/Service Bus/Fabric/Power BI'ı girdi olarak bekler | Güncellenmiş repository sözleşmesi |
| `infra/azure/bicep/main.bicepparam.example` | Değerler placeholder; `crm-analytics-demo` örnek prefix, gerçek pilot adı değil | `ManualInputRequired` |
| `infra/azure/bicep/main.bicepparam.pilot.example` | Gerçek name prefix, ayrı Entra API ve Bot client ID'leri, SingleTenant app type, planlanan OAuth connection adı, DWH, Power BI ve existing Azure non-secret değerlerini içerir; yalnız Container App URL/messaging alanı placeholder'dır | `Prepared` |
| `docs/deployment/KEY_VAULT_CONFIGURATION.md` | API/Teams system identity ile Key Vault, runtime UAMI ile servis erişimleri tanımlar; Bicep ile uyumlu | `Existing` |
| `docs/deployment/DEPLOYMENT_RUNBOOK.md` | Key Vault rolünü API/Teams system identity'lerine, servis erişimlerini runtime UAMI'ye ayırır | `Resolved` |
| `docs/archive/testing/FINAL_ACCEPTANCE_2026-08-01.md` | 2026-08-01 tarihli kabul kaydı gerçek identifier'ın repository'ye verilmediğini ve Fabric contract blocker'ını belirtir; bu envanter dış Azure kanıtını ekler, contract blocker'ını kaldırmaz | `Existing` tarihsel kayıt |
| `.github/workflows/devops_lokman-crm-project.yml` | `lokman-crm-project` için legacy Web App build/deploy akışı | `ReusableForLegacyOnly` |
| `.github/workflows/ci.yml` | CI/test; hedef Azure kaynaklarını oluşturmaz | `NotApplicable` |
| `docs/archive/early-project/TEST_PLAN_v1.md`, `TEST_DATA_STRATEGY.md` | Tarihsel test dokümanları; Azure provisioning sözleşmesi değil | `NotApplicable` |
| `.github/workflows/deploy-azure.yml` | Production Environment onayı, OIDC login, ACR build/push, Bicep validate/what-if/create ve Container App revision/health/smoke doğrulaması içerir; database migration çalıştırmaz | GitHub Environment variables/OIDC federation yapılandırılmalı |
| `.github/workflows/ci.yml` | Canonical build/test, zorunlu fixture, migration artifact drift, Bicep/parameter ve API/Teams image build doğrulaması; Azure deployment yapmaz | `Canonical` |

Önceki Key Vault identity `ConfigurationMismatch` kaydı çözülmüştür:
`KEY_VAULT_CONFIGURATION.md` ve Bicep, Key Vault secret reference için API ve
Teams'in ayrı system-assigned identity'lerini kullanır; runtime UAMI yalnız ACR
ve SQL/Service Bus/Fabric/Power BI servis erişimleri içindir.
`docs/deployment/DEPLOYMENT_RUNBOOK.md` access matrisi aynı modele getirilmiştir. Azure'da
Container App/system identity bulunmaması ve mevcut UAMI'nin yalnız Web App
rolüne sahip olması gerçek durum olarak ayrıca raporlanmıştır.

## Yeniden kullanılabilecek kaynaklar

- `crm-project-rg`: `ReusableAsIs`; hedef deployment resource group'u olarak
  kullanılıp kullanılmayacağı ve default West Europe region seçimi onaylanmalı.
- `lokmancrmkv01`: vault/RBAC modeli bakımından `ReusableAsIs`; application DB,
  DWH, OLTP, internal API key ve Teams client secret hazır, Container App system identity
  role assignment'ları deployment ile oluşturulacak.
- `lokmanfabric`: capacity olarak `ReusableAsIs`; workspace/item/access
  kanıtları manuel sağlanmalı.
- `lokman-crm-project` ve `ASP-crmprojectrg-9e36`:
  `ReusableForLegacyOnly`.
- `crm-project-insights`, yönetilen Log Analytics workspace, workbook ve alerts:
  mevcut gözlemlenebilirlik varlıklarıdır. Container Apps Environment existing
  workspace'i yeniden kullanabilir; application telemetry için API/Teams SDK
  entegrasyonu ayrıca gerekir.
- `crmprojectacr634c`: Basic ACR olarak `ReusableAsIs`; digest-pinned API/Teams
  image referansları hazırdır ve runtime UAMI için exact ACR scope'undaki
  `AcrPull` ataması tamamlanmıştır.
- `id-crm-analytics-runtime`: uygulama runtime kimliği olarak `ReusableAsIs`;
  Container App'e bağlanmamış, Service Bus queue-scope Sender/Receiver rolleri
  tamamlanmıştır.
- `crmprojectsb634c` ve `crm-report-processing`: Standard Service Bus namespace
  ve repository sözleşmesiyle uyumlu queue olarak `ReusableAsIs`.
- `crmprojectsql634c` ve `CrmAnalytics`: Entra-only logical server ve Standard
  S0/10 DTU application database olarak `ReusableAsIs`; ilk altı migration,
  runtime UAMI minimum CRUD izinleri ve application DB Key Vault secret hazırlığı
  tamamlandı. Yedinci tracked migration ayrı DBA onayı bekler.
- `oidc-msi-9bde`: legacy Web App deployment amacı kanıtlıdır; runtime UAMI
  olarak yeniden kullanım için `InsufficientEvidence` ve açık sahiplik/güvenlik
  onayı vardır.

## Eksik kaynaklar

- Container Apps Environment.
- API ve Teams Container App'leri ve bunların system-assigned identity'leri.
- Azure Bot resource, Teams channel, OAuth connection ve callback/messaging endpoint.
- Eski SQL contract adları ile gerçek Fabric `mart` view'ları arasındaki onaylı,
  versionlanmış mapping.

## Manuel olarak alınması gereken Fabric/Power BI/Entra bilgileri

- Fabric job kullanılacaksa onaylı item/job ID, job type ve staging hedefi.
- Data-owner onaylı Fabric staging/parameter contract, lifecycle,
  retention/deletion ve minimum workspace/item izinleri.
- Power BI refresh kararı ile kullanıcı Entra/RLS erişim kanıtı. Workspace,
  report ve semantic model ID'leri pilot parametrelerinde hazırdır.
- Azure Bot üzerinde planlanan `crm-analytics-teams-oauth` OAuth connection'ın
  oluşturulması ile Bot messaging endpoint doğrulaması. Teams/Bot ve Entra API
  client ID'leri ayrı kimlikler olarak hazırdır.
- GitHub Environment OIDC federation ve gerçek
  `AZURE_CLIENT_ID`/tenant/subscription/resource-group değişken eşleşmeleri.
- Deployment region, `namePrefix`, ACR login server, runtime UAMI, Azure SQL
  application database, DWH, Power BI ve Service Bus non-secret değerleri pilot
  parametre dosyasına geçirilmiştir; QueryDwh ve QueryOltp enabled durumundadır.

## Deployment öncesi gerçek blocker'lar

Resource provider registration artık blocker değildir: `Microsoft.App`,
`Microsoft.ContainerRegistry`, `Microsoft.Sql` ve `Microsoft.ServiceBus`
subscription üzerinde `Registered` durumundadır. ACR, runtime UAMI, Azure SQL
application database ve Service Bus bootstrap kaynakları oluşturulmuştur;
aşağıdaki kaynak ve yapılandırma blocker'ları devam etmektedir.

1. Legacy Web App'te kalma veya ayrı Container Apps mimarisine geçme kararı
   verilmedi.
2. Container Apps Environment/apps ve Azure Bot eksiktir; Azure SQL ve Service
   Bus bootstrap'ları tamamlanmıştır. Doğrulanmış API/Teams image'ları ve
   digest-pinned deployment referansları hazırdır; Container Apps deploy edilmemiştir.
3. Runtime UAMI için ACR `AcrPull`, Service Bus Sender/Receiver, `CrmAnalytics`
   schema-scope minimum CRUD ve DWH read permission tamamlandı; Power BI minimum
   erişim/RLS kanıtı henüz sağlanmadı.
4. Application DB, QueryDwh, QueryOltp, internal API key ve Teams client secret
   mevcut ve enabled durumdadır; Teams referans aktivasyonu açıktır. Secret
   değerleri bu envanterde belgelenmedi.
5. `vw_sales`, `vw_customer_rfm` ve `vw_payment` için versionlanmış, sabit
   schema-qualified mapping eklenmiş ve `ConfigurationMappingRequired` çözülmüştür.
   DWH/OLTP sorgusu veya Fabric değişikliği yapılmamıştır.
6. Power BI kimlikleri hazırdır; erişim/RLS smoke kanıtı yoktur. Entra API client
   ID/audience ve ayrı Teams/Bot single-tenant identity hazırdır. OAuth connection
   adı planlanmış ancak connection oluşturulmamıştır; endpoint alanı placeholder'dır.
7. Bicep parametrelerinde gerçek region, ACR login server, digest-pinned API/Teams
   image referansları, runtime UAMI ID'leri, Azure SQL non-secret metadata'sı ve
   Service Bus FQDN/queue adı, gerçek prefix, Entra API, DWH ve Power BI değerleri
   vardır; yalnız Teams Container App URL/messaging endpoint ve kullanılmayan
   FabricJob alanları placeholder kalmıştır.
8. Existing Log Analytics Bicep'e bağlandı; API/Teams application telemetry'si
   için SDK olmadığı ve `ApplicationInsightsConnectionString` eşlenmediği açık
   bir gözlemlenebilirlik kararıdır.
9. DBA kontrollü ilk altı migration uygulandı ve image availability digest ile
   doğrulandı; yedinci tracked migration için uygulama kanıtı yoktur. Gerçek
   deployment, smoke/UAT kanıtı yoktur ve bu adımda bunlar çalıştırılmadı.

## Bir sonraki güvenli işlem sırası

1. Container Apps deployment'ından sonra Azure Bot, Teams channel, planlanan
   `crm-analytics-teams-oauth` OAuth connection ve callback/messaging endpoint
   ayrı onaylı adımda hazırlansın.
2. Power BI erişim/RLS ve runtime smoke ön koşulları doğrulansın.
3. Bicep parameter set'indeki kalan Container App URL/messaging placeholder'ı
   deployment çıktısıyla tamamlanıp bağımsız güvenlik ve mimari incelemeden geçirilsin.
4. Ancak ayrı açık yetki sonrasında Bicep validation/what-if ve rol kapsamı
   incelemesi yapılsın.
5. Yine ayrı açık yetki ve önceki kontrollerin kabulünden sonra doğrulanmış digest
   referanslarıyla Container Apps deployment, smoke ve UAT aşamalarına geçilsin.
6. Managed identity Service Bus smoke testi başarıyla tamamlandıktan sonra
   namespace local authentication kapatılsın.

## Application audit metadata migration — 2026-08-05

`20260805100943_AddApplicationAuditMetadata`, mevcut
`crm.ApplicationAuditEvents` tablosuna yalnız nullable `AuditMetadataJson`
`nvarchar(max)` alanını ve NULL/geçerli JSON check constraint'ini ekledi. Şema
`application-audit-metadata-v1`; JSON conversation, SQL contract, guardrail
doğrulanmış physical object, SHA-256 query-shape fingerprint, timeout/row limit ve
attempt/optional delivery kimliğini taşır. Source mevcut `DataSource` kolonunda
kalır. Raw SQL, prompt/result payload, parameter value, token, secret, API key,
connection string ve teknik hata ayrıntısı kaydedilmez.

Azure SQL history yeni migration'ı içerir; constraint enabled/trusted, kolon
nullable ve ikinci idempotent uygulama değişikliksizdir. Başlangıç/son application
tablo satır sayıları aynıdır. Geçici tek-IP firewall kuralı kaldırılmış ve final
liste yeniden boş doğrulanmıştır. Runtime UAMI migration/DDL yetkisi almamış;
rolsüz kalmış ve yalnız mevcut application DML grant'lerini korumuştur. Script
SHA-256 değeri
`082925c36e0c5bb31cc468261fa07b9679509bdb39530ef951851b53f47726e4`'tür.

Pilot API artık
`crmprojectacr634c.azurecr.io/crm-analytics-api@sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`
referansına sabittir. Teams digest'i değişmemiştir. Bu işlem Container Apps
validation, what-if veya deployment içermemiştir.

Bu envanter herhangi bir deployment onayı veya otomatik mimari kararı değildir.
