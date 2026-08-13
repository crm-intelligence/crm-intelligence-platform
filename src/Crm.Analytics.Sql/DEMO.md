# Guardrail Canlı Gösterimi — Sprint Gün 11

**Senaryolar:** bir geçerli talep, bir belirsiz talep, bir reddedilen talep (+ atlatma
denemeleri, takip sorusu, denetim izi).

## Nasıl çalıştırılır

```bash
dotnet test --filter "FullyQualifiedName~Demo" --logger "console;verbosity=detailed"
```

Demo ayrı bir konsol uygulaması değil, **test olarak** yazıldı. Böylece her CI koşusunda
doğrulanır: slaytta gösterilen çıktı ile gerçek davranışın ayrışma ihtimali kalmaz. Demo
bozulursa test kırmızı döner.

Tek senaryo çalıştırmak için: `--filter "FullyQualifiedName~Senaryo_3"`

---

## Senaryo 1 — Geçerli talep

Aşağıdaki çıktı gerçek koşudan alınmıştır.

```
Talep      : 2018 satış tutarını eyalete göre göster
Kullanici  : SP ve RJ eyaletlerini gorebiliyor
Karar      : Accepted
Yol        : QueryBuilder
Gorsel     : BarChart — Tek kategorik kirilim; kategoriler arasi karsilastirma bar ile okunur.
Timeout    : 30 sn
Dogrulanan kontrol sayisi: 16/16

Uretilen SQL:
SELECT TOP 5000 customer_state AS customer_state,
                SUM(price) AS item_sales
FROM vw_sales
WHERE (order_purchase_timestamp >= @f0
       AND order_purchase_timestamp <= @f1)
      AND vw_sales.customer_state IN (@scope0, @scope1)
GROUP BY customer_state;

ZORLA EKLENEN KAPSAM FILTRESI: vw_sales.customer_state IN (@scope0, @scope1)

Parametreler (DEGERLER audit'e YAZILMAZ):
  @f0 : Date = 2018-01-01
  @f1 : Date = 2018-12-31
  @scope0 : Text = SP
  @scope1 : Text = RJ
```

Gösterilecek noktalar:

- Kullanıcı **kapsam filtresi yazmadı**; guardrail ekledi.
- Hiçbir değer SQL metninde yok — tarih de eyalet kodu da parametre.
- `TOP 5000` talep edilmedi, guardrail koydu.
- Mevcut WHERE koşulu parantez içinde: `(tarih) AND kapsam`. Parantez olmasa
  `a OR b AND kapsam` yazımında `a` kolu filtreden kaçardı.

**Senaryo 1b:** aynı metin, iki farklı kullanıcı → farklı filtre. Filtre kullanıcının
metninden değil **yetkisinden** gelir.

---

## Senaryo 2 — Belirsiz talep

| Talep | Karar | Neden |
|---|---|---|
| "bana bir şeyler göster" | `NeedsClarification` / `CL001` | Katalogda hiçbir terim eşleşmedi |
| "2018 kâr marjı nedir" | `NeedsClarification` / `CL001` | "kâr marjı" katalogda yok → uydurma metriğe bağlanmaz |
| "satış tutarını eyalete göre göster" | `NeedsClarification` / `CL002` | Tarih aralığı yok → varsayılan aralık **uydurulmaz** |

Üçüncüsü önemli: aralık olmadan sorgu tüm dönemi tarar ve tarih bütçesi denetlenemez.
`TOP` dönen satırı sınırlar, **taranan** satırı sınırlamaz.

---

## Senaryo 3 — Reddedilen talepler (Gün 4 sonu kontrolü)

Doküman sorusu: *DELETE, yetkisiz tablo ve kapsam dışı bölge talepleri reddediliyor mu?*
Aşağıdaki SQL'ler dil modelinin ürettiği taslak varsayılır; her biri aynı hattan geçer.

```
Veri degistirme            -> Rejected / GR001   takilan: SelectOnly
Yetkisiz tablo             -> Rejected / GR003   takilan: AllowListObjects
Kapsam disi bolge talebi   -> Rejected / GR008   takilan: ScopeFilterInjection
Ifade zinciri              -> Rejected / GR002   takilan: SingleStatement
Yildiz secim               -> Rejected / GR004   takilan: NoStarSelect
Dinamik SQL                -> Rejected / GR001   takilan: SelectOnly
Kimlik seviyesinde detay   -> Rejected / GR012   takilan: MinCellSize
```

