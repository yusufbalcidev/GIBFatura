# GİB ESÜ Rapor Gönderimi

Bu masaüstü uygulaması sayesinde, AKİS uyumlu USB akıllı kart tipindeki mali mührünüzü kullanarak, elektrikli araçların şarj hizmetlerine ilişkin, ŞARJANLIK türünde düzenlediğiniz faturalara karşılık gelen aylık raporlarınızı Gelir İdaresi Başkanlığı'nın ÖKC (Ödeme Kaydedici Cihaz) web servisine imzalı ve toplu olarak, tek dosya halinde gönderebilirsiniz.

Dayanak: 551 Sıra No.lu VUK Genel Tebliği ve ESÜ Teknik Kılavuzu Sürüm 3.0.

- **Son gün:** takip eden ayın 15'i.
- **İçerik:** plaka başına tek satır (plaka, toplam kWh, toplam tutar, TRY).
- **Lisans ve harici servis yok:** TÜBİTAK MA3 lisansı ya da başka bir lisans sunucusu gerekmez. İmza .NET ile, kart sürücüsü (`akisp11.dll`) üzerinden atılır.
- **EŞÜ:** Elektrikli Şarj Ünitesi. GİB şarj cihazlarını ÖKC sistemine bağladığı için rapor ÖKC web servisine (`okc.gib.gov.tr/okcesu`) gönderilir.

## Kullanım (muhasebe)

Uygulamanın içinde menüdeki **Kılavuz** sayfası, her ay yapılacakları adım adım anlatır. Kısaca:

1. `GibEsuGonderim.exe` dosyasını çift tıklayın. Bir konsol penceresi açılır ve tarayıcıda `http://127.0.0.1:5080` sayfası açılır. Konsol penceresi açık kaldığı sürece uygulama çalışır.
2. **Yeni Rapor:** Excel'i seçin ve dönemi seçin. Varsayılan dönem önceki aydır.
3. **Önizleme:** plaka sayısını, toplam kWh'yi ve toplam tutarı kontrol edin. Hatalı satırlar satır numarasıyla listelenir; hata varsa Excel'i düzeltip yeniden yükleyin.
4. Mühür sertifikasını seçin, PIN'i girin, onay kutusunu işaretleyip **İmzala ve GİB'e gönder** düğmesine basın.
5. Uygulama GİB'in işleme sonucunu yaklaşık 1 dakika bekler:
   - **Kod 30:** başarıyla işlendi. İş bitti.
   - **Kod 10 veya 15:** henüz işleniyor. Birkaç saat sonra **Durum Sorgula** ekranından tekrar sorgulayın.
   - **Kod 150–167:** reddedildi. Açıklamaya göre düzeltip yeniden gönderin.

> **Dikkat:** 15, "işlenmeye başlandı" demektir; gönderim 30 görülene kadar tamamlanmış sayılmaz.

Excel'de `plaka`, `kwh`, `tl` sütunları bulunmalıdır. Ana sayfada boş bir şablon indirme bağlantısı vardır. İsteğe bağlı sütunlar: `aracKimlikNo`, `roaming vkn`, `roaming unvan`. Excel dosyası açıkken de okunabilir.

### Ekranlar

