# Claude Code Prompt — Yapay Zekâ Mühendisi (AI Engineer)

Bu dosya iki bölümden oluşur:

1. **Ana prompt** — projeye ilk kez başlarken Claude Code'a verilecek tam brief
2. **Günlük prompt'lar** — sprint günlerine göre kullanılacak kısa görev prompt'ları

Ana prompt'u `CLAUDE.md` olarak repo köküne koymak, her oturumda tekrar yazmaktan daha iyidir.

---

## BÖLÜM 1 — ANA PROMPT

> Aşağıdaki metnin tamamını kopyalayıp Claude Code'a ver (veya `CLAUDE.md` olarak kaydet).

---

### Proje Bağlamı

Kurumsal bir CRM analitik platformu geliştiriyoruz. İş kullanıcıları Microsoft Teams üzerinden doğal dilde rapor talep ediyor. Talep bir ASP.NET Core API'ye gidiyor, orada SQL'e dönüştürülüyor, read-only olarak DWH veya OLTP üzerinde çalıştırılıyor, sonuç Teams özeti ve Power BI raporu olarak sunuluyor.

Ben bu projede **AI Engineer** rolündeyim ve **yalnızca SQL üretim ve güvenlik katmanından** sorumluyum. Senden bu katmanı geliştirmemde yardım etmeni istiyorum.

### Teknoloji

- .NET 8 / C#
- Sınıf kütüphanesi: `Crm.Analytics.Sql`
- SQL AST parse: `Microsoft.SqlServer.TransactSql.ScriptDom`
- Test: xUnit + FluentAssertions
- Hedef veritabanı: Azure SQL / Fabric Warehouse (T-SQL)
- LLM çağrıları için `IChatCompletionClient` adında bir arayüz tanımla; gerçek implementasyonu şimdilik gerekmez, testlerde fake kullan

### Mimari Kural: AI önerir, kurallar karar verir

Dil modeli SQL taslağı üretebilir. Ancak bir sorgunun çalıştırılabilir olup olmadığına **yalnızca deterministik guardrail** karar verir. Guardrail'ı atlayan hiçbir kod yolu olmayacak — test amaçlı bile.

### Kurulacak Bileşenler

```
Crm.Analytics.Sql/
  Contracts/
    CanonicalRequest.cs         // metrics, dimensions, filters, dateRange, grain, scenarioKey, confidence
    SqlGenerationResult.cs      // decision, sql, parameters, source, reasonCode, checks[]
    UserDataScope.cs            // company, regions[], storeIds[]
    GuardrailDecision.cs        // Accepted | Rejected | NeedsClarification
    ReasonCode.cs               // GR001..GR010, CL001..CL002
  Catalog/
    MetricCatalog.cs            // metric key -> label, SQL expression, source, aliases
    AllowList.cs                // izinli obje/kolon/join yolu, deniedColumns, maxJoins, maxRows, timeout
    CatalogLoader.cs            // JSON'dan yükleme + doğrulama
  Parsing/
    RequestParser.cs            // doğal dil -> CanonicalRequest (LLM + catalog eşleme)
    FollowUpMerger.cs           // previousRequest + yeni talep -> delta uygulanmış CanonicalRequest
  Generation/
    QueryBuilder.cs             // CanonicalRequest -> parametreli SELECT (deterministik)
    ScenarioTemplates.cs        // sales_by_region, sales_trend, customer_segments, campaign_performance
    Nl2SqlGenerator.cs          // guarded NL2SQL, JSON çıktı sözleşmesi
    QueryRouter.cs              // QueryBuilder | NL2SQL | NeedsClarification | Rejected kararı
  Guardrail/
    SqlGuardrail.cs             // 12 kontrolü sırayla çalıştıran pipeline
    Checks/                     // her kontrol ayrı sınıf, IGuardrailCheck implementasyonu
    ScopeFilterInjector.cs      // veri kapsamı filtresini AST üzerinde ZORLA ekler
  Results/
    ResultShapeClassifier.cs    // tek değer | tarih+metric | kategori+metric | çok boyut | detay
  Audit/
    DecisionAuditWriter.cs      // requestId ile karar, gerekçe ve SQL kaydı
```

### Guardrail — Kontroller bu sırayla çalışacak

1. `SingleStatement` — `;` ile ayrılmış çoklu ifade yok
2. `ParseToAst` — ScriptDom ile parse edilebiliyor. **Regex ile kontrol yapma.**
3. `SelectOnly` — AST kökü SELECT; DML, DDL, `EXEC`, `sp_`, `xp_`, dinamik SQL yok
4. `NoStarSelect` — `SELECT *` yok
5. `AllowListObjects` — tüm FROM/JOIN hedefleri allow-list'te (alt sorgular ve CTE'ler dahil)
6. `AllowListColumns` — referans edilen tüm kolonlar allow-list'te
7. `NoDeniedPiiColumns` — `deniedColumns` listesinden hiçbiri seçilmemiş (takma ad arkasına saklanmış olsa bile)
8. `JoinPathAllowed` — JOIN yolları tanımlı, sayı `maxJoins`'i aşmıyor
9. `MandatoryScopeFilter` — **kullanıcının veri kapsamı filtresini AST üzerinde zorla ekle.** Filtrenin var olup olmadığını kontrol etmekle yetinme; enjekte et. `UNION`'ın her kolunda ve her alt sorguda uygulanmalı.
10. `Parameterized` — literal değer yok, tüm değerler `SqlParameter`
11. `RowLimit` — `TOP (@maxRows)` uygulanmış
12. `Timeout` — komut timeout'u atanmış

