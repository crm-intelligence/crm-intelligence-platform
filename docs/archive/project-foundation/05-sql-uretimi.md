# 5. SQL Üretimi: Query Builder ve Guarded NL2SQL

## 5.1 Hibrit Yaklaşım

Tek bir yönteme bağımlı kalmak yerine hibrit yaklaşım kullanılır.

| Yaklaşım | Kullanım Alanı | Temel Avantaj |
|----------|----------------|---------------|
| **Query Builder** | Tanımlı, test edilmiş ve sık kullanılan raporlar | Tahmin edilebilirlik, hız, tutarlılık |
| **Guarded NL2SQL** | Yeni, izinli ve şema kapsamında kalan analiz talepleri | Esneklik, daha geniş soru kapsamı |
| **Netleştirme / Ret** | Belirsiz, yetkisiz veya yüksek riskli talepler | Güvenli ve açıklanabilir kullanıcı deneyimi |

## 5.2 Talep Yönlendirme Mantığı

```
Doğal dil talebi
      ↓
Canonical Request'e ayrıştır (metric, dimension, filter, dateRange, grain)
      ↓
Talep tanımlı bir senaryo şablonuna eşleşiyor mu?
   ├── EVET → Query Builder (deterministik)
   ├── HAYIR, ama allow-list şema kapsamında → Guarded NL2SQL
   └── HAYIR, belirsiz veya kapsam dışı → NeedsClarification / Rejected
      ↓
SQL Guardrail (her iki yol için zorunlu)
      ↓
Onaylı ve parametreli SQL
```

## 5.3 SQL Guardrail Kontrolleri

1. Yalnızca `SELECT` sorgularına izin verilmesi
2. İzinli tablo, kolon, görünüm ve JOIN yollarının allow-list ile sınırlandırılması
3. SQL parser veya AST tabanlı doğrulama (regex ile yetinilmez)
4. Kullanıcının veri kapsamı filtresinin sorguya **zorunlu** uygulanması
5. Parametreli sorgu, tarih aralığı, satır sayısı ve query timeout limitleri
6. Gereksiz kişisel verilerin seçilmemesi ve minimum veri ilkesi
7. Tüm sorgu kararlarının request ID ile audit kaydına alınması

### Reddedilmesi zorunlu kalıplar

- `INSERT`, `UPDATE`, `DELETE`, `MERGE`, `TRUNCATE`, `DROP`, `ALTER`, `CREATE`, `GRANT`
- `EXEC`, `sp_`, `xp_`, dinamik SQL, `OPENROWSET`, `BULK INSERT`
- Birden fazla ifade (`;` ile ayrılmış statement zinciri)
- Yorum enjeksiyonu (`--`, `/* */`) ile filtre atlatma denemeleri
- Allow-list dışı tablo, kolon, şema veya JOIN yolu
- Veri kapsamı filtresi içermeyen sorgu
- `SELECT *` kullanımı

> **Kontrol ilkesi:** AI, SQL taslağı önerebilir; sorgunun çalıştırılabilir olup olmadığına deterministik güvenlik kontrolleri karar verir.
