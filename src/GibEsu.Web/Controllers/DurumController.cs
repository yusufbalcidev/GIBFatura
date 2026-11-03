using GibEsu.Core.Imza;
using GibEsu.Core.Servis;
using GibEsu.Web.Models;
using GibEsu.Web.Servisler;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;

namespace GibEsu.Web.Controllers;

public class DurumController : Controller
{
    private readonly KartServisi _kart;
    private readonly GecmisDeposu _gecmis;
    private readonly EsuGonderimServisi _gonderim;
    private readonly Ayarlar _ayarlar;
    private readonly ILogger<DurumController> _log;

    public DurumController(KartServisi kart, GecmisDeposu gecmis, EsuGonderimServisi gonderim, IOptions<Ayarlar> ayarlar,
        ILogger<DurumController> log)
    {
        _kart = kart;
        _gecmis = gecmis;
        _gonderim = gonderim;
        _ayarlar = ayarlar.Value;
        _log = log;
    }

    [HttpGet]
    public async Task<IActionResult> Index(string? paketId, GibOrtami? ortam)
    {
        var kayitOrtami = paketId == null ? null : _gecmis.Bul(paketId)?.Ortam;
        return View(await ModelAsync(paketId, ortam ?? kayitOrtami ?? _ayarlar.EtkinVarsayilanOrtam, null, null));
    }

    [HttpPost]
    public async Task<IActionResult> Sorgula(string paketId, string sertifika, string pin, GibOrtami ortam, bool sonDeneme)
    {
        paketId = (paketId ?? "").Trim();
        if (ModelState[nameof(ortam)]?.Errors.Count > 0 || !Enum.IsDefined(ortam))
            return View("Index", await ModelAsync(paketId, _ayarlar.EtkinVarsayilanOrtam, null, "GİB ortamı geçersiz."));
        if (!_ayarlar.OrtamIzinli(ortam))
            return View("Index", await ModelAsync(paketId, GibOrtami.Test, null, Ayarlar.CanliKapaliMesaji));
        if (!Guid.TryParse(paketId, out _))
            return View("Index", await ModelAsync(paketId, ortam, null, "Paket ID UUID biçiminde olmalı (ör. 550e8400-e29b-41d4-a716-446655440000)."));

        try
        {
            var yanit = await _kart.ImzaciIleAsync(sertifika, pin, sonDeneme,
                imzaci => _gonderim.DurumSorgulaAsync(paketId, imzaci, ortam, CancellationToken.None));
            if (yanit.Durum == null)
            {
                _log.LogWarning("Durum sorgusu: {Paket} ({Ortam}) → {Hata}", paketId, ortam, yanit.HataMesaji);
                return View("Index", await ModelAsync(paketId, ortam, null, yanit.HataMesaji ?? "GİB'den durum alınamadı."));
            }

            _gecmis.DurumGuncelle(paketId, yanit.Durum);
            _log.LogInformation("Durum sorgusu: {Paket} ({Ortam}) → {Kod}", paketId, ortam, yanit.Durum.Kod);
            return View("Index", await ModelAsync(paketId, ortam, yanit.Durum, null));
        }
        catch (Exception ex) when (ex is KartHatasi or GonderimHatasi)
        {
            // Kart/PIN hatasında istek GİB'e hiç gitmez; SOAP kaydı oluşmadığı için nedeni burada yazılır.
            _log.LogWarning("Durum sorgusu yapılamadı: {Paket} ({Ortam}) {Mesaj}", paketId, ortam, ex.Message);
            return View("Index", await ModelAsync(paketId, ortam, null, ex.Message));
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _log.LogError(ex, "Durum sorgusunda bağlantı hatası");
            return View("Index", await ModelAsync(paketId, ortam, null, "GİB'e bağlanılamadı: " + ex.Message));
        }
    }

    private async Task<DurumModeli> ModelAsync(string? paketId, GibOrtami ortam, PaketDurumu? durum, string? hata)
    {
        var (sertifikalar, kartHatasi) = await _kart.ListeleAsync();
        return new DurumModeli
        {
            PaketId = paketId,
            Durum = durum,
            Hata = hata,
            Kart = new KartSecimModeli { Sertifikalar = sertifikalar, KartHatasi = kartHatasi, Ortam = ortam },
        };
    }
}
