using System.Diagnostics;
using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Web.Models;
using GibEsu.Web.Servisler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace GibEsu.Web.Controllers;

public class HomeController : Controller
{
    private static readonly TimeSpan OnizlemeSuresi = TimeSpan.FromHours(2);
    private static readonly TimeSpan DurumBeklemeSuresi = TimeSpan.FromSeconds(60);
    private static readonly object GonderimKilidi = new();
    public const long EnBuyukYukleme = 50 * 1024 * 1024;

    private readonly IMemoryCache _onbellek;
    private readonly KartServisi _kart;
    private readonly GecmisDeposu _gecmis;
    private readonly EsuGonderimServisi _gonderim;
    private readonly Ayarlar _ayarlar;
    private readonly ILogger<HomeController> _log;

    public HomeController(IMemoryCache onbellek, KartServisi kart, GecmisDeposu gecmis, EsuGonderimServisi gonderim,
        IOptions<Ayarlar> ayarlar, ILogger<HomeController> log)
    {
        _onbellek = onbellek;
        _kart = kart;
        _gecmis = gecmis;
        _gonderim = gonderim;
        _ayarlar = ayarlar.Value;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index()
    {
        var bugun = DateTime.Today;
        var (yil, ay) = RaporTakvimi.BeklenenDonem(bugun);
        var donem = $"{yil:0000}-{ay:00}";
        var tumu = _gecmis.Tumu();

        // Kart takılı değilse ya da okunamıyorsa ana sayfa yine açılır; uyarı yalnızca okunabilen sertifikalar için.
        var (sertifikalar, _) = await _kart.ListeleAsync();
        var uyariSiniri = DateTime.Now.AddDays(_ayarlar.SertifikaUyariGunu);

        return View(new AnaSayfaModeli
        {
            Yil = yil,
            Ay = ay,
            Hata = TempData["Hata"] as string,
            BeklenenDonemAdi = Bicim.AyAdi(yil, ay),
            SonTarih = RaporTakvimi.SonTarih(yil, ay),
            KalanGun = RaporTakvimi.KalanGun(yil, ay, bugun),
            DonemGonderimleri = tumu.Where(k => k.Donem == donem && k.Ortam == GibOrtami.Canli && k.GibeUlasti).ToList(),
            SonucBekleyenler = tumu.Where(k => k.SonucBekleniyor && k.Tarih > DateTime.Now.AddDays(-90)).ToList(),
            SuresiYaklasanlar = sertifikalar.Where(s => s.MaliMuhurMu && s.Sertifika.NotAfter < uyariSiniri).ToList(),
        });
    }

    [HttpPost]
    public IActionResult Onizle(IFormFile? excel, string? donem)
    {
        if (excel == null)
        {
            TempData["Hata"] = "Excel dosyası seçilmedi.";
            return RedirectToAction(nameof(Index));
        }
        if (excel.Length == 0)
        {
            TempData["Hata"] = "Seçilen dosya boş (0 bayt).";
            return RedirectToAction(nameof(Index));
        }
        // Dönem "yyyy-MM" biçiminde gelir (ör. 2026-10).
        if (!DateTime.TryParseExact(donem, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture,
                System.Globalization.DateTimeStyles.None, out var donemBasi) || donemBasi.Year < 2024)
        {
            TempData["Hata"] = "Dönem geçersiz.";
            return RedirectToAction(nameof(Index));
        }
        var (yil, ay) = (donemBasi.Year, donemBasi.Month);
        // Rapor ay bittikten sonra gönderilir; bitmemiş ayın verisi eksiktir.
        var aySonrasi = new DateTime(yil, ay, 1).AddMonths(1);
        if (aySonrasi > DateTime.Today)
        {
            TempData["Hata"] = $"{Bicim.AyAdi(yil, ay)} henüz bitmedi. Bu ayın raporu {aySonrasi:dd.MM.yyyy} tarihinden itibaren gönderilebilir.";
            return RedirectToAction(nameof(Index));
        }

        byte[] icerik;
        using (var ms = new MemoryStream())
        {
            excel.CopyTo(ms);
            icerik = ms.ToArray();
        }
        var veri = new OnizlemeVerisi
        {
            Anahtar = Guid.NewGuid(),
            ExcelAdi = Path.GetFileName(excel.FileName),
            Yil = yil,
            Ay = ay,
            Sonuc = EsuExcelOkuyucu.Oku(new MemoryStream(icerik, writable: false)),
            ExcelIcerigi = icerik,
            ExcelOzeti = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(icerik)),
        };
        _onbellek.Set(veri.Anahtar, veri, OnizlemeSuresi);
        _log.LogInformation("Excel okundu: {Dosya}, dönem {Donem}, {Satir} satır, {Hata} hata",
            veri.ExcelAdi, veri.Donem, veri.Sonuc.OkunanSatirSayisi, veri.Sonuc.Hatalar.Count);
        return RedirectToAction(nameof(Onizleme), new { id = veri.Anahtar });
    }