Her ret hem bir **gerekçe kodu** hem de **hangi kontrolde takıldığı** bilgisini taşır.
Kullanıcıya giden mesaj şema bilgisi içermez: "Bu veri alanı rapor kapsamında tanımlı
değil" — hangi tablo olduğu söylenmez, çünkü ret mesajı şema keşif aracı olmamalı.

**Senaryo 3b — fail-closed:** kapsam çözümlenemezse (`Unresolved`) sorgu çalışmaz →
`GR007`. Boş kapsamın "kısıt yok" sayılması en tehlikeli hata olurdu.

---

## Senaryo 3c — Atlatma denemeleri reddedilmez, **etkisiz kalır**

Bu ayrım demoda önemli: guardrail bu sorguları düzeltip çalıştırılabilir hâle getirir.
Önemli olan atlatmanın işe yaramaması.

| Deneme | Sonuç |
|---|---|
| Filtreyi yoruma alma (`-- AND kapsam`) | Yorum AST'de yok; üretilen SQL'de `--` bulunmaz |
| Kullanıcının kendi kapsam filtresini yazması | Kendi filtresi "kapsam uygulandı" saydırmaz; guardrail kendi filtresini **ayrıca** ekler |
| `TOP 50 PERCENT` | Sabit değere çevrilir — yüzde satır sayısını garanti etmez |
| `UNION` | Kapsam filtresi **her iki kola** ayrı ayrı enjekte edilir |

Son madde en kritiği: tek kola filtre eklemek diğer koldan filtrelenmemiş satır
döndürürdü. Test bunu sayarak doğrular (`2` adet kapsam koşulu).

---

## Senaryo 4 — Takip sorusu

```
1. Talep : 2018 satış tutarını eyalete göre göster
   Metrik : item_sales        Kirilim : customer_state    Tarih : 2018-01-01 .. 2018-12-31

2. Talep : kategoriye göre          <- yalnizca kirilim degisti
   Metrik : item_sales (korundu)    Kirilim : product_category (degisti)    Tarih : (korundu)
```

Aynı metin ilk talep olarak gelseydi `CL002` alırdı (tarih yok). Takip sorusunda kullanıcı
tarihi değiştirmediğini kastediyor. `previousRequestId` zinciri korunur.

Anlaşılmayan takip sorusu ("hmm bilmiyorum") önceki raporu **tekrar üretmez** —
netleştirme ister. Boş delta "isteğin uygulandı" izlenimi verirdi.

---

## Senaryo 5 — Denetim izi

Her karar `requestId` ile audit'e yazılır; guardrail hiç koşmadıysa da yazılır
(`VerifiedCheckCount = 0`, koşulmamış kontrol "geçti" sayılmaz).

Kayıtta **parametre adları** var, **değerleri yok**. Filtre değerleri kullanıcı verisidir;
guardrail'ın PII kontrollerini bir yandan uygularken aynı veriyi diğer yandan log
altyapısına sızdırmak çelişkili olurdu.

---

## Demo sırasında söylenmesi gerekenler

1. **Üretilen SQL gerçek veri üzerinde koşuyor.** Üç görünüm SQLite lehçesinde yerel
   fixture olarak mevcut (`Crm.Analytics.Sql.DevData/sqlite_views.sql`, repodaki
   CSV'lerden üretiliyor) ve `Crm.Analytics.Sql.IntegrationTests` bu fixture üzerinde
   koşuyor. Sınırı net söyleyin: doğrulama **SQLite'a çevrilmiş** SQL ile yapıldı,
   SQL Server/Fabric üzerinde değil.
2. **Görünümler SQL Server'a hâlâ kurulmadı.** `Catalog/olist_views.contract.sql`
   T-SQL sözleşmesi olarak yazıldı ama çalıştırılmak için değil; kurulum Veri
   Mühendisi'nde. `ViewContractDriftTests` iki dosyanın kolon setini karşılaştırarak
   elle senkron tutulan bu ikiliyi denetliyor.
3. **DB tarafında RLS kurulmadı.** Guardrail şu an tek savunma katmanı.
4. **Üretilen SQL'in referans SQL ile aynı sonucu verdiği doğrulanmadı.** Fixture SQL'in
   *çalıştığını* kanıtlıyor; *doğru işi yaptığını* değil. Referans karşılaştırması için
   BI tarafının onaylı referans sorguları gerekiyor — DoD'de bu madde açık.
5. **Olist verisi 2018'de bitiyor.** "Bu yıl" ifadesi 2026'yı çözer ve boş sonuç döner;
   demoda mutlak tarih kullanın. (Entegrasyon testleri bu yüzden bugünü `2026-07-30`
   sabitliyor.)
6. `net_sales` adlandırması ve RFM recency referans tarihi **iş kararı** olarak bekliyor.
