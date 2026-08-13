# SQL Üretim Servisi — Entegrasyon Sözleşmesi

## Hybrid deterministic + Ollama canonical planning

The backend always runs `ISqlProductionService.Produce` first. `Accepted` is used
unchanged and a safe `Rejected` result is preserved without calling a model. Only
`NeedsClarification` with `CL001` or `CL002` may invoke the optional
`IOllamaStructuredPlanningClient`.

Ollama receives `stream=false`, `think=false`, `temperature=0` and a JSON Schema
object in `format`. The schema is derived from the real `CanonicalRequest` resource
and the embedded DWH/OLTP metric catalogs. It sets `additionalProperties=false`,
enumerates usable metric and dimension keys, constrains source and limit, and has
no SQL, table, view, column or raw-query field. The safe catalog prompt contains
logical keys, labels and aliases only; it is not a second hard-coded catalog and
does not expose physical schema metadata.

A returned document is accepted only after HTTP/envelope checks, SQL-pattern
defense-in-depth, strict JSON/schema validation, unknown-member-rejecting
`CanonicalRequestSerializer` deserialization, the configured confidence threshold,
source/catalog compatibility, `AmbiguityGate`, deterministic Query Builder and all
existing guardrails. Data scope continues to arrive independently from the backend
and is injected only in the established guardrail layer. Model confidence by itself
cannot bypass any check.

Clarification resumes send the original request, clarification-question meaning and
the single answer as separate chat messages. Raw user text, full clarification
answers and raw model responses are never written to structured planning logs.
Provider/model, duration, outcome, fallback, safe reason code, validation flags,
done reason and token counts are logged when available.

If Ollama is disabled, unavailable, times out, returns non-JSON/unknown fields, or
fails canonical validation, the original deterministic clarification is returned;
the report is not converted into a technical failure. The model never generates
SQL. Query Builder and the repository allow-list remain the security boundary.

**Kime:** Backend Geliştirici, Fabric/BI Geliştiricisi

## Source-aware DWH/OLTP production contract (2026-08-05)

`CreateForOlist` now loads two validated immutable contexts: DWH contract
`fabric-dwh-object-mapping-v1` and OLTP contract
`fabric-oltp-operational-orders-v1`. Source is determined before parsing output
is sent to the deterministic Query Builder and guardrail. Exactly one complete catalog match is
required when source is omitted; both or neither return the existing
clarification decision. Explicit source never falls back.

Canonical request JSON carries nullable `source` for old-payload compatibility;
null is not a DWH default. New canonical payloads record the selected source and
revision preserves it unless a complete request is successfully reparsed against
an explicitly different source. No component accepts model-authored SQL.

OLTP is limited to `dbo.vw_operational_orders`, default/max TOP 100/1000 and a
15-second timeout. It provides order-detail dimensions only, no business metric.
See [Catalog/OLTP_QUERY_CONTRACT_V1.md](Catalog/OLTP_QUERY_CONTRACT_V1.md).
**Kimden:** AI Engineer (`Crm.Analytics.Sql`)
**Durum:** Kod hazır ve **API'ye bağlandı**, 497 test geçiyor (414 birim + 68 entegrasyon +
15 API). Üretilen SQL yerel SQLite fixture'ında gerçek veri üzerinde koşuyor; **SQL Server
tarafında görünüm katmanı hâlâ bekleniyor** (bkz. §10).

---

## 1. Tek giriş noktası

```csharp
public interface ISqlProductionService
{
    SqlProductionResponse Produce(SqlProductionRequest request);
}
```

Tek metod olması bilinçli. "Önce ayrıştır, sonra guardrail'i çağır" gibi ayrı adımlar
verilse, adımlardan biri atlanabilir hale gelirdi. Zincirin tamamı bu arayüzün arkasında:

```
metin → runtime semantic catalog → strict Canonical Request → deterministic Query Builder → guardrail → sonuç
```

**Guardrail'ı atlayan bir kod yolu yoktur** — test amaçlı bile bırakılmadı.

## 2. Kayıt (Program.cs)

> **Bu kayıt artık yapılmış durumda:** `crm-project/Program.cs`. Aşağıdaki bölüm sözleşmenin
> kendisi olarak duruyor; çalışan hâli için o dosyaya bakın.

Kütüphane `IServiceCollection` uzantısı sunmuyor: bu, kütüphaneyi belirli bir DI
kapsayıcısına bağlardı ve hangi kapsayıcıyı kullandığınız benim kararım değil. Fabrika düz
bir nesne döndürür:

```csharp
builder.Services.AddSingleton<IDecisionAuditWriter, LoggingDecisionAuditWriter>();

builder.Services.AddSingleton<ISqlProductionService>(provider =>
    SqlProductionFactory.CreateForOlist(
        provider.GetRequiredService<IDecisionAuditWriter>()));
```

Uygulamada `LoggingDecisionAuditWriter` yerine `SqlDecisionAuditWriter` kayıtlı: o adaptör
kütüphanenin zengin yapılandırılmış kaydını **ve** uygulamanın tek satırlık `AUDIT | ...`
formatını birlikte yazıyor (`API.md`'deki KQL sorguları ikinci formata bağlı).

Servis **durumsuz ve thread-safe**; singleton olarak kaydedilebilir. Katalog ve allow-list
kurulum anında doğrulanır — hatalı bir katalog satırı ilk istekte değil **uygulama
açılışında** hata verir.

Ayarlar (`SqlProductionOptions`): `ConfidenceThreshold` (varsayılan 0.60),
`SqlVersionName` (varsayılan `"Sql150"` = SQL Server 2019). Tanınmayan sürüm adı sessizce
varsayılana düşmez, hata verir.

## 3. İstek

| Alan | Zorunlu | Not |
|---|---|---|
| `Prompt` | ✔ | Kullanıcının serbest metni |
| `RequestId` | ✔ | Audit ve durum sorgusu buna bağlanır |
| `ConversationId` | ✔ | |
| `Scope` | ✔ | **Kullanıcının veri kapsamı — sizden gelir** (§4) |
| `Today` | ✔ | Görece tarih ifadelerinin referans günü (§5) |
| `PreviousRequest` | — | Takip sorusuysa önceki `CanonicalRequest` (§7) |
| `UserId` | — | Audit'te "kim sordu" |
| `Source` | — | `Dwh` (varsayılan) / `Oltp` |

## 4. Veri kapsamı — sizin sorumluluğunuz

```csharp
UserDataScope.ForRegions("SP", "RJ")   // kullanıcı bu eyaletleri görebilir
UserDataScope.Unrestricted             // kısıt yok (yönetici)
UserDataScope.Unresolved               // ÇÖZÜMLENEMEDİ
```

`Unresolved` **sınırsız anlamına gelmez**, ret sebebidir (`GR007`). Kullanıcı → kapsam
eşlemesi Backend ve Veri Mühendisi'nde; bu servis kapsamı *üretmez*, aldığı kapsamı SQL'e
**zorla uygular**.

Kapsam filtresi kullanıcının SQL'inde var mı diye kontrol edilmez — guardrail filtreyi
AST'deki **her sorgu bloğuna** kendisi enjekte eder. Kullanıcı kendi filtresini yazsa bile
guardrail kendi filtresini ayrıca ekler.

**Kapsam değeri prompt'a asla girmez.** Dil modeline kullanıcının hangi bölgeleri
görebildiği söylenmez.

## 5. `Today` neden dışarıdan geliyor

"Geçen çeyrek" ifadesini sistem saatinden çözersem aynı istek yarın farklı SQL üretir ve
audit kaydı yeniden üretilemez. Referans günü siz verirsiniz:

```csharp
Today = DateOnly.FromDateTime(timeProvider.GetUtcNow().DateTime)
```

⚠️ **Demo uyarısı:** Olist verisi 2018'de bitiyor. "Bu yıl" ifadesi 2026'yı çözer ve **boş
sonuç** döner. Demoda mutlak tarih ("2018 satışı", "Ocak 2018") kullanın.

## 6. Yanıt ve durum eşlemesi

| `Decision` | Sizin durumunuz (9.2) | Ne yapılır |
|---|---|---|
| `Accepted` | → çalıştır → `Completed` | `Sql` + `Parameters` ile çalıştır |
| `NeedsClarification` | `NeedsClarification` | `UserMessage`'ı göster |
| `Rejected` | `Rejected` | `UserMessage`'ı göster |

`Failed` durumunu bu servis üretmez — teknik hata çalıştırma katmanında oluşur.
Guardrail'ın **kendi** hatası `GR014` ile `Rejected` döner (fail-closed: guardrail
çökerse sorgu çalışmaz).

Dolu alanlar:

| Alan | Ne zaman | Not |
|---|---|---|
| `Sql` | yalnızca `Accepted` | Reddedilen sorgunun metni dışa verilmez |
| `Parameters` | `Accepted` | `SqlParameter` olarak **bağlanmalı**, metne gömülmemeli |
| `AppliedScopeFilter` | `Accepted` | Boş olamaz; kapsamın uygulandığının kanıtı |
| `CommandTimeoutSeconds` | `Accepted` | Sözleşme alanı — **uygulaması sizde** |
| `UserMessage` | ret/netleştirme | Şema bilgisi içermez (§9) |
| `ResultShape` | yalnızca `Accepted` | BI'a görsel önerisi (§8) |
| `CanonicalRequest` | parse başarılıysa | **Saklayın** — takip sorusunda geri verilecek |
| `Checks` | guardrail koştuysa | Koşulmamış kontrol "geçti" sayılmaz, boş döner |

### Parametreleri bağlama

```csharp
foreach (var spec in response.Parameters)
{
    var parameter = command.Parameters.Add(spec.Name, spec.Kind switch
    {
        FilterValueKind.Text    => spec.IsUnicode ? SqlDbType.NVarChar : SqlDbType.VarChar,
        FilterValueKind.Integer => SqlDbType.Int,
        FilterValueKind.Decimal => SqlDbType.Decimal,
        FilterValueKind.Boolean => SqlDbType.Bit,
        FilterValueKind.Date    => SqlDbType.Date,
        _ => throw new NotSupportedException($"Bilinmeyen tip: {spec.Kind}")
    });

    parameter.Value = spec.Raw;   // dönüşüm ADO.NET'e bırakılır
}
```

`switch` ifadesinde `default` dalının **hata atması** bilinçli: yeni bir tip eklendiğinde
sessizce yanlış bağlamak, collation'a bağlı olarak farklı satır kümesi döndürebilir.

## 7. Takip sorusu

Servis **durumsuz**: konuşma durumunu siz saklarsınız (09-backend-teams.md'deki
Conversation ID / Previous Request ID yönetimi). Önceki talebin *kimliğini* değil
**kendisini** geri verin:

```csharp
var second = service.Produce(new SqlProductionRequest
{
    Prompt = "sipariş sayısı",
    PreviousRequest = savedCanonicalRequest,   // ilk yanıttan
    // ...
});
```

Takip modunda metin **delta** olarak çözümlenir: belirtilmeyen alanlar korunur. "Sipariş
sayısı" der demez tarih aralığı ve kırılım aynı kalır. Aynı metin ilk talep olarak
gelseydi `CL002` alırdı (tarih yok) — takip sorusunda kullanıcı tarihi değiştirmediğini
kastediyor.

Hiçbir alan tanınmazsa netleştirme istenir; boş delta önceki raporu aynen tekrar üretir ve
"isteğin uygulandı" izlenimi verirdi.

### `CanonicalRequest`'i saklama — serializer sözleşmesi

Konuşma deposuna yazarken **kendi `JsonSerializerOptions`'ınızı kurmayın**:

```csharp
var json    = CanonicalRequestSerializer.Serialize(response.CanonicalRequest!);
var request = CanonicalRequestSerializer.Deserialize(json);
```

Ayarlar iki tarafta ayrı tanımlanırsa `single_value` ile `singleValue` gibi bir fark, geçerli
bir talebin sessizce farklı yorumlanmasına yol açar. Sözleşme: **alan adları camelCase**, **enum
değerleri snake_case**, tanımsız alan **hata** verir, null alanlar **yazılır** (şema `required`
alanın varlığını bekler).

`canonical_request.schema.json` ile modelin örtüşmesi `SchemaModelAlignmentTests` tarafından
doğrulanır: şemaya uygun bir JSON'un modele çözümlenebildiği, üretilen alan adlarının şemada
tanımlı olduğu ve enum kümelerinin birebir örtüştüğü test edilir. (Tam JSON Schema doğrulaması
yapılmıyor — bunun için bir şema doğrulama kütüphanesi gerekir; yapısal örtüşme kontrol ediliyor.)

### Tarih aralığının üç durumu

| `kind` | Anlamı |
|---|---|
| `relative` | Görece ifade (`last_quarter`); çözümlenmiş tarihler `from`/`to`'da da bulunur |
| `absolute` | Kesin aralık; `from`/`to` **zorunlu** |
| `not_applicable` | Kaynakta zaman boyutu **yok** (`vw_customer_rfm`); `from`/`to` null |

`not_applicable` ayrı bir durum: "belirtilmemiş aralık" ile "uygulanamaz aralık" farklıdır —
ilki zaman boyutu taşıyan bir kaynakta `CL002` sebebidir, ikincisi normal durumdur.

### Filtre sözleşmesi

