# Azure Pilot Deployment Plan

## Targeted Teams legal-pages deployment result - 2026-08-05

The legal pages were released with a revision-only operation. The main Bicep
deployment, API Container App, managed environment, Key Vault, SQL, Fabric,
Service Bus, Azure Bot, image build/push, and secret operations were explicitly
out of scope and were not run.

1. Repository cleanliness, `git diff --check`, tenant, subscription, source
   revision, traffic, and target-resource baselines passed.
2. Azure CLI 2.88.0 with Container Apps extension 1.3.0b4 exposed the required
   `revision copy` contract. The source revision was copied with suffix
   `legalpages1` and only the Teams image digest was overridden.
3. The new revision reached active/running/healthy, 1/1 ready, zero restarts, and
   no startup exception matches before traffic changed.
4. Traffic moved from the old revision at 100 percent to the new revision at
   100 percent. Three HTML pages and both health endpoints returned HTTP 200;
   the bot endpoint returned HTTP 405 to HEAD as expected for a POST-only route.
5. After the five HTTP 200 checks and a second 1/1 readiness gate, the old
   revision was deactivated but not deleted. The new revision remains at 100
   percent traffic.
6. Before/after resource hashes and Activity Log proved that target-external
   Azure resource configuration did not change. API remains healthy on its
   approved digest; ACR admin remains disabled.
7. A GA manifest 1.28 package was produced at
   `deploy/teams/crm-analytics-pilot-teams-app.zip`. Automated local and
   JSON Schema validation passed. Developer Portal import/validation and the
   interactive OAuth/report UAT remain manual steps documented in
   `docs/deployment/TEAMS_APP_INSTALLATION.md`.

## UAMI connection selection recovery result - 2026-08-05

1. The existing application DB Key Vault value was parsed and rebuilt only in
   process memory. Managed-identity authentication now explicitly selects runtime
   UAMI client ID `da156325-7c70-46fc-b899-024f9d73b994`; the new enabled Key
   Vault version preserved content type/tag metadata. The secret value was not
   reported or persisted in repository files.
2. The API secret reference remains versionless and uses the API system identity.
   Teams received no application SQL, Fabric DWH, or Fabric OLTP secret access.
3. API revision `crm-analytics-pilot-api--0000002` was restarted. A recovery copy,
   `crm-analytics-pilot-api--uamifix1`, used the unchanged approved image digest
   and resolved the latest app-scope secret, but Azure SQL still rejected the
   token-identified principal. The API remained unhealthy. Teams revision
   `crm-analytics-pilot-teams--0000001` remained unhealthy; none of the four
   health endpoint checks returned the required HTTP 200 result.
4. Application SQL UAMI authorization failed. Fabric DWH and Fabric OLTP UAMI
   smokes and the Service Bus send/receive/complete diagnostic were not run because
   the healthy-API prerequisite was absent. Queue active/dead-letter/scheduled
   counts remained zero.
5. No revision was deactivated or deleted because no replacement became healthy.
   The final recovery verdict is `SqlPrincipalMismatchBlocked`; database
   permissions and roles were not changed.
6. Pilot firewall hardening remains mandatory: after a custom VNet/NAT Gateway or
   private-endpoint path is proven, remove `AllowAllWindowsAzureIps`.

## Pilot recovery execution - 2026-08-05

The controlled recovery used the existing environment and apps; it did not create
a replacement environment, rebuild images, change SQL authentication/authorization,
or modify Key Vault secret values.

1. Repository build/lint and pilot parameter compilation succeeded. Azure
   resource-group validation returned `Succeeded`.
2. What-if contained no resource `Delete`. Resource changes were limited to the
   two Container Apps, existing environment default/computed fields, and creation
   of the SQL child firewall rule `AllowAllWindowsAzureIps`. SQL databases, Key
   Vault access model, Service Bus, ACR, App Service, Fabric, and unrelated managed
   identities were ignored. Existing role assignments remained single-secret scope.
3. Deployment `crm-pilot-recovery-20260805-144323` succeeded and returned the new
   environment/app name, FQDN, and latest-revision outputs.
4. URL generation is permanent Bicep logic based on deterministic app names plus
   the environment `defaultDomain`; API-to-Teams and Teams-to-internal-API settings
   no longer depend on another app's generated properties or a runtime URL override.
5. The pilot SQL rule allows Azure-hosted resources to reach the logical server.
   Microsoft Entra-only authentication and the existing minimum SQL grants remain
   in force. The rule does not grant database access.
6. New revisions were created but did not become healthy. The firewall error was
   replaced by a token-principal database login failure. Because the application
   DB secret was explicitly out of scope for reading/changing, the required
   user-assigned identity selection could not be corrected in this run.
7. Key Vault sync succeeded for both apps and the old Teams KEDA secret-resolution
   warning did not recur. No old revision was deactivated or deleted.
8. Application SQL smoke failed at authentication. Fabric DWH, Fabric OLTP, and
   Service Bus runtime smokes remain blocked behind a healthy API; no queue message
   was created.

Remaining recovery action: an authorized secret operator must update the existing
application DB connection configuration so Microsoft.Data.SqlClient explicitly
selects runtime UAMI client ID `da156325-7c70-46fc-b899-024f9d73b994`, without
changing Entra-only authentication or the UAMI's minimum database grants. Then a
new controlled revision must be validated, health-tested, assigned 100 percent
traffic, and only afterward may the old unhealthy revisions be deactivated.

Production hardening remains mandatory: migrate to a workload-profile environment
with a custom VNet and NAT Gateway or private endpoint, validate the private route,
then remove `AllowAllWindowsAzureIps`.

