# CRM Analytics sistem mimarisi

Bu belge, repository'deki çalışan kodun **as-built** görünümünü özetler. Sistem iki
ayrı uygulamadan oluşur: dışarıya açık Microsoft Teams host'u ve iç ağda çalışan
API/worker uygulaması. Rapor üretimi, kullanıcı HTTP isteği açık tutulmadan
asenkron olarak gerçekleştirilir.

## 1. Üst seviye mimari

```mermaid
flowchart LR
    user[Teams kullanıcısı]
    entra[Microsoft Entra ID<br/>SSO ve JWT]

    subgraph aca[Azure Container Apps ortamı]
        direction LR

        subgraph teamsApp[CrmAnalytics.Teams<br/>Public ingress]
            teamsEndpoint[Teams activity endpoint<br/>Mesaj / sign-in / card action]
            teamsNotify[Proactive notification endpoint]
            teamsClient[Backend API client]
        end

        subgraph apiApp[CrmAnalytics.Api<br/>Internal ingress]
            api[REST API<br/>Auth + authorization + ownership]
            app[Application katmanı<br/>Talep ve iş akışı servisleri]
            outboxWorker[Outbox dispatcher<br/>Hosted service]
            queueWorker[Service Bus consumer<br/>Hosted service]
            processor[Report processing orchestration]
        end
    end

    db[(CRM Analytics SQL DB<br/>Requests, conversations, scopes,<br/>audit, outbox, Teams state)]
    bus[[Azure Service Bus<br/>crm-report-processing]]
    sqlGen[Crm.Analytics.Sql<br/>SQL planı + guardrail]
    dwh[(DWH<br/>Read-only)]
    oltp[(OLTP<br/>Read-only, varsayılan kapalı)]
    fabric[Microsoft Fabric job<br/>Analytics provider]
    pbi[Power BI<br/>Report / semantic model]

    user -->|Prompt veya kart aksiyonu| teamsEndpoint
    teamsEndpoint <-->|Delegated SSO| entra
    teamsEndpoint --> teamsClient
    teamsClient -->|Bearer token ile REST| api
    api -->|JWT doğrulama| entra
    api --> app
    app <-->|Kısa transaction| db

    outboxWorker -->|Pending processing mesajı| db
    outboxWorker -->|Publish| bus
    bus -->|PeekLock / retry / DLQ| queueWorker
    queueWorker --> processor
    processor <-->|Request, durum, audit| db
    processor -->|Prompt + güvenli veri kapsamı| sqlGen
    sqlGen -->|SqlExecutionPlan| processor
    processor -->|Plan source = DWH| dwh
    processor -.->|Plan source = OLTP| oltp
    processor -->|Sınırlanmış sorgu sonucu| fabric
    processor -->|Rapor bağlantısı üret| pbi

    processor -->|Terminal durum + notification outbox| db
    outboxWorker -->|API key ile callback| teamsNotify
    teamsNotify -->|Proactive Adaptive Card| user

    classDef app fill:#e8f1ff,stroke:#2563eb,color:#111827;
    classDef data fill:#fff7db,stroke:#d97706,color:#111827;
    classDef external fill:#f3e8ff,stroke:#7e22ce,color:#111827;
    class teamsEndpoint,teamsNotify,teamsClient,api,app,outboxWorker,queueWorker,processor app;
    class db,bus,dwh,oltp data;
    class entra,sqlGen,fabric,pbi external;
```

### Deployment sınırı

- `CrmAnalytics.Teams`: Teams/Bot trafiğini alan, public ingress'li ayrı Container
  App'tir.
- `CrmAnalytics.Api`: REST API ile birlikte outbox dispatcher ve Service Bus
  consumer'ını aynı process içinde çalıştıran internal ingress'li Container
  App'tir. Repository'de ayrı bir worker executable/deployment yoktur.
- SQL DB, Service Bus, Entra/Bot, DWH/OLTP, Power BI ve Fabric Bicep tarafından
  oluşturulmaz; mevcut dış kaynaklar olarak beklenir.

## 2. Bir rapor talebi nasıl çalışır?

