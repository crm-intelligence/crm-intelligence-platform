# 4. DWH ve OLTP Veri Stratejisi

DWH ve OLTP aynı amaçla kullanılmaz. Kaynak seçimi; talebin güncellik ihtiyacı, veri kapsamı, sorgu maliyeti ve operasyonel sistem üzerindeki potansiyel etkisi dikkate alınarak yapılır.

| Kriter | DWH / Fabric | OLTP |
|--------|--------------|------|
| Ana kullanım | Tarihsel ve analitik sorgular | Güncel operasyonel bilgi |
| Örnekler | Satış trendi, segmentasyon, kampanya, KPI | Son sipariş, bugünkü satış, anlık stok |
| Sorgu yaklaşımı | Varsayılan ve önerilen analitik kaynak | Sınırlı, kontrollü ve güncellik odaklı |
| Performans hedefi | Ağır analizleri taşıyabilen yapı | Operasyonel yükü artırmama |
| Erişim | Read-only, izinli görünüm ve tablolar | Read-only, sıkı timeout ve limitler |

## Kaynak Seçim İlkesi

Analitik sorgular mümkün olduğunca DWH veya Fabric üzerinden karşılanır. OLTP yalnızca güncel verinin iş açısından gerekli olduğu sınırlı senaryolarda kullanılır.

## Karar Kuralı (uygulanabilir hali)

```
EĞER talep "bugün / şu an / anlık / son sipariş" gibi güncellik ifadesi içeriyor
   VE tarih aralığı son 24 saat içindeyse
   VE beklenen satır sayısı düşükse
THEN kaynak = OLTP (timeout ve satır limiti sıkı)
ELSE kaynak = DWH / Fabric
```