2026-08-05 OLTP Query Builder güncellemesi yalnız API runtime image'ını
`a94fc43c9c7c-oltp-qb-manual-20260805084130` tag'iyle yeniledi. Pilot API
referansı OCI index digest'ine sabitlendi; Teams image değişmedi. Bu güncellemede
Container Apps deployment, Azure validation veya what-if çalıştırılmadı.

Bu plan `634ccf7f-1073-4965-9385-9ae3bbef1533` subscription'ındaki hedef
Container Apps pilotu için deployment öncesi bootstrap sırasını tanımlar. Tenant
`2e010224-86ea-4b34-93ea-f9833137c80e`, ana resource group
`crm-project-rg` ve Fabric resource group `fabric-rg` olarak Azure CLI ile
2026-08-04 tarihinde doğrulanmıştır.

Bootstrap adımlarında `crmprojectacr634c` ACR'si,
`id-crm-analytics-runtime` UAMI'si, `crmprojectsb634c` Service Bus namespace'i,
`crm-report-processing` queue'su, `crmprojectsql634c` Azure SQL logical server'ı
ve `CrmAnalytics` application database'i oluşturuldu. Runtime UAMI'nin ACR
`AcrPull`, Service Bus queue-scope Data Sender/Receiver ve application database
schema-scope CRUD izinleri tamamlandı. DBA kontrollü SQL migration uygulandı ve
application database connection string'i yalnız Key Vault'a yazıldı. API image'ı
önceki DWH mapping image'ına sabit kaldı. Bu adımda Teams/Bot Client ID,
`SingleTenant` app type, tenant ID, OAuth connection name, client-secret Key Vault
reference sözleşmesi ve Production/Staging validator değişikliklerini içeren yalnız
Teams image'ı benzersiz manuel tag altında build/push edildi. Deployment
validation/what-if veya Container Apps deployment yapılmadı; secret değeri
çıktılanmadı veya belgelenmedi. API/Teams image'ları OCI index digest'leriyle
doğrulandı, pilot Bicep parametreleri bu digest'lere sabitlendi ve tag'ler
write-disabled durumdadır.

Bu hazırlık adımında ayrıca `crm-analytics-query-dwh` ve
`crm-analytics-internal-api-key` Key Vault secret'ları güvenli süreç belleğinden
oluşturuldu; yalnız ad/enabled/content-type metadata'sı doğrulandı. Application
DB ve OLTP secret'ları okunmadı veya değiştirilmedi. Teams client secret
`crm-analytics-teams-client-secret` adıyla doğrudan Key Vault'a yazıldı; yalnız
ad/enabled/content-type metadata'sı doğrulandı. Gerçek Entra API, Fabric DWH,
Power BI ve ayrı Teams/Bot non-secret kimlikleri pilot parametrelerine geçirildi.
OAuth connection adı planlanan sabit değer olarak kaydedildi; connection, Azure
Bot ve messaging endpoint oluşturulmadı.

## Tamamlanan ACR, runtime identity, Azure SQL ve Service Bus bootstrap'ı

| Kaynak/işlem | Gerçek değer | Durum |
|---|---|---|
| ACR | `crmprojectacr634c` / `crmprojectacr634c.azurecr.io` | `Succeeded`; Basic, Sweden Central, admin disabled, public network enabled, RBAC mode |
| Runtime UAMI | `id-crm-analytics-runtime` | `Existing`; Sweden Central |
| UAMI client ID | `da156325-7c70-46fc-b899-024f9d73b994` | Non-secret deployment parametresine yazıldı |
| UAMI principal ID | `ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` | Rol ataması assignee object ID'si |
| ACR `AcrPull` | ACR resource scope | `Existing`; runtime UAMI için read-only doğrulandı |
| API image | `crm-analytics-api:b83b9712c652-audit-metadata-manual-20260805102107` | OCI index `sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`; `linux/amd64` manifest `sha256:cf97b6a0d383b25742d113a06164fcf2f5cbb086a5c2057c17b22234e0214103`; registry image size `139733722` byte; audit metadata runtime/migration desteğini içerir; tag write-disabled |
| Teams image | `crm-analytics-teams:45cbd936556b-teams-bot-manual-20260804215550` | OCI index `sha256:91bd6b83283f0c66686e0219256e3a4a881b0ff74cdab86002529e9c4036650f`; `linux/amd64` manifest `sha256:239e4caf3fc8b1460661439de55879c1a355c4615c19bd5120f5d04e6a411eee`; `linux/amd64`; compressed layer+config `100182518` byte; SingleTenant bot configuration içerir; tag write-disabled |
| Azure SQL logical server | `crmprojectsql634c` / `crmprojectsql634c.database.windows.net` | `Ready`; Sweden Central, Entra-only, public network enabled, TLS 1.2, firewall rule yok |
| Azure SQL application DB | `CrmAnalytics` | `Online`; Standard S0, 10 DTU |
| Azure SQL migration | `deploy/sql/CrmAnalytics.Migrations.sql` | Azure kanıtında ilk altı migration `20260805100943_AddApplicationAuditMetadata` dahil uygulanmış durumda; tracked artifact ayrıca DBA/operator onayı bekleyen `20260809235416_AddSubmittedSemanticPlan` migration'ını içerir |
| Azure SQL runtime user | `id-crm-analytics-runtime` | `Existing`; `EXTERNAL_USER`, yalnız `crm` schema CRUD, yönetici rolü yok |
| Application DB secret | `crm-analytics-application-db` | `Existing`; enabled, değer/version belgelenmedi |
| Service Bus namespace | `crmprojectsb634c` / `crmprojectsb634c.servicebus.windows.net` | `Succeeded`; Standard, Sweden Central, public network enabled, TLS 1.2, local authentication enabled |
| Service Bus queue | `crm-report-processing` | `Active`; duplicate detection ve expiration DLQ açık, sessions kapalı |
| Service Bus Sender/Receiver | Queue resource scope | İki rol de runtime UAMI için `Existing` |

