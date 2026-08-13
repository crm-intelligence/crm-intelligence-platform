# 1. Yönetici Özeti ve Proje Vizyonu

## 1.1 Özet

İş kullanıcıları Microsoft Teams üzerinden doğal dilde CRM, satış ve müşteri analizi talep eder. Talep kontrollü biçimde SQL'e dönüştürülür, veri DWH / OLTP / Microsoft Fabric katmanlarında işlenir, sonuç Teams özeti ve Power BI raporu olarak sunulur.

Çözümün değeri veri ekiplerinin rolünü ortadan kaldırmak değil; **tekrar eden ve tanımlı rapor taleplerinin bir bölümünü güvenli self-service analitik deneyimine dönüştürmektir.**

- Standart raporlar için **deterministik Query Builder**
- Yeni fakat izinli ad hoc talepler için **guarded NL2SQL**
- Üretilen sorgular doğrudan çalıştırılmaz; yetki, allow-list, parser, süre, satır ve veri kapsamı kontrollerinden geçer

## 1.2 İş Problemi

Kurumlarda veri çoğu zaman mevcuttur; teknik olmayan kullanıcıların doğru veriye hızlı ulaşması kolay değildir. Basit bir iş sorusu bile tablo analizi, SQL geliştirme, doğrulama ve rapor hazırlama adımlarına dönüşür. Sabit dashboard'lar temel ihtiyaçları karşılasa da yeni kırılımlar, takip soruları ve ad hoc analizler için veri veya BI ekibi desteği gerekir.

## 1.3 Hedeflenen Değişim

- Teams üzerinden doğal dilde analiz talebi
- Takip sorularıyla mevcut raporun filtre, metrik ve kırılımlarının güncellenmesi
- Teams'te kısa özet, Power BI'da detaylı rapor
- KPI ve metriklerin kurumsal tanımlarla tutarlı hesaplanması
- Veri erişiminin kullanıcı rolü ve veri kapsamına göre sınırlandırılması

## 1.4 Ürün Konumlandırması

Çözüm yalnızca doğal dilden SQL üreten bir chatbot değildir. Hedef ürün; kullanıcı deneyimi, kimlik ve yetki, güvenli SQL üretimi, veri kaynağı seçimi, Fabric analitiği, Power BI raporlaması ve audit yeteneklerini tek akışta birleştiren kurumsal CRM analytics platformudur.

> **Nihai proje mesajı:** Bu ürün veriye erişimi serbest bırakmak yerine güvenli ve yönetilebilir hale getirir; doğal dil deneyimini kurumsal veri yönetişimiyle birleştirir.
