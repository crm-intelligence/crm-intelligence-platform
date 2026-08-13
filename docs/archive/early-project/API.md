# CRM Project

## Proje Hakkında

Bu proje, CRM verilerini güvenli bir şekilde işleyerek yapay zekâ destekli iş zekâsı (Business Intelligence - BI) çözümleri geliştirmek amacıyla hazırlanmıştır.

Proje kapsamında ASP.NET Core Web API ile geliştirilen servisler Azure üzerinde çalışacak şekilde tasarlanmış; güvenli yapılandırma yönetimi için Azure Key Vault, uygulama izleme için Azure Application Insights ve sürekli entegrasyon (CI) için GitHub Actions kullanılmıştır.

---

## Kullanılan Teknolojiler

- .NET 8
- ASP.NET Core Web API
- Azure App Service
- Azure Key Vault
- Azure Application Insights
- GitHub Actions
- Swagger (OpenAPI)
- Git
- Azure CLI

---

## Repository

Geliştirme süreci aşağıdaki branch yapısı üzerinden yürütülmektedir.

- `main`
- `devops`

---

## Azure Kaynakları

| Kaynak | Adı |
|---------|-----|
| Subscription | Azure for Students |
| Resource Group | crm-project-rg |
| Azure Key Vault | lokmancrmkv01 |
| Application Insights | crm-project-insights |

> **Not:** Hassas bilgiler (Connection String, API anahtarları ve diğer gizli veriler) Azure Key Vault üzerinde güvenli şekilde saklanmaktadır.

---

## API

Swagger arayüzü:

```
http://localhost:<port>/swagger
```

### Endpoint

```
POST /api/requests
```

### Header

```
X-Request-ID
```

### Örnek İstek

```json
{
  "requestId": "REQ-001",
  "userId": "lokman",
  "useCase": "SalesSummary",
  "parameters": {
    "year": 2026,
    "month": 7
  }
}
```

---

## Endpoint — kontrollü SQL üretimi

```
POST /api/reports
```

Doğal dil talebinden **guardrail'dan geçmiş, parametreli** bir sorgu üretir.
`Crm.Analytics.Sql` katmanını çağırır (bkz. `Crm.Analytics.Sql/ENTEGRASYON.md`).

> **Bu uç sorguyu ÇALIŞTIRMAZ.** Çalıştırma katmanı henüz yok; dönen şey onaylanmış
> sorgu ve onun görsel önerisi. Bu yüzden kabul durumu `Completed` değil `ReadyToRun`.

### `/api/requests`'ten farkı

| | `/api/requests` | `/api/reports` |
|---|---|---|
| Bölge (yetki) | İstemci **gövdede** gönderiyor | Yalnızca **token claim'inden** türetiliyor |
| Hedef tablo | İstemci gönderiyor (`targetTable`) | Allow-list kataloğu belirliyor |
| Rolsüz kullanıcı | Bölge kontrolünü **atlıyor** (fail-open) | `GR007` ile reddediliyor (fail-closed) |
| SQL | Üretilmiyor | Guardrail'ın 16 kontrolünden geçmiş parametreli SQL |

İki uç bilinçli olarak ayrı: birleştirmek, istemciden gelen bölge değerinin yetki
kararına sızmasına açık kapı bırakırdı.

### Örnek istek

```json
{
  "prompt": "2018 satış tutarını eyalete göre göster",
  "conversationId": "conv-8f2a"
}
```

Gövdede `region` **yok** — kapsam token'dan gelir. `userId` de yok: kimlik token'dan
okunur, istemcinin gönderdiği bir kullanıcı adına güvenilmez.

### Örnek yanıt (kabul, 200)

```json
{
  "requestId": "1f3c...",
  "conversationId": "conv-8f2a",
  "status": "ReadyToRun",
  "title": "Rapor sorgusu hazir — bar grafik",
  "reasonCode": null,
  "visual": {
    "type": "BarChart",
    "rationale": "Tek kategorik kirilim; kategoriler arasi karsilastirma bar ile okunur.",
    "metricCount": 1,
    "dimensionCount": 1,
    "hasTimeDimension": false
  },
  "query": {
    "sql": "SELECT TOP 5000 customer_state AS customer_state, SUM(price) AS item_sales FROM vw_sales WHERE ... ;",
    "appliedScopeFilter": "vw_sales.customer_state IN (@scope0)",
    "parameterNames": ["@f0", "@f1", "@scope0"],
    "commandTimeoutSeconds": 30,
    "verifiedCheckCount": 16
  },
  "suggestions": []
}
```

**Parametre adları var, değerleri yok.** Değerler kullanıcı verisidir; guardrail'ın PII
kontrollerini uygularken aynı veriyi yanıtta dışa vermek çelişkili olurdu. Sorgu metninde
de hiçbir literal bulunmaz — tarih de eyalet kodu da parametredir.

### Durum eşlemesi

| Karar | HTTP | `status` | `query` |
|---|---|---|---|
| Kabul | 200 | `ReadyToRun` | dolu |
| Netleştirme | 200 | `NeedsClarification` | `null` — hata değil, kullanıcıdan bilgi isteniyor |
| Ret | 403 | `Rejected` | `null` — güvenlik/yetki kararı |

Netleştirmede `suggestions` dolu gelir. Öneriler çözümlenemeyen terimlerden üretilir;
katalogda olmayan bir metrik **önerilmez** — kullanıcıyı var olmayan bir rapora
yönlendirmek olurdu.

Ret ve netleştirme mesajları **şema bilgisi içermez**: görünüm veya kolon adı verilmez,
çünkü ret mesajı şema keşif aracı olmamalı.

---

## Request ID Correlation

Her API isteği benzersiz bir **Request ID** ile işaretlenmektedir.

Bu sayede aşağıdaki loglar aynı istek altında takip edilebilmektedir:

- İstek başladı
- Canonical talep alındı
- İstek tamamlandı

Bu loglar Azure Application Insights üzerinden izlenebilmektedir.

---

## Application Insights

Loglar Azure Portal üzerinden görüntülenebilir.

**Azure Portal**

```
Application Insights
    ↓
crm-project-insights
    ↓
Günlükler (Logs)
```

### Örnek KQL Sorgusu

```kusto
traces
| where timestamp > ago(30m)
| order by timestamp desc
```

---

## Continuous Integration (CI)

Projede GitHub Actions kullanılmaktadır.

CI Pipeline aşağıdaki adımları otomatik olarak çalıştırmaktadır:

- Restore
- Build
- Unit Test

Kod değişiklikleri GitHub'a gönderildiğinde bu işlemler otomatik olarak tetiklenmektedir.

---

## Projeyi Çalıştırma

Repository'yi klonlayın.

```bash
git clone https://github.com/lokmannonal/crm-project.git
```

Proje dizinine geçin.

```bash
cd crm-project
```

Bağımlılıkları yükleyin.

```bash
dotnet restore
```

Projeyi çalıştırın.

```bash
dotnet run
```

Swagger arayüzüne erişin.

```
http://localhost:<port>/swagger
```

---

## Ekip İçin Notlar

- Azure kaynakları ekip tarafından ortak kullanılmaktadır.
- Yeni gizli bilgiler (secret) Azure Key Vault üzerinden yönetilmelidir.
- Hassas bilgiler hiçbir zaman source code içerisine eklenmemelidir.
- Tüm geliştirmeler **devops** branch'i üzerinden yapılmalıdır.

---

## Geliştirici

**InternCamp 2026**

**Rol:** DevOps

**Geliştirici:** Lokman Önal