namespace GibEsu.Core.Rapor;

/// <summary>Raporun bir satırı: plaka bazında aylık toplam.</summary>
public sealed record EsuSatiri(
    string Plaka,
    decimal Kwh,
    decimal Tutar,
    string? AracKimlikNo = null,
    string? RoamingVkn = null,
    string? RoamingUnvan = null);

public sealed class ExcelOkumaSonucu
{
    public List<EsuSatiri> Satirlar { get; } = new();
    public List<string> Hatalar { get; } = new();
    public List<string> Uyarilar { get; } = new();

    /// <summary>Excel'de okunan dolu satır sayısı (gruplamadan önce).</summary>
    public int OkunanSatirSayisi { get; set; }

    public decimal ToplamKwh => Satirlar.Sum(s => s.Kwh);
    public decimal ToplamTutar => Satirlar.Sum(s => s.Tutar);
    public bool Gecerli => Hatalar.Count == 0 && Satirlar.Count > 0;
}