    [HttpGet]
    public async Task<IActionResult> Onizleme(Guid id)
    {
        if (!_onbellek.TryGetValue(id, out OnizlemeVerisi? veri) || veri == null)
        {
            TempData["Hata"] = "Önizleme süresi doldu veya rapor zaten gönderildi. Excel'i yeniden yükleyin.";
            return RedirectToAction(nameof(Index));
        }

        var (sertifikalar, kartHatasi) = veri.Sonuc.Gecerli
            ? await _kart.ListeleAsync()
            : (new List<KartSertifikasi>(), null);
        var oncekiDonem = _gecmis.OncekiDonemKaydi(veri.Donem);
        return View(new OnizlemeModeli
        {
            Veri = veri,
            Kart = new KartSecimModeli { Sertifikalar = sertifikalar, KartHatasi = kartHatasi, Ortam = _ayarlar.EtkinVarsayilanOrtam },
            OncekiGonderimler = _gecmis.DonemGonderimleri(veri.Donem, GibOrtami.Canli)
                .Concat(_gecmis.DonemGonderimleri(veri.Donem, GibOrtami.Test)).ToList(),
            Hata = TempData["Hata"] as string,
            OncekiDonem = oncekiDonem,
            Farklar = oncekiDonem == null ? new() : DonemKarsilastirma.Karsilastir(oncekiDonem.Ozet, veri.Ozet),
            AyniVeriGonderimleri = veri.Sonuc.Gecerli ? _gecmis.AyniVeriIleGonderilenler(veri.ExcelOzeti, veri.Ozet, veri.Donem) : new(),
        });
    }

