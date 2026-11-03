using System.Reflection;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Core.Xml;
using GibEsu.Web.Models;
using GibEsu.Web.Servisler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GibEsu.Web.Controllers;

/// <summary>
/// Sistem kontrolü: kart sürücüsü, kart ve sertifika, saat, GİB'e erişim ve kartla deneme imzası.
/// Deneme imzası GİB'e hiçbir şey göndermez.
/// </summary>
public class SistemController : Controller
{
    private readonly KartServisi _kart;
    private readonly HttpClient _http;
    private readonly GecmisDeposu _gecmis;
    private readonly EsuGonderimServisi _gonderim;
    private readonly Ayarlar _ayarlar;
    private readonly ILogger<SistemController> _log;

    public SistemController(KartServisi kart, HttpClient http, GecmisDeposu gecmis, EsuGonderimServisi gonderim,
        IOptions<Ayarlar> ayarlar, ILogger<SistemController> log)
    {
        _kart = kart;
        _http = http;
        _gecmis = gecmis;
        _gonderim = gonderim;
        _ayarlar = ayarlar.Value;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index() => View(await ModelAsync(new List<string>(), null, null));

    /// <summary>
    /// Seçilen GİB ortamına mühürle imzalı bir durum sorgusu gönderir. Rapor göndermez.
    /// GİB bir durum kodu dönerse imzamızı ve mühürün o ortamdaki yetkisini kabul etmiş demektir.
    /// </summary>
    [HttpPost]
    public async Task<IActionResult> GibImzaTesti(string sertifika, string pin, bool sonDeneme, string? paketId, GibOrtami ortam)
    {
        var ortamAdi = ortam == GibOrtami.Canli ? "canlı" : "test";
        var baslik = $"GİB {ortamAdi} imza testi";
        paketId = (paketId ?? "").Trim();
        if (ModelState[nameof(ortam)]?.Errors.Count > 0 || !Enum.IsDefined(ortam))
            return View("Index", await ModelAsync(new List<string> { "HATA: GİB ortamı geçersiz." }, false, baslik));
        if (!_ayarlar.OrtamIzinli(ortam))
            return View("Index", await ModelAsync(new List<string> { "HATA: " + Ayarlar.CanliKapaliMesaji }, false, baslik));
        if (!Guid.TryParse(paketId, out _))
            return View("Index", await ModelAsync(new List<string> { $"HATA: Daha önce {ortamAdi} ortama gönderilmiş bir paketin ID'sini girin (UUID biçiminde)." }, false, baslik));

        var sonuclar = new List<string>();
        bool basarili;
        try
        {
            var yanit = await _kart.ImzaciIleAsync(sertifika, pin, sonDeneme,
                imzaci => _gonderim.DurumSorgulaAsync(paketId, imzaci, ortam, CancellationToken.None));
            if (yanit.Durum is PaketDurumu d)
            {
                basarili = true;
                sonuclar.Add($"GİB {ortamAdi} servisi mühürle imzalı isteği kabul etti ve cevap verdi: imza ve yetki tamam.");
                sonuclar.Add($"Paket {paketId}: kod {d.Kod} – {d.Aciklama}{(d.GibAciklamasi != null ? " – " + d.GibAciklamasi : "")}");
                _gecmis.DurumGuncelle(paketId, d);
            }
            else
            {
                basarili = false;
                var mesaj = yanit.HataMesaji ?? "GİB'den durum alınamadı.";
                sonuclar.Add("HATA: " + mesaj);
                if (mesaj.Contains("yetki", StringComparison.OrdinalIgnoreCase))
                    sonuclar.Add("Mühür doğru imzalıyor ama bu VKN için GİB'de ESÜ rapor yetkisi görünmüyor. okcdestek@gelirler.gov.tr ile görüşün.");
                else
                    sonuclar.Add($"Paket ID'nin doğru ve {ortamAdi} ortama gönderilmiş bir pakete ait olduğundan emin olun. Mesaj imzayla ilgiliyse bu ekranın görüntüsünü destek için saklayın.");
            }
        }
        catch (Exception ex) when (ex is KartHatasi or GonderimHatasi)
        {
            sonuclar.Add("HATA: " + ex.Message);
            basarili = false;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "GİB imza testinde bağlantı hatası");
            sonuclar.Add("HATA: GİB'e bağlanılamadı: " + ex.Message);
            basarili = false;
        }

        _log.LogInformation("GİB imza testi: {Paket} {Sonuc}", paketId, basarili ? "başarılı" : "başarısız");
        return View("Index", await ModelAsync(sonuclar, basarili, baslik, paketId));
    }

