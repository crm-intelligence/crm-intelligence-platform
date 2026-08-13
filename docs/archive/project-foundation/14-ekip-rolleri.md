# 14. Ekip Rolleri ve Teknik Sorumluluklar

Beş rolün tamamı geliştirmede somut teknik çıktı üretir. Test, dokümantasyon ve sunum hazırlığı ekip içinde ortak sorumluluktur.

| Rol | Sahibi | Özet Sorumluluklar | Somut Çıktı |
|-----|--------|--------------------|-------------|
| **1. Veri Mühendisi** (Data Engineer) | Sümeyye (@sumeyyeeliacik) | DWH/OLTP bağlantıları, read-only görünümler, veri kalitesi, veri sözlüğü | Read-only, izinli ve dokümante edilmiş veri erişim katmanı |
| **2. Microsoft Fabric ve Business Intelligence Geliştiricisi** | Sıla (@silassaatli) | Fabric pipeline, veri dönüşümü, demo analitik flow'ları, semantic model, raporlar, RLS | Power BI için hazırlanmış Fabric veri ürünleri ve yetkili rapor deneyimi |
| **3. Yapay Zekâ Mühendisi** (AI Engineer — NL2SQL & Query Builder) | Mehmet Efe (@mehmetefeaytas) — ekip lideri | Talep ayrıştırma, Query Builder, guarded NL2SQL, metadata kullanımı, SQL guardrail | Doğal dilden kontrollü SQL üreten hibrit sorgu sistemi |
| **4. Backend Geliştirici** (ASP.NET Core & Microsoft Teams) | Ramazan (@RamazanBozkurrtt) | API, Teams entegrasyonu, konuşma bağlamı, rapor revizyonu, hata ve log yönetimi | Teams ile API arasında çalışan uçtan uca rapor talep akışı |
| **5. DevOps, Proje Yönetimi ve Kalite Güvence** | Lokman (@lokmannonal) | Azure ortamı, Entra ID, erişim kontrolleri, CI/CD, monitoring, audit, test yönetimi | Güvenli, izlenebilir ve tekrarlanabilir cloud altyapısı + test kanıtları |

## Rol Arası Sözleşmeler

| Kimden → Kime | Sözleşme |
|---------------|----------|
| Veri Mühendisi → AI Engineer | Allow-list'e girecek görünüm, kolon ve JOIN yolları listesi |
| Veri Mühendisi → Fabric/BI | Kaynak tablo şemaları ve veri kalite kuralları |
| AI Engineer → Backend | Canonical request şeması + SQL üretim servisi arayüzü |
| AI Engineer → Fabric/BI | Sonuç seti sözleşmesi (kolon adları, tipler, grain) |
| Fabric/BI → Backend | Rapor URL şablonu ve filtre parametreleri |
| Backend → DevOps | Log ve telemetri şeması |
| DevOps → Tüm ekip | Ortam, secret ve erişim yönetimi |