Her kontrol başarısız olduğunda işlem durur ve bir `ReasonCode` döner. Ret gerekçeleri kullanıcıya gösterilecek şekilde açıklanabilir olmalı ama şema detayı sızdırmamalı.

### Kırmızı Çizgiler

- String birleştirmeyle **asla** SQL kurma. Her değer parametre olacak.
- Tüm kurumsal şemayı **asla** LLM prompt'una koyma. Yalnızca allow-list'teki objeler ve kolonlar.
- Kullanıcının veri kapsamı değerlerini (bölge, mağaza) prompt'a koyma. Bunlar SQL'e guardrail tarafından eklenir.
- NL2SQL çıktısını doğrudan çalıştırmaya izin veren bir yol bırakma.
- Guardrail'da `IsEnabled` / `SkipChecks` / `bypass` gibi bir bayrak oluşturma.
- Secret, bağlantı dizesi veya gerçek veri satırı hiçbir yere yazma.

### Çalışma Şeklin

1. Kod yazmadan önce **kısa bir plan** sun ve onayımı bekle. Plan 10 satırı geçmesin.
2. Test-first çalış: her guardrail kontrolü için önce başarısız testi yaz, sonra implementasyonu.
3. Küçük adımlarla ilerle. Her adımda `dotnet test` çalıştır ve sonucu bana göster.
4. Yalnızca `Crm.Analytics.Sql` ve `Crm.Analytics.Sql.Tests` içinde dosya oluştur veya değiştir. Diğer projelere dokunma; ihtiyaç varsa bana söyle.
5. Bir şey belirsizse **varsayım üretme, sor.** Özellikle şema, KPI tanımı ve yetki modeli konusunda.
6. Türkçe konuş; kod, sınıf ve değişken adları İngilizce olsun. Yorumları kısa tut.
7. Bir kontrolü tam yapamıyorsan yarım bırakıp "TODO" yazma; bana neyin eksik olduğunu söyle.

### İlk Görev

`Guardrail/` klasörünü kur. Sırayla:

1. `IGuardrailCheck` arayüzü ve `SqlGuardrail` pipeline'ı (kontroller sırayla, ilk hatada dur)
2. `SingleStatement`, `ParseToAst`, `SelectOnly`, `NoStarSelect` kontrolleri
3. Bu dördü için şu SQL'leri reddeden testler:
   - `DELETE FROM vw_sales`
   - `SELECT 1; DROP TABLE vw_sales`
   - `SELECT * FROM vw_sales`
   - `EXEC sp_executesql N'SELECT 1'`
   - `SELECT net_amount FROM vw_sales WHERE region = 'Ege' -- AND scope`
4. Ve şunu kabul eden test: `SELECT TOP (@maxRows) region, SUM(net_amount) AS net_sales FROM vw_sales WHERE order_date BETWEEN @from AND @to AND region IN (SELECT value FROM STRING_SPLIT(@scopeRegions, ',')) GROUP BY region`

Planını sun, onaylayınca başla.

---

## BÖLÜM 2 — GÜNLÜK PROMPT'LAR

Ana prompt `CLAUDE.md` olarak repoda duruyorsa, günlük çalışmada yalnızca aşağıdaki kısa prompt'lar yeterlidir.

### Gün 2 — Katalog ve allow-list

> `Catalog/` klasörünü kur. `MetricCatalog` ve `AllowList` sınıflarını, bunları JSON'dan yükleyen `CatalogLoader`'ı ve yükleme sırasında doğrulama yapan kontrolleri yaz. Doğrulama: her metric'in `source`'u allow-list'te tanımlı bir obje olmalı, her metric ifadesinde kullanılan kolon allow-list'te olmalı, `deniedColumns` ile `columns` kesişmemeli. Geçersiz katalog yüklenmeye çalışıldığında anlamlı bir exception atsın. Testleriyle birlikte.

### Gün 3 — Query Builder

> `Generation/QueryBuilder.cs` ve `ScenarioTemplates.cs`'i yaz. `sales_by_region` senaryosu için `CanonicalRequest` girdisinden parametreli T-SQL üret. Kurallar: metric ifadeleri katalogdan gelir, kolon adları allow-list'ten doğrulanır, tarih aralığı `@from` / `@to` parametresi olur, `TOP (@maxRows)` her zaman eklenir, `GROUP BY` dimension listesinden türetilir. String birleştirme yok — SQL'i ScriptDom AST'i kurarak veya katı şablon + parametre ile üret. Testler: 3 farklı canonical request için beklenen SQL'i doğrula.

