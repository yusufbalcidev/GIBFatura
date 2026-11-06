using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Web.Models;
using GibEsu.Web.Servisler;
using Microsoft.AspNetCore.Mvc;

namespace GibEsu.Web.Controllers;

public class GecmisController : Controller
{
    private readonly GecmisDeposu _gecmis;
    private readonly ILogger<GecmisController> _log;

    public GecmisController(GecmisDeposu gecmis, ILogger<GecmisController> log)
    {
        _gecmis = gecmis;
        _log = log;
    }

    /// <summary>Gönderim listesi. Dönem, ortam ve duruma göre süzülür; plaka verilirse arşivlenmiş paketlerde aranır.</summary>
    [HttpGet]
    public IActionResult Index(string? donem, GibOrtami? ortam, string? durum, string? plaka)
    {
        var tumu = _gecmis.Tumu();
        donem = string.IsNullOrWhiteSpace(donem) ? null : donem;
        durum = durum is "basarili" or "bekliyor" or "hata" ? durum : null;
        var arananPlaka = string.IsNullOrWhiteSpace(plaka) ? null : EsuExcelOkuyucu.PlakaNormallestir(plaka);

        var kayitlar = tumu
            .Where(k => donem == null || k.Donem == donem)
            .Where(k => ortam == null || k.Ortam == ortam)
            .Where(k => durum switch
            {
                "basarili" => k.DurumTuru == PaketDurumTuru.Basarili,
                "bekliyor" => k.SonucBekleniyor,
                "hata" => k.Hata != null || k.DurumTuru == PaketDurumTuru.Hata,
                _ => true,
            })
            .ToList();

        List<(GonderimKaydi, EsuSatiri)>? plakaSonuclari = null;
        if (arananPlaka != null)
        {
            // Ayda bir paket olduğu için tüm arşivi taramak hızlıdır (yıllık birkaç MB).
            plakaSonuclari = new();
            foreach (var k in kayitlar.Where(k => k.GibeUlasti || k.SonucBilinmiyor))
            {
                var yol = _gecmis.PaketYolu(k.PaketId);
                if (!System.IO.File.Exists(yol))
                    continue;
                try
                {
                    plakaSonuclari.AddRange(PaketOkuyucu.Satirlar(System.IO.File.ReadAllBytes(yol))
                        .Where(s => s.Plaka.Contains(arananPlaka, StringComparison.Ordinal))
                        .Select(s => (k, s)));
                }
                catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or FormatException)
                {
                    _log.LogWarning("Plaka aramasında paket okunamadı: {Paket} {Mesaj}", k.PaketId, ex.Message);
                }
            }
        }

        return View(new GecmisModeli
        {
            Kayitlar = kayitlar,
            ToplamKayit = tumu.Count,
            Donemler = tumu.Select(k => k.Donem).Distinct().OrderByDescending(d => d, StringComparer.Ordinal).ToList(),
            BozukYedek = _gecmis.BozukYedek,
            Donem = donem,
            Ortam = ortam,
            Durum = durum,
            Plaka = arananPlaka,
            PlakaSonuclari = plakaSonuclari,
        });
    }

    /// <summary>GİB'e gönderilen imzalı paketi (zip) indirir.</summary>
    [HttpGet]
    public IActionResult Indir(Guid id)
    {
        var paketId = id.ToString("D");
        var yol = _gecmis.PaketYolu(paketId);
        if (!System.IO.File.Exists(yol))
            return NotFound();
        return PhysicalFile(yol, "application/zip", paketId + ".zip");
    }

    /// <summary>
    /// GİB'e giden hâli: imzalı paketteki satırlardan plaka başına toplam Excel'i üretir.
    /// İlk sayfa (Plaka, kWh, Tutar (TL)) uygulamaya yeniden yüklenebilir biçimdedir; toplamlar "Özet" sayfasındadır.
    /// </summary>
    [HttpGet]
    public IActionResult ToplamExcel(Guid id)
    {
        var paketId = id.ToString("D");
        var yol = _gecmis.PaketYolu(paketId);
        if (!System.IO.File.Exists(yol))
            return NotFound();
        List<EsuSatiri> satirlar;
        try
        {
            satirlar = PaketOkuyucu.Satirlar(System.IO.File.ReadAllBytes(yol));
        }
        catch (Exception ex) when (ex is IOException or InvalidDataException or System.Xml.XmlException or FormatException)
        {
            _log.LogWarning("Toplam Excel için paket okunamadı: {Paket} {Mesaj}", paketId, ex.Message);
            return Problem("İmzalı paket okunamadı: " + ex.Message);
        }
        var kayit = _gecmis.Bul(paketId);
        var bilgiler = new List<(string, string)> { ("Paket ID", paketId) };
        if (kayit != null)
        {
            bilgiler.Add(("Dönem", kayit.Donem));
            bilgiler.Add(("Ortam", kayit.Ortam.ToString()));
            bilgiler.Add(("VKN", kayit.Vkn));
            bilgiler.Add(("Kaynak Excel", kayit.ExcelAdi));
            bilgiler.Add(("Gönderim zamanı", kayit.Tarih.ToString("dd.MM.yyyy HH:mm")));
            bilgiler.Add(("GİB durumu", kayit.DurumKodu == null ? "–" : $"{kayit.DurumKodu} {kayit.DurumAciklamasi}".Trim()));
        }
        var icerik = ToplamExcelUretici.Uret(satirlar, bilgiler);
        var ad = kayit == null ? $"ESU_{paketId}_toplam.xlsx" : $"ESU_{kayit.Donem}_{kayit.Ortam}_toplam.xlsx";
        return File(icerik, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", ad);
    }

    /// <summary>Raporun hazırlandığı Excel dosyasını (arşivlenmişse) indirir.</summary>
    [HttpGet]
    public IActionResult Excel(Guid id)
    {
        var paketId = id.ToString("D");
        var yol = _gecmis.ExcelYolu(paketId);
        if (!System.IO.File.Exists(yol))
            return NotFound();
        var ad = _gecmis.Bul(paketId)?.ExcelAdi;
        return PhysicalFile(yol, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
            string.IsNullOrWhiteSpace(ad) ? paketId + ".xlsx" : ad);
    }
}