Read-only doğrulamayla tamamlandığı görülen ACR ataması:

| Alan | Exact değer |
|---|---|
| Scope | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ContainerRegistry/registries/crmprojectacr634c` |
| Principal ID | `ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` |
| Principal type | `ServicePrincipal` |
| Role | `AcrPull` |
| Role definition ID | `/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/providers/Microsoft.Authorization/roleDefinitions/7f951dda-4ed3-4680-a7ca-43fe172d538d` |
| Role assignment ID | `dd2cb918-08c1-4f87-822b-8fb66c02006b` |

`AcrPush` veya başka rol verilmedi. UAMI ACR'ye resource identity olarak
bağlanmadı; yalnız yukarıdaki role assignment mevcuttur.

Service Bus için yetkili operatörün tamamlaması gereken rol ataması yoktur. İki
atama da principal ID `ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` ve scope
`/subscriptions/634ccf7f-1073-4965-9385-9ae3bbef1533/resourceGroups/crm-project-rg/providers/Microsoft.ServiceBus/namespaces/crmprojectsb634c/queues/crm-report-processing`
ile tamamlanmıştır:

| Rol | Role definition ID | Durum |
|---|---|---|
| Azure Service Bus Data Sender | `69a216fc-b8fb-44d8-bc22-1f3c2cd27a39` | `Existing` |
| Azure Service Bus Data Receiver | `4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0` | `Existing` |

## Hedef ve kaynak sınırı

Korunan hedef mimari şöyledir:

- Yeni API Container App internal ingress, Teams Container App external ingress
  kullanır ve ikisi yeni bir Container Apps Environment içinde çalışır.
- `lokman-crm-project` Web App ve `ASP-crmprojectrg-9e36` planı
  `ReusableForLegacyOnly` sınıfındadır; Bicep kapsamına alınmaz, değiştirilmez ve
  migration hedefi yapılmaz.
- `lokmancrmkv01` mevcut Key Vault olarak kullanılır; yeni vault oluşturulmaz.
- `managed-crm-project-insights-ws` mevcut Log Analytics workspace olarak
  kullanılır; yeni workspace seçili pilot parametreleriyle oluşturulmaz.
- `crm-project-insights` mevcut workspace-based Application Insights bileşeni
  olarak kaydedilir. API/Teams kodunda Application Insights SDK kaydı olmadığı
  için connection string enjekte edilmez; Container Apps platform logları aynı
  bağlı Log Analytics workspace'e gönderilir.
- `lokmanfabric` (`fabric-rg`, F2, Sweden Central) yalnız mevcut kapasite olarak
  belgelenir. Bu Bicep deployment'ına eklenmez ve değiştirilmez.
- Pilot analytics provider `Direct`, OLTP `false` olur. `Direct` seçimi
  FabricJob'ı devre dışı bırakır; FabricJob için ayrı enable bayrağı yoktur.

## Bicep kaynak sözleşmesi

| Kaynak | Şablon davranışı |
| --- | --- |
| Container Apps Environment | Yeni oluşturur |
| API Container App | Yeni oluşturur; internal ingress, system + runtime UAMI |
| Teams Container App | Yeni oluşturur; external ingress, system + runtime UAMI |
| Log Analytics | Existing ID veya ad/RG seçilirse yeniden kullanır; yalnız hiçbiri seçilmezse yeni workspace oluşturur |
| Key Vault secret-scope role assignments | Key Vault entegrasyonu açıksa API/Teams system identity'leri için oluşturur |
| Key Vault, secrets, Application Insights | Existing bekler; oluşturmaz |
| ACR | Existing `crmprojectacr634c`; login server parametresi dolduruldu, Bicep oluşturmaz |
| Runtime UAMI | Existing `id-crm-analytics-runtime`; resource ID ve client ID parametreleri dolduruldu, Bicep oluşturmaz |
| Azure SQL application DB | Existing `crmprojectsql634c` / `CrmAnalytics`; Key Vault connection-string secret sözleşmesi bekler, oluşturmaz |
| Service Bus namespace ve queue | Existing `crmprojectsb634c` / `crm-report-processing`; FQDN ve queue name parametreleri dolduruldu, Bicep oluşturmaz |
| Azure Bot | Missing; oluşturmaz |
| Fabric ve Power BI artefact'ları | ID'leri bekler; oluşturmaz veya değiştirmez |
| Legacy App Service/plan | Kapsam dışı; referans vermez veya değiştirmez |

## Existing observability modeli

Pilot parametre dosyası Log Analytics için şu doğrulanmış adı ve resource
group'u seçer:

- `managed-crm-project-insights-ws`
- `ai_crm-project-insights_dc8b3de1-135d-462c-842f-3b1847ebce79_managed`

Şablon bunlardan workspace resource ID'sini oluşturur. Tam ID verilirse ID daha
önceliklidir. Hiçbir açık workspace seçilmez ancak existing Application Insights
seçilirse component'in `WorkspaceResourceId` özelliği fallback olur. Container
Apps Environment modülü `customerId` ve `primarySharedKey` değerlerini deployment
runtime'ında `reference`/`listKeys` ile çözer. Shared key parametre değildir,
output değildir ve repository/log'a yazılmamalıdır. Deployment principal'ının
workspace üzerinde `Microsoft.OperationalInsights/workspaces/sharedKeys/action`
izni gerçek deployment öncesinde doğrulanmalıdır.

`crm-project-insights` component'i ad/RG veya tam resource ID ile kabul edilir ve
yeni component oluşturulmaz. Hedef API/Teams projelerinde
`Microsoft.ApplicationInsights.AspNetCore`, `AddApplicationInsightsTelemetry`
veya eşdeğer bir telemetry kaydı yoktur. Key Vault'taki mevcut
`ApplicationInsightsConnectionString` bu nedenle yeni Container App'lere
bağlanmaz; yalnız environment variable eklemek çalışan entegrasyon sağlamaz.
Application-level telemetry istenirse ayrı kod, paket, test ve image build
değişikliği gerekir. Legacy projenin mevcut entegrasyonu korunur.

## Key Vault ve identity modeli

Key Vault `lokmancrmkv01`, `crm-project-rg`, Sweden Central ve Azure RBAC modeli
ile `Existing/ReusableAsIs` durumundadır. Secret değerleri incelenmemiştir.

- API ve Teams ayrı system-assigned identity alır.
- Container App secret reference'ları `identity: 'system'` kullanır.
- Bicep `Key Vault Secrets User` rolünü yalnız ilgili existing secret scope'unda
  bu system identity'lere atar.
- Runtime UAMI yalnız ACR, Azure SQL, DWH/opsiyonel OLTP, Service Bus, Fabric ve
  Power BI servis erişimleri içindir. Runtime UAMI'ye gereksiz Key Vault secret
  rolü verilmez.
- `id-crm-analytics-runtime` bu model için oluşturuldu; henüz Container App'e
  bağlanmadı. ACR `AcrPull` ve Service Bus queue-scope Data Sender/Receiver
  rolleri, application DB user/izinleri ve sağlanan DWH read permission tamamlandı.
- Secret değerleri Bicep parametrelerinde, output'larında, workflow girdilerinde
  veya planda bulunmaz.

Bicep DWH ve internal key için sırasıyla version'sız
`https://lokmancrmkv01.vault.azure.net/secrets/crm-analytics-query-dwh` ve
`https://lokmancrmkv01.vault.azure.net/secrets/crm-analytics-internal-api-key`
referanslarını üretir. API'de `ConnectionStrings__QueryDwh` DWH secret'ına,
`TeamsNotifications__ApiKey` ortak internal key'e bağlıdır. Teams'de
`BackendApi__InternalApiKey` ve `ReportNotifications__ApiKey` aynı internal key'e
bağlıdır; Teams'e DWH secret verilmez. API'nin
`QueryExecution__Dwh__ManagedIdentityClientId` değeri runtime UAMI Client ID'si
`da156325-7c70-46fc-b899-024f9d73b994` olarak kalır.