`filters[].values` **her zaman dizi**, elemanları `{ "kind": "text", "raw": "electronics" }`.
Skaler ile dizi arasında şema dalı oluşturmak çözümlemeyi polimorfik ve kırılgan yapardı.
`kind` yalnızca fallback: Query Builder tipi **öncelikle** Metric Catalog'daki dimension
tanımından çözer, çünkü `'2026-04-01'` hem metin hem tarih olabilir ve JSON'dan tahmin etmek
collation'a bağlı olarak farklı satır kümesi döndürebilir.

## 8. BI için sonuç seti sözleşmesi

`ResultShape` bir **öneridir**, karar değil. Görsel seçimi BI'ın sorumluluğunda; her öneri
`Rationale` taşır ki "neden bar değil çizgi" tartışması her rapor için baştan yapılmasın.

| Talep | `SuggestedVisual` |
|---|---|
| Kırılımsız tek ölçüm | `KpiCard` |
| Tek kategorik kırılım | `BarChart` |
| Tek zaman kırılımı | `LineChart` |
| İki veya daha fazla kırılım | `Matrix` |
| Ölçümsüz | `Table` |

### Kolon adları ve sırası

Üretilen `SELECT` listesinin sırası **deterministiktir**: önce kırılımlar (talepteki
sırayla), sonra ölçümler (talepteki sırayla). Kolon takma adları:

- **Kırılım:** boyut anahtarı (`customer_state`, `product_category`)
- **Zaman kırılımı:** boyut anahtarı — değer `YEAR()`/`MONTH()`/`DATEPART(quarter, …)`
  sonucudur, yani **tam sayı** (tarih değil)
- **Ölçüm:** metrik anahtarı (`item_sales`, `order_count`)

Tip ve biçim bilgisi Metric Catalog'da: `unit` (`BRL`, `count`, `days`) ve `format`
(`#,##0.00`). Semantic model bu değerleri kaynak alsın — ikinci bir yerde tanımlamak iki
gerçek yaratır.

⚠️ **Zaman kırılımı yalnızca yıl/çeyrek/ay.** Gün `CAST` gerektiriyor (izinli fonksiyon
listesinde yok), hafta `DATEPART(week, …)` sunucunun `DATEFIRST` ayarına bağlı olduğu için
**deterministik değil** — aynı sorgu farklı ortamlarda farklı sonuç verirdi.

⚠️ **`ORDER BY` üretilmez.** Hangi sıralamanın doğru olduğu iş kararı; guardrail varsayım
yapmaz. Sıralama BI/UI tarafında uygulanmalı.

## 9. Ret mesajları

`UserMessage` alanı şema bilgisi (görünüm adı, kolon adı, SQL parçası) **içermez**. Ret
mesajı bir şema keşif aracı olmamalıdır: "böyle bir kolon yok" ile "bu kolonu göremezsin"
arasındaki fark saldırgana bilgi verir. Bir test bu kuralı zorlar.

Tam kod listesi: `Contracts/ReasonCode.cs` (GR001–GR015, CL001–CL002).
İç teşhis notları yanıtta taşınmaz, yalnızca audit'e ve log'a gider.

## 10. Sizden beklediklerim

| Ne | Kimden | Neden bloke edici |
|---|---|---|
| `vw_sales`, `vw_customer_rfm`, `vw_payment` logical contract'ları | SQL production | `fabric-dwh-object-mapping-v1`: sırasıyla `mart.vw_sales`, `mart.vw_customer_rfm`, `mart.vw_payment`. Ham tablolar allow-list'e konamaz: kapsam kolonu (`customer_state`) yalnızca customers'ta, tutar (`price`) order_items'ta — ham tablo eklemek en hassas tabloyu kapsam filtresiz bırakırdı |
| ~~Kullanıcı → kapsam eşlemesi~~ | ~~Backend + DE~~ | **Yapıldı:** `crm-project/Services/ClaimsDataScopeResolver.cs` — token claim'lerinden türetiliyor, fail-closed. Rol/bölge claim'i çözümlenemezse `GR007`. Bölge **değerleri** hâlâ DE'den bekleniyor: uygulama TR/EU, katalog Brezilya eyaletleri (SP, RJ) kullanıyor |
| DB tarafında RLS + read-only bağlantı | Veri Mühendisi | Guardrail şu an **tek** savunma katmanı |
| Local Ollama semantic planner | AI / QA | Yalnızca strict canonical semantic key üretir; SQL üretemez |
| "Net satış" kargo dahil mi? | BI / iş birimi | Hiçbir metrik `net_sales` anahtarını almadı |
| RFM recency referans tarihi | İş birimi | `recency_days` ifadesi yazılmadı |

Desteklenen/desteklenmeyen soru tipleri ve gerekçeleri: `KAPSAM.md`.