### Gün 4 — Guardrail'ın tamamı

> Guardrail'ın kalan kontrollerini tamamla: `AllowListObjects`, `AllowListColumns`, `NoDeniedPiiColumns`, `JoinPathAllowed`, `MandatoryScopeFilter`, `Parameterized`, `RowLimit`, `Timeout`. `ScopeFilterInjector`'ı yaz: kullanıcının veri kapsamı filtresini AST üzerinde her SELECT bloğuna, her `UNION` koluna ve her alt sorguya zorla ekle. Sonra şu negatif senaryolar için test yaz: allow-list dışı tablo, allow-list dışı kolon, `SELECT email AS x FROM vw_customer`, tanımsız JOIN yolu, `UNION SELECT ... FROM dbo.users`, alt sorgu içinde yasaklı tablo, scope filtresi olmayan sorgu (bu düzeltilmeli, reddedilmemeli). Her ret doğru `ReasonCode`'u döndürmeli.

### Gün 5 — Router ve entegrasyon

> `QueryRouter.cs`'i yaz: canonical request bir `scenarioKey` ile eşleşiyorsa Query Builder'a, eşleşmiyor ama allow-list kapsamındaysa NL2SQL'e, belirsizse `NeedsClarification`'a, kapsam dışıysa `Rejected`'a yönlendir. Karar mantığını `confidence` eşiği ve `unresolvedTerms` üzerinden kur. Sonra `ResultShapeClassifier`'ın ilk halini yaz. Router'ın her dalı için test.

### Gün 6 — Guarded NL2SQL

> `Nl2SqlGenerator.cs`'i yaz. Prompt'u allow-list metadata'sından **dinamik** kur — hardcode etme. Prompt'a girmeyecekler: gerçek veri, kullanıcı kimliği, veri kapsamı değerleri, allow-list dışı şema. Model çıktısı yalnızca şu JSON olacak: `{ sql, parameters, confidence, usedObjects, notes }`. Markdown fence veya ön söz gelirse temizle; parse edilemezse `NeedsClarification` döndür. Üretilen SQL **her zaman** guardrail'dan geçecek. `IChatCompletionClient` için fake ile test yaz: kötü niyetli model çıktısı (allow-list dışı tablo, DELETE, prompt injection'a uymuş çıktı) guardrail tarafından reddedilmeli.

### Gün 8 — Audit ve açıklanabilirlik

> `Audit/DecisionAuditWriter.cs`'i yaz. Her karar için şunları kaydet: requestId, conversationId, previousRequestId, kullanıcı kimliği, etkin veri kapsamı, ham prompt, canonical request, üretim yolu (QueryBuilder/NL2SQL), üretilen SQL, guardrail kararı ve gerekçe kodu, seçilen kaynak, süre, dönen satır sayısı. Kişisel veri ve secret loglanmayacak. Log kaydından bir kararın **neden** verildiğinin okunabildiğini gösteren bir test yaz.

### Gün 9 — Güvenlik test paketi

> Negatif test paketini 15+ senaryoya çıkar. Ekle: yorum enjeksiyonuyla filtre atlatma, `STRING_SPLIT` üzerinden kapsam kaçırma, CTE içinde yasaklı tablo, `OPENROWSET`, çok geniş tarih aralığıyla satır limiti aşımı, kullanıcının yetkili olmadığı bölge talebi, prompt injection'ın NL2SQL'e sızması. Tüm paketi tek bir test suite'inde topla ve `dotnet test` çıktısını rapor formatında özetle: hangi senaryo, hangi ReasonCode, geçti/geçmedi.

### Gün 10 — Dokümantasyon

> `docs/ai-engineer-teslim.md` dosyasını oluştur. İçerik: bileşen listesi ve sorumlulukları, guardrail kontrol sırası, ret gerekçe kodları tablosu, desteklenen soru tipleri, desteklenmeyen soru tipleri ve nedenleri, test kanıtı özeti, bilinen kısıtlar. Kod içinden gerçek bilgiyi çek, uydurma.

---

## Prompt Yazarken Dikkat

- **Her oturumda bağlam ver.** Claude Code oturumlar arası hafızaya sahip değil; `CLAUDE.md` bunu çözer.
- **Tek oturumda tek bileşen.** Guardrail ve NL2SQL'i aynı oturumda isteme; ikisi de dikkat gerektiriyor.
- **Testi sen tanımla.** "Test de yaz" demek yerine hangi senaryonun reddedilmesi gerektiğini açıkça listele. Güvenlik kodunda testin kapsamını modele bırakmak risklidir.
- **Planı onaylamadan koda geçmesine izin verme.** Özellikle guardrail'da mimari kararı önce netleşmeli.
- **Üretilen guardrail kodunu satır satır oku.** Bu katman bu projede en kritik parçadır ve gözden geçirilmeden kabul edilmemelidir.