    [HttpPost]
    public async Task<IActionResult> Gonder(Guid id, string sertifika, string pin, GibOrtami ortam,
        bool onay, bool sonDeneme, bool mukerrerOnay)
    {
        // Tarayıcı kapansa bile başlamış gönderim yarıda kesilmesin.
        var iptal = CancellationToken.None;
        if (!_onbellek.TryGetValue(id, out OnizlemeVerisi? veri) || veri == null)
        {
            TempData["Hata"] = "Önizleme süresi doldu veya rapor zaten gönderildi. Excel'i yeniden yükleyin.";
            return RedirectToAction(nameof(Index));
        }
        // Geçersiz değer gelirse ASP.NET onu sessizce varsayılana (Test) çevirir; bunu ModelState'ten yakala.
        if (ModelState[nameof(ortam)]?.Errors.Count > 0 || !Enum.IsDefined(ortam))
            return OnizlemeyeDon(id, "GİB ortamı geçersiz.");
        if (!_ayarlar.OrtamIzinli(ortam))
            return OnizlemeyeDon(id, Ayarlar.CanliKapaliMesaji);
        if (!onay)
            return OnizlemeyeDon(id, "Gönderimden önce dönemi ve toplamları kontrol ettiğinizi onaylayın.");
        if (string.IsNullOrWhiteSpace(sertifika))
            return OnizlemeyeDon(id, "Mühür sertifikası seçilmedi.");
        if (_gecmis.DonemGonderimleri(veri.Donem, ortam).Count > 0 && !mukerrerOnay)
            return OnizlemeyeDon(id, "Bu dönem için daha önce gönderim yapılmış. Yine de göndermek için mükerrer gönderim onayını işaretleyin.");

        // Aynı önizleme iki kez gönderilmesin (çift tıklama, iki sekme): önizlemeyi tek seferde sahiplen.
        lock (GonderimKilidi)
        {
            if (!_onbellek.TryGetValue(id, out OnizlemeVerisi? sahiplenilen) || sahiplenilen == null)
            {
                TempData["Hata"] = "Bu rapor şu anda gönderiliyor ya da zaten gönderildi. Sonucu Geçmiş sayfasından takip edin.";
                return RedirectToAction("Index", "Gecmis");
            }
            _onbellek.Remove(id);
        }

        GonderimKaydi? kayit = null;
        try
        {
            await _kart.ImzaciIleAsync(sertifika, pin, sonDeneme, async imzaci =>
            {
                var paket = EsuGonderimServisi.PaketHazirla(veri.Sonuc, veri.Yil, veri.Ay, imzaci, _ayarlar.ImzaciRolu);
                var k = new GonderimKaydi
                {
                    PaketId = paket.PaketId.ToString("D"),
                    Tarih = DateTime.Now,
                    Donem = veri.Donem,
                    Ortam = ortam,
                    Vkn = paket.Vkn,
                    ExcelAdi = veri.ExcelAdi,
                    SatirSayisi = paket.SatirSayisi,
                    ToplamKwh = paket.ToplamKwh,
                    ToplamTutar = paket.ToplamTutar,
                    ExcelOzeti = veri.ExcelOzeti,
                };
                _gecmis.PaketiSakla(k.PaketId, paket.Zip);
                _gecmis.ExceliSakla(k.PaketId, veri.ExcelIcerigi);
                _gecmis.Kaydet(k);
                kayit = k;
                _log.LogInformation("Paket hazırlandı: {Paket}, dönem {Donem}, ortam {Ortam}, {Satir} plaka, zip {Boyut} bayt",
                    k.PaketId, k.Donem, ortam, k.SatirSayisi, paket.Zip.Length);

                var yanit = await _gonderim.GonderAsync(paket, imzaci, ortam, iptal);
                k.GonderimYaniti = yanit.DonusMetni;
                k.Hata = yanit.HataMesaji;
                _gecmis.Kaydet(k);
                _log.LogInformation("GİB gönderim yanıtı: {Paket} → {Yanit} {Hata}", k.PaketId, yanit.DonusMetni, yanit.HataMesaji);
                if (yanit.HataVar)
                    return k;

                try
                {
                    await DurumuBekleAsync(k, imzaci, ortam, iptal);
                }
                catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or GonderimHatasi or KartHatasi)
                {
                    // Dosya GİB'e ulaştı; durum sonradan sorgulanabilir.
                    _log.LogWarning("Durum sorgulanamadı: {Paket} {Mesaj}", k.PaketId, ex.Message);
                }
                return k;
            });
        }
        catch (Exception ex) when (ex is KartHatasi or GonderimHatasi && kayit?.GonderimYaniti == null)
        {
            _log.LogWarning("Gönderim yapılamadı: {Mesaj}", ex.Message);
            if (kayit != null)
            {
                kayit.Hata = ex.Message;
                _gecmis.Kaydet(kayit);
            }
            _onbellek.Set(id, veri, OnizlemeSuresi);
            return OnizlemeyeDon(id, ex.Message);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "GİB'e bağlanılamadı");
            if (kayit == null)
            {
                _onbellek.Set(id, veri, OnizlemeSuresi);
                return OnizlemeyeDon(id, "GİB'e bağlanılamadı: " + ex.Message + " İnternet bağlantısını kontrol edip tekrar deneyin.");
            }
            // İstek gitmiş olabilir; sonucu bilinmiyor. Tekrar göndermeden önce durum sorgulanmalı.
            kayit.Hata = "Gönderim sırasında bağlantı koptu; GİB'e ulaşıp ulaşmadığı bilinmiyor. "
                         + "Tekrar göndermeden önce bu paket ID ile durum sorgulayın. (" + ex.Message + ")";
            kayit.SonucBilinmiyor = true;
            _gecmis.Kaydet(kayit);
        }

        return RedirectToAction(nameof(Sonuc), new { id = kayit!.PaketId });
    }

    [HttpGet]
    public IActionResult Sonuc(string id)
    {
        var kayit = _gecmis.Bul(id);
        if (kayit == null)
            return RedirectToAction("Index", "Gecmis");

        // GİB'e giden satırları arşivlenmiş paketten göster.
        var paketYolu = _gecmis.PaketYolu(kayit.PaketId);
        if (System.IO.File.Exists(paketYolu))
        {
            try
            {
                ViewBag.Satirlar = PaketOkuyucu.Satirlar(System.IO.File.ReadAllBytes(paketYolu));
            }
            catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or FormatException)
            {
                _log.LogWarning("Arşivlenmiş paket okunamadı: {Paket} {Mesaj}", kayit.PaketId, ex.Message);
            }
        }
        return View(kayit);
    }

    /// <summary>Boş Excel şablonu (plaka, kwh, tl).</summary>
    [HttpGet]
    public IActionResult Sablon()
    {
        using var kitap = new ClosedXML.Excel.XLWorkbook();
        var sayfa = kitap.AddWorksheet("Rapor");
        sayfa.Cell(1, 1).Value = "plaka";
        sayfa.Cell(1, 2).Value = "kwh";
        sayfa.Cell(1, 3).Value = "tl";
        sayfa.Cell(2, 1).Value = "34ABC123";
        sayfa.Cell(2, 2).Value = 12.5;
        sayfa.Cell(2, 3).Value = 160.25;
        // Tüm yazılar siyah; başlık kalın.
        sayfa.Style.Font.FontColor = ClosedXML.Excel.XLColor.Black;
        sayfa.Range(1, 1, 2, 3).Style.Font.FontColor = ClosedXML.Excel.XLColor.Black;
        sayfa.Row(1).Style.Font.Bold = true;
        sayfa.Columns(1, 3).Width = 16;
        using var ms = new MemoryStream();
        kitap.SaveAs(ms);
        return File(ms.ToArray(), "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", "esu-rapor-sablonu.xlsx");
    }

    /// <summary>Hata sayfası. POST isteklerinde oluşan hatalarda da açılabilmesi için yöntem kısıtı yoktur.</summary>
    [IgnoreAntiforgeryToken]
    public IActionResult Error()
    {
        var hata = HttpContext.Features.Get<Microsoft.AspNetCore.Diagnostics.IExceptionHandlerPathFeature>();
        if (hata != null)
            _log.LogError(hata.Error, "Beklenmeyen hata: {Yol}", hata.Path);
        // Uygulama yalnızca bu bilgisayardan erişilebildiği için hata nedeni ekranda gösterilir.
        ViewBag.Neden = hata?.Error.GetBaseException().Message;
        return View(model: Activity.Current?.Id ?? HttpContext.TraceIdentifier);
    }

    /// <summary>400, 404, 405, 413 gibi durumlarda boş sayfa yerine açıklama gösterir.</summary>
    [IgnoreAntiforgeryToken]
    public IActionResult HttpDurumu([FromQuery] int kod)
    {
        // Gövdesi okunamayan isteklerde parametre bağlanamayabilir; asıl kod yanıtta korunur.
        if (kod == 0)
            kod = Response.StatusCode;
        // Çok büyük dosyada form okunamadığı için ASP.NET 400 döner; gövde boyutundan asıl nedeni çıkar.
        if (kod == 400 && Request.ContentLength > EnBuyukYukleme)
            kod = 413;
        ViewBag.Kod = kod;
        ViewBag.Mesaj = kod switch
        {
            400 => "İstek kabul edilmedi. Sayfa uygulama yeniden başlatıldığı için eskimiş olabilir; sayfayı yenileyip tekrar deneyin.",
            404 => "Aradığınız sayfa ya da dosya bulunamadı.",
            405 => "Bu işlem bu şekilde yapılamaz. Menüden ilgili sayfaya gidin.",
            413 => "Dosya çok büyük. En fazla 50 MB yüklenebilir.",
            _ => "İşlem tamamlanamadı.",
        };
        return View();
    }

    private async Task DurumuBekleAsync(GonderimKaydi kayit, IMuhurImzaci imzaci, GibOrtami ortam, CancellationToken iptal)
    {
        var bitis = DateTime.UtcNow + DurumBeklemeSuresi;
        while (true)
        {
            await Task.Delay(TimeSpan.FromSeconds(5), iptal);
            var yanit = await _gonderim.DurumSorgulaAsync(kayit.PaketId, imzaci, ortam, iptal);
            if (yanit.Durum != null)
            {
                _gecmis.DurumGuncelle(kayit.PaketId, yanit.Durum);
                var guncel = _gecmis.Bul(kayit.PaketId)!;
                kayit.DurumKodu = guncel.DurumKodu;
                kayit.DurumAciklamasi = guncel.DurumAciklamasi;
                kayit.SonSorgu = guncel.SonSorgu;
                _log.LogInformation("Paket durumu: {Paket} → {Kod}", kayit.PaketId, yanit.Durum.Kod);
                if (yanit.Durum.Tur != PaketDurumTuru.Isleniyor)
                    return;
            }
            if (DateTime.UtcNow > bitis)
                return;
        }
    }

    private IActionResult OnizlemeyeDon(Guid id, string hata)
    {
        TempData["Hata"] = hata;
        return RedirectToAction(nameof(Onizleme), new { id });
    }
}