| Ekran | Ne işe yarar |
|---|---|
| Yeni Rapor (ana sayfa) | Geçen ayın raporunun durumunu ve son gününü gösterir (son 5 gün kırmızı). Sonucu kesinleşmemiş paketleri ve süresi 60 günden az kalmış mali mühürü listeler. |
| Önizleme | Önceki aya göre plaka sayısı, kWh ve tutar değişimini gösterir; %30'dan büyük sapmada uyarır. Aynı Excel ya da birebir aynı toplamlar daha önce gönderildiyse uyarır. |
| Durum Sorgula | Paket ID ile GİB durumunu sorgular. Başka bir programla gönderilmiş paketler de sorgulanabilir. Sorgu da mühürle imzalandığı için kart ve PIN gerekir. |
| Geçmiş | Tüm gönderimler ve son durumları. GİB'e giden imzalı paket (.zip) ve raporun hazırlandığı Excel indirilebilir. |
| Sistem Kontrolü | Kart sürücüsü, kart ve sertifika, saat sapması, GİB servislerine erişim. **Kartı test et:** PIN ile deneme imzası atar, örnek bir rapor ve SOAP mesajı imzalayıp doğrular; GİB'e bağlanmaz. **GİB imza testi:** seçilen ortama daha önce gönderilmiş bir paketin durumunu mühürle imzalı bir istekle sorar. GİB cevap verirse imza ve yetki tamamdır. Rapor göndermez. |
| Kılavuz | Kullanıcı kılavuzu: aylık adımlar, Excel biçimi, durum kodları, sorunlar ve çözümleri. |

## Kurulum

Kurulum gerekmez. `yayin` klasöründeki iki dosyayı kartın takılı olduğu bilgisayarda OneDrive dışındaki bir klasöre (önerilen: `C:\GibEsu`) kopyalamak yeterlidir. OneDrive eşitlemesi çalışan programın dosyalarını kilitleyebilir.

- `GibEsuGonderim.exe` (yaklaşık 48 MB; .NET içinde, ayrıca .NET kurmaya gerek yok)
- `appsettings.json` (ayarlar)

Bilgisayarda bulunması gerekenler:

1. Windows 10/11, 64-bit
2. AKİS kart sürücüsü (`C:\Windows\System32\akisp11.dll`). Mali mühürün daha önce kullanıldığı bilgisayarlarda zaten kuruludur.
3. Kart okuyucu ve mali mühür kartı
4. `okc.gib.gov.tr` ve `okctest.gib.gov.tr` adreslerine HTTPS (443) erişimi
5. Yönetici yetkisi gerekmez.

Kart aynı anda iki programa açılmayabilir. Bu uygulamayı kullanırken mühürü kullanan diğer programları kapatın.

### Ayarlar (`appsettings.json`)

| Ayar | Varsayılan | Açıklama |
|---|---|---|
| `Port` | 5080 | Uygulama yalnızca bu bilgisayardan erişilebilir (`127.0.0.1`). |
| `TarayiciAc` | true | Açılışta tarayıcıyı açar. |
| `CanliAcik` | `false` | **false iken canlı GİB'e hiçbir istek gitmez** (gönderim, durum sorgusu, imza testi); her sayfada "TEST MODU" yazar. Denemeler bitip canlıya geçiş kararı verilince `true` yapılıp uygulama yeniden başlatılır. |
| `VarsayilanOrtam` | `Test` | Gönderim ekranında seçili gelen ortam (`Canli` / `Test`). Canlı kapalıyken her zaman Test. |
| `Pkcs11Kutuphaneleri` | `akisp11.dll` | Kart sürücüsü yolu. Farklı bir kart kullanılırsa değiştirin. |
| `ImzaciRolu` | Şarj Ağı İşletmecisi | XAdES imzasındaki rol. |
| `CanliAdres` / `TestAdres` | `https://okc.gib.gov.tr/okcesu/services/EArsivWsPort` / `https://okctest.gib.gov.tr/okcesu/services/EArsivWsPort` | GİB'in okcdestek yazısında verilen gönderim adresleri (WSDL: aynı adres + `/earsiv.wsdl`). GİB adres değiştirirse yeniden derlemeden buradan güncellenir; yalnızca `https://` kabul edilir. |
| `SertifikaUyariGunu` | 60 | Mühürün bitişine bu kadar gün kala ana sayfada uyarı gösterilir. |
| `VeriKlasoru` | `%LOCALAPPDATA%\GibEsuGonderim` | Geçmiş (`gecmis.json`), imzalı paketler ve kaynak Excel'ler (`paketler\`). |
| `LogKlasoru` | `.exe`'nin yanındaki `loglar\` | Günlük log dosyaları (`gibesu-yyyyMMdd.log`). Yazılamazsa veri klasöründeki `logs\` kullanılır. |

