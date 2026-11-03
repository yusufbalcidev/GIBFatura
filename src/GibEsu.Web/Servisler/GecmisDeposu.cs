using System.Text.Json;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using Microsoft.Extensions.Options;

namespace GibEsu.Web.Servisler;

public sealed class GonderimKaydi
{
    public string PaketId { get; set; } = "";
    public DateTime Tarih { get; set; }
    public string Donem { get; set; } = "";
    public GibOrtami Ortam { get; set; }
    public string Vkn { get; set; } = "";
    public string ExcelAdi { get; set; } = "";
    public int SatirSayisi { get; set; }
    public decimal ToplamKwh { get; set; }
    public decimal ToplamTutar { get; set; }
    public string? GonderimYaniti { get; set; }
    public string? Hata { get; set; }
    public int? DurumKodu { get; set; }
    public string? DurumAciklamasi { get; set; }
    public DateTime? SonSorgu { get; set; }

    /// <summary>Yüklenen Excel dosyasının SHA-256 özeti; aynı dosyanın tekrar yüklenmesini yakalamak için.</summary>
    public string? ExcelOzeti { get; set; }

    /// <summary>Gönderim sırasında bağlantı koptu; dosyanın GİB'e ulaşıp ulaşmadığı bilinmiyor.</summary>
    public bool SonucBilinmiyor { get; set; }

    public PaketDurumTuru DurumTuru => DurumKodu is int k ? DurumKodlari.Tur(k) : PaketDurumTuru.Bilinmiyor;
    public bool GibeUlasti => GonderimYaniti != null && Hata == null;

    /// <summary>GİB'e ulaşmış ama işlenme sonucu (30 veya hata kodu) henüz alınmamış ya da ulaşıp ulaşmadığı bilinmiyor.</summary>
    [System.Text.Json.Serialization.JsonIgnore]
    public bool SonucBekleniyor => SonucBilinmiyor
        || (GibeUlasti && DurumTuru is PaketDurumTuru.Isleniyor or PaketDurumTuru.Bilinmiyor);

    [System.Text.Json.Serialization.JsonIgnore]
    public RaporOzeti Ozet => new(SatirSayisi, ToplamKwh, ToplamTutar);
}

/// <summary>Gönderim geçmişi ve imzalı paket arşivi (yerel diskte).</summary>
public sealed class GecmisDeposu
{
    private static readonly JsonSerializerOptions JsonAyarlari = new() { WriteIndented = true };
    private readonly object _kilit = new();
    private readonly string _dosya;
    private readonly string _paketKlasoru;

    private readonly ILogger<GecmisDeposu> _log;

    public GecmisDeposu(IOptions<Ayarlar> ayarlar, ILogger<GecmisDeposu> log)
    {
        _log = log;
        var klasor = ayarlar.Value.VeriKlasoruYolu;
        _paketKlasoru = Path.Combine(klasor, "paketler");
        Directory.CreateDirectory(_paketKlasoru);
        _dosya = Path.Combine(klasor, "gecmis.json");
    }

    public List<GonderimKaydi> Tumu()
    {
        lock (_kilit)
            return Oku().OrderByDescending(k => k.Tarih).ToList();
    }

