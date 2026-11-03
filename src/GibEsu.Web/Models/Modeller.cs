using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Web.Servisler;

namespace GibEsu.Web.Models;

public sealed class OnizlemeVerisi
{
    public required Guid Anahtar { get; init; }
    public required string ExcelAdi { get; init; }
    public required int Yil { get; init; }
    public required int Ay { get; init; }
    public required ExcelOkumaSonucu Sonuc { get; init; }

    /// <summary>Gönderimde paketle birlikte arşivlenir.</summary>
    public required byte[] ExcelIcerigi { get; init; }
    public required string ExcelOzeti { get; init; }

    public string Donem => $"{Yil:0000}-{Ay:00}";
    public string DonemAdi => Bicim.AyAdi(Yil, Ay);
    public RaporOzeti Ozet => new(Sonuc.Satirlar.Count, Sonuc.ToplamKwh, Sonuc.ToplamTutar);
}

public sealed class AnaSayfaModeli
{
    public required int Yil { get; init; }
    public required int Ay { get; init; }
    public string? Hata { get; init; }

    /// <summary>Gönderilmesi gereken dönem (geçen ay) ve son tarihi.</summary>
    public required string BeklenenDonemAdi { get; init; }
    public required DateTime SonTarih { get; init; }
    public required int KalanGun { get; init; }
    /// <summary>Beklenen dönem için bu uygulamadan yapılmış canlı gönderimler.</summary>
    public List<GonderimKaydi> DonemGonderimleri { get; init; } = new();

    public List<GonderimKaydi> SonucBekleyenler { get; init; } = new();
    public List<KartSertifikasi> SuresiYaklasanlar { get; init; } = new();
}

/// <summary>Kart seçimi ve PIN alanlarını gösteren ortak bölüm.</summary>
public sealed class KartSecimModeli
{
    public List<KartSertifikasi> Sertifikalar { get; init; } = new();
    public string? KartHatasi { get; init; }
    public GibOrtami Ortam { get; init; }
    public bool OrtamSecilebilir { get; init; } = true;
}

public sealed class OnizlemeModeli
{
    public required OnizlemeVerisi Veri { get; init; }
    public required KartSecimModeli Kart { get; init; }
    public List<GonderimKaydi> OncekiGonderimler { get; init; } = new();
    public string? Hata { get; init; }

    /// <summary>Önceki dönemin canlı gönderimi ve ona göre farklar (geçmiş yoksa boş).</summary>
    public GonderimKaydi? OncekiDonem { get; init; }
    public List<DonemFarki> Farklar { get; init; } = new();

    /// <summary>Aynı Excel dosyasıyla ya da aynı toplamlarla daha önce yapılmış gönderimler.</summary>
    public List<GonderimKaydi> AyniVeriGonderimleri { get; init; } = new();
}

public sealed class DurumModeli
{
    public string? PaketId { get; init; }
    public required KartSecimModeli Kart { get; init; }
    public PaketDurumu? Durum { get; init; }
    public string? Hata { get; init; }
}

public sealed class SistemModeli
{
    public required string Surum { get; init; }
    public required string VeriKlasoru { get; init; }
    public required string LogKlasoru { get; init; }
    public string? VeriKlasoruUyarisi { get; init; }
    public required List<(string Yol, bool Var)> Suruculer { get; init; }
    public required KartSecimModeli Kart { get; init; }
    public required SaatBilgisi Saat { get; init; }
    /// <summary>Basarili null ise ortam kapalı olduğu için denenmedi.</summary>
    public required List<(GibOrtami Ortam, string Sonuc, bool? Basarili)> Baglantilar { get; init; }
    public List<string> TestSonuclari { get; init; } = new();
    public bool? TestBasarili { get; init; }
    public string? TestBasligi { get; init; }

    /// <summary>GİB imza testinde sorgulanacak paket (en son canlı gönderim).</summary>
    public string? GibTestPaketId { get; init; }
}

public static class Bicim
{
    private static readonly System.Globalization.CultureInfo Tr = new("tr-TR");

    public static string Para(decimal d) => d.ToString("N2", Tr) + " TL";
    public static string Sayi(decimal d) => d.ToString("N2", Tr);
    public static string AyAdi(int yil, int ay) => new DateTime(yil, ay, 1).ToString("MMMM yyyy", Tr);
    public static string Tarih(DateTime t) => t.ToString("dd.MM.yyyy HH:mm", Tr);

    public static string DurumSinifi(PaketDurumTuru tur) => tur switch
    {
        PaketDurumTuru.Basarili => "basarili",
        PaketDurumTuru.Isleniyor => "bekliyor",
        PaketDurumTuru.Hata => "hata",
        _ => "notr",
    };
}

/// <summary>Geçmiş sayfası: filtrelenmiş gönderimler ve isteğe bağlı plaka araması.</summary>
public sealed class GecmisModeli
{
    public List<GonderimKaydi> Kayitlar { get; init; } = new();
    public int ToplamKayit { get; init; }
    public List<string> Donemler { get; init; } = new();
    public string? BozukYedek { get; init; }

    public string? Donem { get; init; }
    public GibOrtami? Ortam { get; init; }
    public string? Durum { get; init; }
    public string? Plaka { get; init; }

    /// <summary>Plaka arandıysa bulunan satırlar (gönderim + o gönderimdeki satır).</summary>
    public List<(GonderimKaydi Kayit, EsuSatiri Satir)>? PlakaSonuclari { get; init; }
    public bool FiltreVar => Donem != null || Ortam != null || Durum != null || Plaka != null;
}
