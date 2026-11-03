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