    [HttpPost]
    public async Task<IActionResult> KartTesti(string sertifika, string pin, bool sonDeneme)
    {
        var sonuclar = new List<string>();
        bool basarili;
        try
        {
            basarili = await _kart.ImzaciIleAsync(sertifika, pin, sonDeneme, imzaci =>
            {
                sonuclar.Add("Karta PIN ile giriş yapıldı.");
                var s = imzaci.Sertifika;
                sonuclar.Add($"Sertifika: {SertifikaBilgisi.OrtakAd(s)} – VKN {SertifikaBilgisi.VknVeyaTckn(s) ?? "bulunamadı"} – geçerlilik {s.NotAfter:dd.MM.yyyy}");

                // 1) Ham imza: kart özet imzalayabiliyor mu?
                var (_, ozetAlg) = ImzaAlgoritmasi.Sec(s);
                var ozet = XmlYardimci.Ozet(ozetAlg, RandomNumberGenerator.GetBytes(32));
                var imza = imzaci.OzetImzala(ozet, ozetAlg);
                var hamDogru = ImzaAlgoritmasi.EcMi(s)
                    ? s.GetECDsaPublicKey()!.VerifyHash(ozet, imza, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                    : s.GetRSAPublicKey()!.VerifyHash(ozet, imza, ozetAlg, RSASignaturePadding.Pkcs1);
                sonuclar.Add(hamDogru ? "Kartla deneme imzası atıldı ve doğrulandı." : "HATA: Kartın attığı imza doğrulanamadı.");
                if (!hamDogru)
                    return Task.FromResult(false);

                // 2) Örnek rapor: XAdES imzası + şema kontrolü (gönderilmez).
                var ornek = new ExcelOkumaSonucu();
                ornek.Satirlar.Add(new EsuSatiri("34TEST01", 1.00m, 1.00m));
                var oncekiAy = DateTime.Today.AddMonths(-1);
                var paket = EsuGonderimServisi.PaketHazirla(ornek, oncekiAy.Year, oncekiAy.Month, imzaci, _ayarlar.ImzaciRolu);
                sonuclar.Add($"Örnek rapor imzalandı, şema ve imza kontrolünden geçti (paket {paket.PaketId}, GİB'e gönderilmedi).");

                // 3) Örnek SOAP imzası.
                var zarf = WsSecurityImzalayici.ImzaliZarf(WsSecurityImzalayici.DurumSorguGovdesi(paket.PaketId.ToString()), imzaci, DateTime.UtcNow);
                var soap = ImzaDogrulayici.WsSecurityDogrula(XmlYardimci.Yukle(zarf));
                sonuclar.Add(soap.Gecerli ? "Örnek SOAP imzası doğrulandı." : "HATA: " + soap);
                return Task.FromResult(soap.Gecerli);
            });
        }
        catch (Exception ex) when (ex is KartHatasi or GonderimHatasi)
        {
            sonuclar.Add("HATA: " + ex.Message);
            basarili = false;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Kart testi beklenmeyen hata");
            sonuclar.Add("HATA: " + ex.Message);
            basarili = false;
        }

        _log.LogInformation("Kart testi: {Sonuc}", basarili ? "başarılı" : "başarısız");
        return View("Index", await ModelAsync(sonuclar, basarili, "Kart testi"));
    }

    private async Task<SistemModeli> ModelAsync(List<string> testSonuclari, bool? testBasarili, string? testBasligi,
        string? gibTestPaketId = null)
    {
        var kartGorevi = _kart.ListeleAsync();
        var saatGorevi = NtpSaati.SimdiAsync();
        var baglantiGorevleri = new[] { GibOrtami.Canli, GibOrtami.Test }.Select(BaglantiDeneAsync).ToArray();
        await Task.WhenAll(kartGorevi, saatGorevi, Task.WhenAll(baglantiGorevleri));

        var (sertifikalar, kartHatasi) = kartGorevi.Result;
        return new SistemModeli
        {
            Surum = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "",
            VeriKlasoru = _ayarlar.VeriKlasoruYolu,
            LogKlasoru = _ayarlar.LogKlasoruYolu,
            VeriKlasoruUyarisi = _ayarlar.VeriKlasoruUyarisi,
            Suruculer = _ayarlar.Kutuphaneler.Select(y => (y, System.IO.File.Exists(y))).ToList(),
            Kart = new KartSecimModeli { Sertifikalar = sertifikalar, KartHatasi = kartHatasi, Ortam = _ayarlar.EtkinVarsayilanOrtam },
            Saat = saatGorevi.Result,
            Baglantilar = baglantiGorevleri.Select(g => g.Result).ToList(),
            TestSonuclari = testSonuclari,
            TestBasarili = testBasarili,
            TestBasligi = testBasligi,
            GibTestPaketId = gibTestPaketId
                ?? _gecmis.Tumu().FirstOrDefault(k => _ayarlar.OrtamIzinli(k.Ortam) && k.GibeUlasti)?.PaketId,
        };
    }

    /// <summary>GİB servisine SSL ile bağlanılabiliyor mu? (GET isteğine 405 dönmesi beklenir.) Kapalı ortam denenmez.</summary>
    private async Task<(GibOrtami, string, bool?)> BaglantiDeneAsync(GibOrtami ortam)
    {
        if (!_ayarlar.OrtamIzinli(ortam))
            return (ortam, "Canlı ortam kapalı (appsettings.json: CanliAcik); bağlantı denenmedi.", null);
        try
        {
            using var zaman = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            using var yanit = await _http.GetAsync(GibEsuIstemcisi.Adres(ortam), zaman.Token);
            return (ortam, $"Erişilebilir (HTTP {(int)yanit.StatusCode}) – {GibEsuIstemcisi.Adres(ortam)}", true);
        }
        catch (Exception ex)
        {
            var mesaj = ex.InnerException?.Message ?? ex.Message;
            return (ortam, "Erişilemedi: " + mesaj, false);
        }
    }
}