```mermaid
sequenceDiagram
    autonumber
    actor U as Teams kullanıcısı
    participant T as CrmAnalytics.Teams
    participant E as Entra ID
    participant A as CrmAnalytics.Api
    participant D as SQL DB + Outbox
    participant B as Azure Service Bus
    participant W as API içindeki worker
    participant S as Crm.Analytics.Sql
    participant Q as DWH veya OLTP
    participant F as Analytics (Direct/Fabric)
    participant P as Power BI

    U->>T: Doğal dilde rapor talebi
    T->>E: Teams SSO token al
    E-->>T: Delegated access token
    T->>A: POST /api/report-requests + Bearer token
    A->>A: JWT, scope, rol, ownership ve data-scope kontrolü
    A->>D: Request + conversation + audit + processing outbox
    D-->>A: Transaction commit
    A-->>T: 202 Accepted + requestId
    T->>A: Request-conversation target kaydı
    T-->>U: Talep alındı

    Note over U,T: HTTP kullanıcı akışı burada biter; işleme arka planda devam eder

    W->>D: Processing outbox kaydını claim et
    W->>B: ReportProcessingRequested publish
    B-->>W: Mesajı PeekLock ile teslim et
    W->>D: Request'i yükle, güncel data-scope'u çöz
    W->>D: Durum = Validating
    W->>S: Prompt + scope + conversation context

    alt Ek bilgi gerekiyor
        S-->>W: NeedsClarification + güvenli soru
        W->>D: WaitingForClarification + audit + notification outbox
    else Politika reddi
        S-->>W: Rejected
        W->>D: Rejected + audit + notification outbox
    else Plan kabul edildi
        S-->>W: SQL + typed parameters + source + limitler
        W->>Q: Yalnız seçilen kaynağa read-only sorgu
        Q-->>W: Boyutu sınırlandırılmış sonuç
        W->>D: Durum = Processing
        W->>F: Analiz et
        F-->>W: Özet + result reference
        W->>P: Rapor bağlantısını üret/doğrula
        P-->>W: Güvenli Power BI web URL
        W->>D: Completed + audit + notification outbox
    end

    W->>D: Notification outbox kaydını claim et
    W->>T: Internal callback (API key)
    T-->>U: Proactive Adaptive Card
    W->>D: Delivery = Delivered
```

İlk API cevabındaki `202 Accepted`, raporun hazır olduğunu değil; talep ile işleme
niyetinin aynı transaction içinde kalıcı olarak kaydedildiğini gösterir.

## 3. Rapor durum modeli

```mermaid
stateDiagram-v2
    [*] --> Received
    Received --> Validating: Worker başladı
    Validating --> Processing: SQL planı ve sorgu başarılı
    Validating --> WaitingForClarification: Ek bilgi gerekli
    Validating --> Rejected: Guardrail/policy reddi
    Validating --> Failed: Teknik hata
    Processing --> Completed: Analytics + rapor başarılı
    Processing --> WaitingForClarification: Ek bilgi gerekli
    Processing --> Rejected: Kontrollü ret
    Processing --> Failed: Teknik hata
    WaitingForClarification --> Validating: Kullanıcı cevap verdi
    WaitingForClarification --> Processing: Domain'in izin verdiği devam yolu
    WaitingForClarification --> Failed
    Completed --> [*]
    Rejected --> [*]
    Failed --> [*]
```

`Queued` ve `Running` durumları domain sözleşmesinde bulunur; mevcut worker akışı
bu iki durumu set etmez. Fiili normal yol şöyledir:

`Received -> Validating -> Processing -> Completed`

## 4. Kod katmanları

```mermaid
flowchart TB
    hosts[Host katmanı<br/>CrmAnalytics.Teams / CrmAnalytics.Api]
    contracts[CrmAnalytics.Contracts<br/>HTTP ve entegrasyon DTO'ları]
    application[CrmAnalytics.Application<br/>Use-case, authorization, orchestration, port'lar]
    domain[CrmAnalytics.Domain<br/>ReportRequest aggregate ve durum kuralları]
    infrastructure[CrmAnalytics.Infrastructure<br/>EF Core, SQL, Service Bus, outbox,<br/>Teams state, provider adapter'ları]

    hosts --> application
    hosts --> contracts
    infrastructure --> application
    infrastructure --> domain
    infrastructure --> contracts
    application --> domain
    application --> contracts
```

Bağımlılık yönü business kurallarını dış sistemlerden ayırır: Application katmanı
arayüzleri tanımlar, Infrastructure bu arayüzleri SQL Server, Service Bus, Fabric
ve Power BI adapter'larıyla gerçekleştirir.

## 5. Sistemi okurken bilinmesi gereken sınırlar

- Transactional outbox, request/durum değişikliği ile mesaj niyetini atomik tutar;
  teslimat semantiği **at-least-once**'dur, global exactly-once değildir.
- Azure Service Bus mesajında prompt, token, SQL veya sonuç satırları taşınmaz;
  worker request'i ve güncel yetki kapsamını DB'den yeniden yükler.
- DWH varsayılan sorgu kaynağıdır. OLTP varsayılan olarak kapalıdır ve otomatik
  DWH/OLTP fallback yoktur.
- `FabricJob` adapter kodu bulunsa da kalıcı sonuç staging contract'ı mevcut
  değildir; gerçek sorgu sonucu ile Fabric aktivasyonu fail-closed olur.
- Power BI linki veri yetkisi sağlamaz; erişim Entra/Power BI/RLS tarafında ayrıca
  uygulanmalıdır.

Daha ayrıntılı route, güvenlik, persistence ve operasyon analizi için
[`AS_BUILT_ARCHITECTURE_2026-08-02.md`](../archive/architecture/AS_BUILT_ARCHITECTURE_2026-08-02.md) tarihsel belgesine bakın.