PIN hiçbir yere kaydedilmez. Loglara plaka veya tutar yazılmaz; yalnızca işlem adımları, paket ID'leri ve hatalar yazılır.

## İlk kullanım için kontrol listesi

> Denemeler yalnızca **test ortamında** yapılır (`CanliAcik: false`). Canlı ortam, test sonuçları başarılı olup karar verilince açılır.

1. **Sistem Kontrolü → Kartı test et.** "Kart testi başarılı" çıkmalı. Ekrandaki unvan ve VKN'nin GİB izni olan şirkete ait olduğunu kontrol edin.
2. **Test ortamına gönderim:** Ekim 2026 Excel'ini yükleyip ortam **Test** ile gönderin. "Dosya kaydedildi" ve ardından **kod 30** beklenir. Sonuç bekliyorsa Durum Sorgula ya da Sistem Kontrolü → GİB imza testi (Test) ile aynı paket sorgulanır.
3. Canlıya geçişten sonra (`CanliAcik: true`) önceki programla gönderilmiş bir paket, paket ID'si ile canlıda sorgulanabilir.
   - **30 dönerse:** Eylül raporu tamam.
   - **15 dönerse:** paket hâlâ işleniyor.
   - **150 üstü bir kod dönerse:** Eylül raporu reddedilmiş; mali müşavirle görüşün.

## Önceki programa göre farklar ve Eylül verisinde bulunanlar

- **Plakalardaki noktalı İ:** önceki program Türkçe Windows'ta küçük "i" harfini "İ"ye çeviriyordu (ör. `34ABİ123`).
- **Aynı araç iki satır:** aynı sebeple `34ABİ123` ile `34ABI123` gibi plakalar iki ayrı satır olarak gönderilmiş. GİB plaka başına tek satır ister. Bu uygulama plakaları `I` ile yazar ve aynı plakayı tek satırda toplar; düzeltmeyi önizlemede uyarı olarak gösterir.
- **Plaka olmayan değerler:** Excel'de Türkiye biçimine uymayan değerler olabilir (ör. bir isim, eksik plaka ya da şasi numarası). Bunlar gönderimi engellemez; önizlemede uyarı olarak listelenir.
- **Dosya açıkken okuma:** Excel'de açık olan dosya da okunur.
- **Paket bütünlüğü kontrolü:** her gönderimden önce rapor resmi `esuRapor.xsd` şemasıyla, iki imza da kendi doğrulayıcımızla kontrol edilir. Hatalı paket GİB'e gitmez.
- **Mükerrer gönderim uyarısı:** aynı dönem için daha önce gönderim yapılmışsa uyarı verir ve ek onay ister.

## Teknik ayrıntılar

```
GIBGonderımASP/
├── src/GibEsu.Core     İş mantığı (net8.0 sınıf kütüphanesi)
│   ├── Rapor/          Excel okuma, esuRapor XML üretimi, XSD doğrulama, zip
│   ├── Imza/           PKCS#11 kart erişimi, XAdES ve WS-Security imzalayıcıları, doğrulayıcı
│   ├── Servis/         GİB SOAP istemcisi, durum kodları, NTP saati, uçtan uca akış
│   └── Kaynaklar/      esuRapor.xsd (GİB EŞÜ Paketi V3.1 ile birebir aynı), KamuSM kök sertifikaları
├── src/GibEsu.Web      ASP.NET Core MVC arayüzü (127.0.0.1'de çalışan yerel uygulama)
└── tests/GibEsu.Tests  xUnit testleri
```

### Mesaj yapısı

Yapı, GİB'in Eylül 2026'da kabul ettiği gerçek gönderimle birebir aynıdır. Testlerde log'daki bu gönderim doğrulanır.