## `oidc-msi-9bde` değerlendirmesi

Read-only metadata sonucu:

- identity `crm-project-rg` içinde France Central'dadır;
- federated credential `github-devops`, GitHub Actions issuer'ı ve `devops`
  branch subject'i ile mevcuttur;
- tek görülen role assignment, `lokman-crm-project` Web App scope'undaki
  `Website Contributor` rolüdür;
- kök `.github/workflows/devops_lokman-crm-project.yml` aynı legacy Web App'e
  OIDC login ile deployment yapar;
- ACR pull, SQL, Service Bus, Fabric veya Power BI runtime erişimi kanıtı yoktur.

Sonuç `InsufficientEvidence`'dır. `oidc-msi-9bde` otomatik runtime UAMI olarak
seçilmedi, değiştirilmedi ve yeniden kullanılmadı. Pilot için ayrı
`id-crm-analytics-runtime` UAMI'si oluşturuldu.

## Region kullanılabilirliği

Azure Resource Provider metadata sonucu üç aday da hedef dört resource type'ı
listeler:

| Region | Container Apps managed environment | ACR | Azure SQL server | Service Bus namespace |
| --- | --- | --- | --- | --- |
| Sweden Central | Destekleniyor | Destekleniyor | Destekleniyor | Destekleniyor |
| France Central | Destekleniyor | Destekleniyor | Destekleniyor | Destekleniyor |
| West Europe (repository örneği/RG metadata region'ı) | Destekleniyor | Destekleniyor | Destekleniyor | Destekleniyor |

Mevcut kaynak region'ları:

| Kaynak | Region |
| --- | --- |
| Key Vault `lokmancrmkv01` | Sweden Central |
| Application Insights `crm-project-insights` | Sweden Central |
| Log Analytics `managed-crm-project-insights-ws` | Sweden Central |
| Fabric capacity `lokmanfabric` | Sweden Central |
| ACR `crmprojectacr634c` | Sweden Central |
| Runtime UAMI `id-crm-analytics-runtime` | Sweden Central |
| Service Bus `crmprojectsb634c` | Sweden Central |
| Legacy App Service/plan | France Central |
| `crm-project-rg` ARM metadata | West Europe |

Yeni pilot kaynaklarını tek bir region'da toplamak açısından servis
availability engeli görülmemiştir. Resource group metadata region'ı içindeki
kaynakların region'ını zorlamaz; legacy App Service de ayrı ve korunmuş bir iş
yüküdür. Sweden Central mevcut Key Vault, observability ve Fabric capacity ile
co-location sağlar. Bu bootstrap için pilot region `swedencentral` olarak
doğrulanmış, ACR/UAMI ve pilot parametre dosyasında uygulanmıştır. Kalan servisler
için quota/SKU ve kurumsal policy kontrolleri hâlâ gereklidir.

Dört provider (`Microsoft.App`, `Microsoft.ContainerRegistry`, `Microsoft.Sql`,
`Microsoft.ServiceBus`) subscription'da `Registered` durumundadır. Bu bootstrap
başlangıcında `Microsoft.ContainerRegistry` ve `Microsoft.ManagedIdentity` de
`Registered` olarak doğrulanmış, provider register çalıştırılmamıştır.

## Kesin bootstrap ve deployment sırası

ACR, runtime UAMI ve Service Bus adımları tamamlanmıştır. Kalan adımlar yalnız
kendi açık Azure yazma/deployment yetkileri verildikten sonra uygulanır ve
belirtilen çıktıyla kapanır.

### 1. Region ve naming onayı

Pilot region bu bootstrap için Sweden Central olarak belirlenmiştir. ACR ve
runtime UAMI, Azure SQL ve Service Bus adları uygulanmıştır; `namePrefix` ve Azure
Bot adı hâlâ onay bekler. Global benzersizlik, policy ve quota/SKU kalan kaynaklar
için doğrulanır.

### 2. ACR oluşturulması

Tamamlandı: `crmprojectacr634c`, Sweden Central, Basic, admin disabled, public
network enabled ve RBAC mode ile oluşturuldu. `crmprojectacr634c.azurecr.io`
`acrLoginServer` parametresine kaydedildi. Credential okunmadı ve image push
yapılmadı.

### 3. Runtime UAMI oluşturulması

Tamamlandı: `oidc-msi-9bde` kullanılmadan uygulama runtime'ına özel
`id-crm-analytics-runtime` oluşturuldu; resource ID ve client ID pilot
parametrelerine yazıldı. Identity hiçbir Container App'e bağlanmadı.

### 4. Azure SQL application database oluşturulması

Tamamlandı: `crmprojectsql634c` logical server'ı Sweden Central'da, Microsoft
Entra-only authentication, Entra administrator `ramazanb` (object ID
`eac148db-46b9-4895-9a5e-834ef358384b`), public network `Enabled` ve minimum TLS
`1.2` ile oluşturuldu. Server firewall listesi boştur; `Allow Azure services` /
`0.0.0.0` dahil hiçbir kural oluşturulmadı. FQDN
`crmprojectsql634c.database.windows.net` değeridir.

`CrmAnalytics` database'i `Online`, Standard S0 / 10 DTU olarak oluşturuldu.
`deploy/sql/CrmAnalytics.Migrations.sql` artifact'ının ilk altı EF migration'ı
uygulandı. Mevcut tracked artifact'taki yedinci
`20260809235416_AddSubmittedSemanticPlan` için uygulama kanıtı yoktur ve ayrı DBA
onayı gerekir. Runtime UAMI explicit object ID ile `EXTERNAL_USER` contained user
olarak oluşturuldu ve yalnız `crm` şemasında `SELECT`, `INSERT`, `UPDATE`, `DELETE`
aldı; `EXECUTE`, DDL veya yönetici rolleri verilmedi. `ConnectionStrings:CrmAnalytics`
için `crm-analytics-application-db` Key Vault secret'ı mevcut ve enabled durumdadır;
değeri/version bilgisi bu plana alınmamıştır. Tek-IP geçici firewall kuralı
kaldırıldı ve final server firewall listesi boştur.
`ConnectionStrings:QueryDwh`,
`eqbaclxkqy2exe7k7gbtcn6iby-5es6c723oane3lbx5mhcfaa6q4.datawarehouse.fabric.microsoft.com:1433`
ve Initial Catalog `wh_crm_analytics` için managed identity connection olarak
hazırdır. `ConnectionStrings:QueryOltp`, pilotta enabled durumundadır ve
`crm-analytics-query-oltp` Key Vault secret reference'ına bağlanır.

Fabric SQL Database OLTP hedefi
`eqbaclxkqy2exe7k7gbtcn6iby-5es6c723oane3lbx5mhcfaa6q4.database.fabric.microsoft.com:1433`,
Initial Catalog `crm_oltp-3fc7fdf2-6c02-4c2f-8811-298d6b38c299` ve schema `dbo`
olarak hazırlanmıştır. Runtime UAMI Client ID
`da156325-7c70-46fc-b899-024f9d73b994`, secret adı
`crm-analytics-query-oltp` ve secret durumu enabled'dır. `queryOltpEnabled=true`
ile yalnız `dbo.vw_operational_orders` read view'ı OLTP report query yüzeyidir.
Current report execution yolu SELECT-only kalır.

Database permission envanteri değiştirilmedi: runtime UAMI beş base table üzerinde
`SELECT`, `INSERT`, `UPDATE`, view üzerinde `SELECT` yetkisine sahiptir; `DELETE`
verilmemiştir. Base table write izinleri report query allowlist'ine eklenmemiştir.
Runtime managed-identity smoke testi Container Apps deployment sonrasında yapılır.

### 5. Service Bus namespace ve queue oluşturulması

Tamamlandı: `crmprojectsb634c` namespace'i Standard SKU ile Sweden Central'da
oluşturuldu; provisioning state `Succeeded`, public network `Enabled`, minimum
TLS `1.2`, local authentication `Enabled` durumundadır. FQDN
`crmprojectsb634c.servicebus.windows.net` olarak parametreye yazıldı.

`crm-report-processing` queue'su `Active` durumundadır. Repository sözleşmesine
göre duplicate detection ve expiration dead-lettering açık, sessions kapalıdır.
Açıkça tanımlanmayan alanlarda Azure Standard varsayılanları kullanıldı: lock
`PT1M`, max delivery `10`, default TTL `P10675199DT2H48M5.4775807S`, duplicate
window `PT10M`, max size `1024 MB`, partitioning kapalı. Deterministik `MessageId`
ve application idempotency değiştirilmedi; ayrı DLQ queue oluşturulmadı.

### 6. Runtime UAMI role ve DB izinleri

Runtime UAMI'nin ACR `AcrPull`, Service Bus Data Sender/Receiver, application DB
izinleri ve DWH read permission'ı tamamlandı. Application DB'de yalnız `crm`
şeması üzerinde `SELECT`, `INSERT`, `UPDATE`, `DELETE` verildi. Power BI/Fabric
erişimi yalnız seçili Direct/reporting akışı gerçekten gerektiriyorsa minimum
kapsamda verilir. Key Vault Secrets User verilmez. Çıktı: scope/role/DB grant
matrisi ve güvenlik onayı.

Service Bus Data Sender (`69a216fc-b8fb-44d8-bc22-1f3c2cd27a39`) ve Data Receiver
(`4f6d3b9b-027b-4f4c-9142-0e5a2a2247e0`) atamaları runtime UAMI principal ID'si
`ab5c6bec-a1b1-43fc-af9f-3aebe38a6182` için yalnız queue scope'unda tamamlandı.
Namespace scope'una genişletme yapılmadı. Önceki `PermissionDenied` kaydından
sonra ACR `AcrPull` atamasının exact ACR scope, principal ID ve role definition ID
ile mevcut olduğu read-only doğrulandı. SQL migration, runtime UAMI contained user,
minimum application DB izinleri ve application DB Key Vault secret hazırlığı
tamamlandı.

### 7. Key Vault secret değerlerinin yetkili operatör tarafından eklenmesi

`crm-analytics-application-db` ve `crm-analytics-query-oltp` mevcut ve enabled
durumdadır; bu adımda değerleri okunmadı ve değiştirilmedi.
`crm-analytics-query-dwh` enabled ve content type
`application/vnd.microsoft.data.connection-string` olarak oluşturuldu.
`crm-analytics-internal-api-key` en az 32 cryptographically secure random byte
ile oluşturuldu ve enabled metadata'sı doğrulandı. Teams client secret
`crm-analytics-teams-client-secret` adı ve
`application/vnd.microsoft.botframework.client-secret` content type'ı ile
oluşturuldu; enabled metadata'sı secret değeri tekrar okunmadan doğrulandı.
`teamsClientSecretIntegrationEnabled=true` ile yalnız Teams Container App'in
Key Vault referansı ve `Teams__ClientSecret` environment mapping'i etkinleştirildi.
Secret API Container App'e verilmez. Teams system identity secret-scope rol
ataması Container Apps deployment aşamasında oluşturulacaktır.
`ApplicationInsightsConnectionString` yeni uygulamalara bağlanmaz. Değerler
çıktıya veya deployment parametresine alınmaz. Çıktı: yalnız secret adı ve güvenli
metadata varlık kanıtı; değer veya version kanıtı değil.

### 8. Immutable image doğrulaması ve deployment referansları

Tamamlandı: source-aware OLTP Query Builder/runtime değişikliklerini içeren yalnız
API image'ı `a94fc43c9c7c-oltp-qb-manual-20260805084130` benzersiz tag'iyle
build/push edildi. Teams image ve digest referansı değiştirilmedi.
OCI index digest'leriyle yapılan pull/inspect işlemleri `linux/amd64` çalıştırılabilir
manifestlerini doğruladı. API tag'i ile yeni Teams tag'i `writeEnabled=false`,
`readEnabled=true`, `listEnabled=true` durumundadır; `deleteEnabled` değiştirilmedi.
`latest` oluşturulmadı veya kullanılmadı. Azure Bot, OAuth connection, Teams channel
ve messaging endpoint henüz oluşturulmadı; Container Apps henüz deploy edilmedi.

Pilot deployment parametreleri aşağıdaki tam ve immutable referansları taşır;
`main.bicep` ve Container App modülü bu string'leri tag/digest yeniden
birleştirmeden image alanına geçirir:

- API: `crmprojectacr634c.azurecr.io/crm-analytics-api@sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`
- Teams: `crmprojectacr634c.azurecr.io/crm-analytics-teams@sha256:91bd6b83283f0c66686e0219256e3a4a881b0ff74cdab86002529e9c4036650f`

Gelecek CI/CD stratejisi:

- Her build benzersiz `<12-char-git-sha>-<pipeline-run-id>-<attempt>` tag'ini kullanır.
- Bir tag yalnız bir kere push edilir.
- Push sonrasında registry digest'i çözülür.
- Bicep/Container Apps deployment'ı tag yerine digest referansı kullanır.
- Başarılı image doğrulaması sonrasında tag write-disabled yapılır.
- Yalnız Git SHA'nın build kimliği olarak yeniden kullanılmasına izin verilmez.
- `latest` kullanılmaz.

Yeni API manuel image'ı `a94fc43c9c7c9078a2738d33a937700b88a8e24e` taban
commit'inden ve bu commit'e henüz dahil olmayan doğrulanmış çalışma ağacı
değişikliklerinden üretildi; UTC timestamp tag benzersizliğini korur. Exact OCI
index pull/inspect, local Development/mock health ve environment/layer
history/root filesystem/appsettings secret taramaları başarılıdır. Teams image
build veya push edilmedi.

### 9. Bicep validate ve what-if

Önce local build/lint/build-params tekrarlanır. Ardından ayrı açık Azure yetkisi
ile resource-group validate ve what-if çalıştırılır. What-if yeni Log Analytics,
Application Insights, Key Vault, Fabric veya legacy App Service değişikliği
göstermemelidir. Role assignment scope'ları tekil secret'larla sınırlı olmalıdır.
Çıktı: incelenmiş validate/what-if artefact'ı ve deployment onayı.

### 10. Container Apps deployment

Onaylı parameter set'iyle Environment, internal API, external Teams ve gereken
system-identity secret-scope rol atamaları deploy edilir. Existing Log Analytics
ID'si kullanıldığı ve shared key hiçbir output'ta olmadığı doğrulanır. Çıktı:
resource IDs, revision adları ve non-secret FQDN'ler.

### 11. SQL migration

Deployment'tan ayrı DBA kontrollü idempotent artifact'ın ilk altı migration'ı
application DB'ye uygulandı ve history kayıtları doğrulandı. Tracked
`deploy/sql/CrmAnalytics.Migrations.sql` artık yedinci
`20260809235416_AddSubmittedSemanticPlan` migration'ını da içerir; bu migration
Container App deployment'ından önce ayrıca onaylanıp uygulanmalıdır. Uygulama
startup'ı migration çalıştırmaz.

### 12. Health ve smoke testleri

API internal erişim yolundan `/health/live` ve `/health/ready`, Teams public
endpoint'inden health ve güvenli callback akışları test edilir. SQL, Service Bus,
Key Vault reference resolution ve revision health doğrulanır; secret/log sızıntısı
aranır. Çıktı: zaman damgalı smoke raporu.

Managed identity ile Service Bus send/receive smoke başarıyla tamamlandıktan sonra
namespace'te local authentication kapatılır. Bu güvenlik sıkılaştırması smoke
öncesinde yapılmaz ve bu bootstrap adımında uygulanmamıştır.

### 13. Azure Bot ve Teams bağlantısı

Teams/Bot single-tenant Entra identity ve Key Vault credential hazırlığı
tamamlanmıştır:

| Alan | Değer |
|---|---|
| App Registration display name | `crm-analytics-teams-bot` |
| Bot Application Client ID | `3e277fe2-0da0-4149-80da-2168ac44e7b9` |
| Application Object ID | `eeed61b7-4c9d-444f-ac82-efb3d76de126` |
| Service principal Object ID | `a8e37340-3f61-4e32-af40-f7eeea1478b7` |
| signInAudience / app type | `AzureADMyOrg` / `SingleTenant` |
| Credential oluşturma | `2026-08-04T21:38:47.3659122Z` |
| Credential expiration | `2027-01-31T21:38:47.3659122Z` |
| Key Vault secret | `crm-analytics-teams-client-secret`; enabled |
| Planlanan OAuth connection adı | `crm-analytics-teams-oauth`; henüz oluşturulmadı |

Bot Client ID ile API Client ID `809393e5-ff46-4422-a4b3-084ed1b91d98`
ayrı kimliklerdir; API audience
`api://809393e5-ff46-4422-a4b3-084ed1b91d98` değiştirilmemiştir. Bir sonraki
onaylı adımda Azure Bot, Teams channel ve OAuth connection oluşturulur; Teams
external Container App deployment'ından sonra messaging endpoint ve public
callback URL'si belirlenip manifest doğrulanır. Pilot parametresinde Azure Bot
resource name placeholder'ı `crm-analytics-teams-bot` olarak tutulur; URL alanı
placeholder kalır.

### 14. Fabric DWH ve Power BI smoke testleri

#### Source-aware OLTP Query Builder gate — 2026-08-05

Before any later deployment, the API runtime must carry
`fabric-oltp-operational-orders-v1`, select DWH/OLTP before SQL generation, retain
source through revisions, isolate prompts/guardrails, enforce OLTP TOP 1000 and
15-second ceilings, and reject SELECT INTO upstream. Fabric compatibility in this
task is current-user metadata/no-row compile only. Runtime UAMI smoke is deferred
until deployment; deployment, validation, and what-if are explicitly out of scope.
Current-user `TOP (0)`/`WHERE 1=0` compile checks passed for detail, date, city,
and stable Top N shapes; describe-first-result-set passed. No runtime UAMI smoke
was performed.

`lokmanfabric` capacity değiştirilmeden DWH ve Power BI erişimi doğrulanır.
Analytics provider `Direct` kalır; FabricJob başlatılmaz. Power BI Workspace ID
`7fe125e9-705b-4d1a-ac37-eb0e22801e87`, Report ID
`36b69ded-e421-412b-b187-70caa70d4292` ve Semantic Model ID
`bdc46a8f-03cb-4bd8-a9c2-2dde70dd5432` olarak parametrelere geçirilmiştir.
Page identifier'ları `84686876a120a0b04881`, `033e576a7c43418160b7` ve
`637aeb7338de115d4c9c` yalnız deployment metadata'sıdır; Report ID ile
birleştirilmez. Onaylı read-only DWH sorgusu ve Power BI rapor erişimi/RLS smoke
deployment sonrasında yapılır.

DWH production contract'ı versionlanmış merkezi mapping üzerinden
`vw_sales → mart.vw_sales`, `vw_customer_rfm → mart.vw_customer_rfm` ve
`vw_payment → mart.vw_payment` üretir; `ConfigurationMappingRequired` çözülmüştür.
Mevcut report/use-case veya metric contract'ına açıkça bağlı özel execution
view'ları `mart.vw_sales_detail`, `mart.vw_monthly_sales`,
`mart.vw_sales_by_region`, `mart.vw_sales_by_category`,
`mart.vw_customer_rfm_segmented` ve `mart.vw_payment_summary` ile sınırlıdır.
`dwh.dim_customer`, `dwh.dim_product`, `dwh.dim_date` ve `dwh.fact_sales` base
tabloları genel NL2SQL/Query Builder veya execution allowlist'ine eklenmez.
SELECT-only guardrail korunur; unqualified production adları reddedilir. Bu adım
Fabric üzerinde view, tablo, schema veya permission değiştirmez ve DWH/OLTP
sorgusu çalıştırmaz.

### 15. Bicep parameter ve output'larında secret olmadığını kontrol et

Parameter kopyası, compiled ARM template, deployment inputs/outputs ve CI logları
secret/connection string/token/access key/password açısından taranır. Yalnız
resource ID, ad, client ID, tenant ID, endpoint ve image digest gibi non-secret
değerler kalır. Log Analytics shared key yalnız runtime `listKeys` ifadesiyle
çözülür ve hiçbir output'a alınmaz. Çıktı: güvenlik sign-off'u.

## Gerçek deployment öncesi blocker'lar

1. `namePrefix=crm-analytics-pilot` ve pilot region `swedencentral` olarak
   parametreye geçirilmiştir; kalan quota/policy kararları deployment öncesi
   doğrulanmalıdır.
2. Gerekli provider'lar kayıtlıdır; provider registration blocker'ı yoktur.
3. ACR'deki API/Teams image'ları doğrulanmış, deployment referansları OCI index
   digest'lerine sabitlenmiş, mevcut API tag'i ve yeni benzersiz Teams manuel tag'i
   write-disabled yapılmıştır;
   image availability artık blocker değildir.
4. Runtime UAMI için ACR-scope `AcrPull`, Service Bus queue-scope Data
   Sender/Receiver ve Azure SQL schema-scope minimum CRUD izinleri tamamlandı.
5. Azure SQL logical server ve Standard S0 application database hazırdır;
   `crm-analytics-application-db` Key Vault secret'ı ve ilk altı migration
   tamamlandı; `20260809235416_AddSubmittedSemanticPlan` ayrı DBA onayı bekler.
6. Service Bus namespace/queue ve queue-scope runtime data rolleri tamamlanmıştır;
   managed identity smoke sonrasında local authentication'ı kapatma işi açıktır.
7. DWH, internal API key ve Teams client secret hazırdır. Teams/Bot App
   Registration ve service principal hazır, secret referans aktivasyonu açıktır.
   Container App system identity secret-scope rol atamaları app'ler henüz mevcut
   olmadığından deployment aşamasında oluşturulacaktır.
8. Deployment principal'ının managed workspace `listKeys` ve secret-scope role
   assignment yazma yetkileri onaylanmamıştır.
9. Entra API client ID/audience ile ayrı Teams/Bot client ID/tenant/app type
   hazırdır. `crm-analytics-teams-oauth` yalnız planlanan addır; OAuth connection,
   Teams callback/messaging URL'si ve Azure Bot henüz yoktur.
10. Fabric DWH endpoint/catalog ve Power BI workspace/report/model ID'leri
    hazırdır. Production SQL contract için versionlanmış MART mapping tamamlanmıştır;
    Power BI erişim/RLS smoke kanıtı halen eksiktir.
11. ACR, digest-pinned API/Teams image referansları, runtime UAMI, location,
    `namePrefix`, Azure SQL, Service Bus, Entra API, DWH ve Power BI değerleri
    gerçektir. Yalnız Teams Container App URL/messaging endpoint alanı ve
    kullanılmayan FabricJob kimlikleri placeholder'dır. QueryDwh ve QueryOltp enabled'dır.
12. Azure validate/what-if, deployment ve smoke testleri ayrı onay beklemektedir;
    ilk altı application DB migration'ı tamamlanmıştır; tracked artifact'taki
    `20260809235416_AddSubmittedSemanticPlan` için ayrı DBA onayı gerekir.

## Audit metadata release sonucu — 2026-08-05

Application audit uyumluluğu `application-audit-metadata-v1` ile tamamlandı.
`20260805100943_AddApplicationAuditMetadata` additive migration'ı, mevcut audit
tablosuna nullable `AuditMetadataJson` ve Azure SQL `ISJSON` constraint'i ekler;
backfill/UPDATE, DELETE, TRUNCATE veya DROP içermez. Metadata conversation ID,
source'a göre explicit SQL contract version, execution plan'dan doğrulanmış physical
object, parameter value içermeyen SHA-256 query-shape fingerprint, timeout/row limit
ve processing attempt/optional Service Bus delivery kimliğini taşır. Source mevcut
`DataSource` kolonunda tek kopya olarak kalır.

Migration aktif Entra operatör oturumuyla `CrmAnalytics` database'e uygulandı ve
ikinci idempotent koşu doğrulandı. Runtime UAMI'ye DDL/migration yetkisi verilmedi;
yalnız mevcut `crm` application DML grant'leri kaldı. Geçici firewall kuralı
kaldırıldı. Yeni API OCI index digest'i
`sha256:ad06f57fce2e226ee7f8a812cfd352394701438a703a34ae104b245656db6d6a`,
linux/amd64 manifest'i
`sha256:cf97b6a0d383b25742d113a06164fcf2f5cbb086a5c2057c17b22234e0214103`'tür.
Teams image değiştirilmedi ve Container Apps deployment yapılmadı.
