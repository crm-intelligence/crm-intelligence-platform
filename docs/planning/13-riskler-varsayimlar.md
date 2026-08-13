# 13. Varsayımlar, Riskler ve Kapsam Dışı Alanlar

## 13.1 Temel Varsayımlar

- DWH ve OLTP veri kaynaklarının Azure üzerinde veya Azure servislerinden güvenli biçimde erişilebilir olması
- Kurumun gerekli Entra ID, ağ, veritabanı ve Power BI yetkilerini sağlayabilmesi
- Demo için KPI, metric, dimension ve veri ilişkilerinin iş birimi tarafından doğrulanması
- Power BI lisanslama ve çalışma alanı modelinin proje öncesinde netleştirilmesi
- Kişisel veri ve veri sınıflandırma kurallarının kurum politikalarına göre belirlenmesi

## 13.2 Riskler ve Azaltma

| Risk | Olası Etki | Azaltma Yaklaşımı |
|------|-----------|-------------------|
| Veri kalitesi veya KPI belirsizliği | Yanlış veya çelişkili raporlar | Veri sözlüğü, metric catalog ve iş birimi onayı |
| NL2SQL hata üretimi | Yanlış sorgu veya güvenlik riski | Guardrail, allow-list, parser ve Query Builder önceliği |
| OLTP üzerinde yüksek yük | Operasyonel sistem etkisi | DWH/Fabric önceliği, timeout ve query limitleri |
| Power BI model karmaşıklığı | Performans ve bakım zorluğu | Demo için sabit model, kontrollü genişleme |
| Yetki modelinin geç netleşmesi | Gecikme ve yeniden geliştirme | Faz 0-1 içinde rol ve veri kapsamı tasarımı |

## 13.3 Kapsam Dışı Alanlar

- Veritabanında `INSERT`, `UPDATE`, `DELETE` veya benzeri yazma işlemleri
- Her kullanıcı sorusu için yeni bir PBIX oluşturulması
- Kontrolsüz veya tüm kurumsal şemaya açık NL2SQL erişimi
- Müşteri sunucusuna özel on-premises Query Agent kurulumu
- Demo fazında tam otonom aksiyon alan agent yapısı