1. **Rapor:** `earsiv:eArsivRaporu`. Başlıkta VKN (mükellef = hazırlayan = mühür sahibi), `raporNo` = paket ID (UUID), dönem ve `bolumNo=1` bulunur. Plaka başına bir `esuRapor` satırı vardır.
2. **Rapor imzası (XAdES-BES, enveloped):** `earsiv:baslik` içine eklenir.
   - SignedInfo: exclusive C14N; EC anahtarda **ECDSA-SHA384 (DER)**, RSA anahtarda RSA-SHA256.
   - Referanslar: SignedProperties ve belge, inclusive C14N, SHA-256.
   - SigningCertificate (SHA-256 özet ve IssuerSerial), SignerRole "Şarj Ağı İşletmecisi" ve DataObjectFormat `text/xml`.
3. **Paket:** `<paketId>.zip` (40 karakter), içinde tek bir `<paketId>.xml`. Açık boyut en fazla 100 MB.
4. **SOAP 1.2 ve WS-Security:** Timestamp (5 dk) ile Body imzalanır.
   - Referanslar: exclusive C14N (InclusiveNamespaces `wsse soap web` / `web`), SHA-1.
   - SignedInfo: exclusive C14N (`soap web`); EC anahtarda **ECDSA-SHA384 (ham r‖s)**.
   - Sertifika BinarySecurityToken olarak eklenir ve DirectReference ile gösterilir.
5. **Servis:** `https://okc.gib.gov.tr/okcesu/services/EArsivWsPort` (test: `okctest.gib.gov.tr`).
   - Gönderim: `sendDocumentFile` (SOAPAction `urn:SendDocumentFile`). Başarı yanıtı "Dosya kaydedildi".
   - Durum: `getBatchStatus` (SOAPAction `urn:GetBatchStatus`), paket ID ile.
6. **SSL:** GİB sunucularının sertifikası *KamuSM Kurumsal Kök* sertifikasına bağlanır ve bu kök Windows'ta güvenilir değildir. Kök sertifika uygulamaya gömülüdür; yalnızca bu kökle doğrulanan zincir kabul edilir.
7. **Saat:** WS-Security zaman damgası NTP'den (time.google.com) alınır, böylece bilgisayar saatindeki kayma sorun çıkarmaz.

Önemli bir tuzak: .NET'te `XmlDocument.Load`/`LoadXml` CR karakterlerini normalleştirmez. Windows satır sonlu belgelerde bu, özetleri bozar. Bu yüzden tüm XML'ler `XmlYardimci.Yukle` (standart `XmlReader`) ile okunur.

### Geliştirme

```bash
dotnet build GibEsu.sln
dotnet test GibEsu.sln
GIBESU_REFERANS_ZORUNLU=1 dotnet test GibEsu.sln         # referans log'u bulunmazsa referans testleri başarısız olur
dotnet run --project src/GibEsu.Web                      # http://127.0.0.1:5080
dotnet publish src/GibEsu.Web -c Release -o yayin        # tek dosya GibEsuGonderim.exe
```

Referans testleri, üst klasördeki `GİB-Rapor-Eylül2026.log` dosyasından GİB'e giden gerçek imzalı paketi çıkarır ve:

- iki imzayı bizim doğrulayıcımızla doğrular,
- 4.541 satırı Excel'e çevirip bizim akıştan geçirir ve aynı verinin üretildiğini kontrol eder.

### Kaynaklar

- ESÜ Teknik Kılavuzu Sürüm 3.0: https://ynokc.gib.gov.tr/UploadedFiles/Files/ESU_Teknik_Kilavuz_Surum3-20062024.pdf (bölüm 2.2 aylık bildirim)
- EŞÜ Paketi V3.1: https://ynokc.gib.gov.tr/UploadedFiles/Files/Esu_GIB_Paket_V3.1.rar
- e-Arşiv Teknik Kılavuzu V.1.18: https://ebelge.gib.gov.tr/dosyalar/kilavuzlar/e-Arsiv_Teknik_Kilavuzu_V.1.18.pdf (bölüm 5 gönderim servisi, bölüm 11 hata kodları)