    public GonderimKaydi? Bul(string paketId)
    {
        lock (_kilit)
            return Oku().FirstOrDefault(k => string.Equals(k.PaketId, paketId, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Aynı dönem ve ortam için GİB'e ulaşmış, reddedilmemiş gönderimler.</summary>
    public List<GonderimKaydi> DonemGonderimleri(string donem, GibOrtami ortam)
    {
        lock (_kilit)
        {
            return Oku()
                .Where(k => k.Donem == donem && k.Ortam == ortam && k.GibeUlasti && k.DurumTuru != PaketDurumTuru.Hata)
                .ToList();
        }
    }

    /// <summary>Verilen dönemden önceki en son canlı gönderim (karşılaştırma için).</summary>
    public GonderimKaydi? OncekiDonemKaydi(string donem)
    {
        lock (_kilit)
        {
            return Oku()
                .Where(k => k.Ortam == GibOrtami.Canli && k.GibeUlasti && k.DurumTuru != PaketDurumTuru.Hata
                            && string.CompareOrdinal(k.Donem, donem) < 0)
                .OrderByDescending(k => k.Donem, StringComparer.Ordinal).ThenByDescending(k => k.Tarih)
                .FirstOrDefault();
        }
    }

    /// <summary>
    /// Başka bir dönem için aynı Excel dosyasıyla ya da birebir aynı toplamlarla yapılmış gönderimler (yanlış dosya yüklenmiş olabilir).
    /// Aynı dönemin gönderimleri sayılmaz: testten sonra canlıya aynı dosyayı göndermek normaldir; aynı dönem ayrıca mükerrer kontrolüne takılır.
    /// </summary>
    public List<GonderimKaydi> AyniVeriIleGonderilenler(string excelOzeti, RaporOzeti ozet, string donem)
    {
        lock (_kilit)
        {
            return Oku()
                .Where(k => k.GibeUlasti && k.Donem != donem && (k.ExcelOzeti == excelOzeti || k.Ozet == ozet))
                .OrderByDescending(k => k.Tarih)
                .ToList();
        }
    }

    public void Kaydet(GonderimKaydi kayit)
    {
        lock (_kilit)
        {
            var liste = Oku();
            liste.RemoveAll(k => string.Equals(k.PaketId, kayit.PaketId, StringComparison.OrdinalIgnoreCase));
            liste.Add(kayit);
            var gecici = _dosya + ".tmp";
            Directory.CreateDirectory(Path.GetDirectoryName(_dosya)!);
            File.WriteAllText(gecici, JsonSerializer.Serialize(liste, JsonAyarlari));
            File.Move(gecici, _dosya, overwrite: true);
        }
    }

    public void DurumGuncelle(string paketId, PaketDurumu durum)
    {
        lock (_kilit)
        {
            var kayit = Bul(paketId);
            if (kayit == null)
                return;
            kayit.DurumKodu = durum.Kod;
            kayit.DurumAciklamasi = durum.Aciklama + (durum.GibAciklamasi != null ? " – " + durum.GibAciklamasi : "");
            kayit.SonSorgu = DateTime.Now;
            if (kayit.SonucBilinmiyor)
            {
                // GİB bu paket için durum döndüyse dosya ulaşmış demektir.
                kayit.SonucBilinmiyor = false;
                kayit.Hata = null;
                kayit.GonderimYaniti ??= "Bağlantı kopmuştu; durum sorgusuyla GİB'e ulaştığı doğrulandı.";
            }
            Kaydet(kayit);
        }
    }

    public string PaketYolu(string paketId) => Path.Combine(_paketKlasoru, paketId + ".zip");

    public void PaketiSakla(string paketId, byte[] zip)
    {
        // Klasör açılışta oluşturulsa da sonradan silinmiş olabilir (ör. güvenlik yazılımı konteyneri sıfırladığında).
        Directory.CreateDirectory(_paketKlasoru);
        File.WriteAllBytes(PaketYolu(paketId), zip);
    }

    /// <summary>Raporun kaynağı olan Excel; "ne gönderdik" sorusu için paketle birlikte saklanır.</summary>
    public string ExcelYolu(string paketId) => Path.Combine(_paketKlasoru, paketId + ".xlsx");

    public void ExceliSakla(string paketId, byte[] excel)
    {
        Directory.CreateDirectory(_paketKlasoru);
        File.WriteAllBytes(ExcelYolu(paketId), excel);
    }

    private List<GonderimKaydi> Oku()
    {
        if (!File.Exists(_dosya))
            return new List<GonderimKaydi>();
        try
        {
            return JsonSerializer.Deserialize<List<GonderimKaydi>>(File.ReadAllText(_dosya), JsonAyarlari) ?? new();
        }
        catch (JsonException ex)
        {
            // Bozuk dosya uygulamayı kilitlemesin: yedeğini al, boş geçmişle devam et.
            var yedek = _dosya + $".bozuk-{DateTime.Now:yyyyMMddHHmmss}";
            File.Copy(_dosya, yedek, overwrite: true);
            File.Delete(_dosya);
            _log.LogError(ex, "Geçmiş dosyası okunamadı; yedeği {Yedek} olarak alındı.", yedek);
            BozukYedek = yedek;
            return new List<GonderimKaydi>();
        }
    }

    /// <summary>Geçmiş dosyası bozuk bulunup yedeklendiyse yedeğin yolu.</summary>
    public string? BozukYedek { get; private set; }
}
