namespace GibEsu.Core.Rapor;

public sealed record RaporOzeti(int PlakaSayisi, decimal ToplamKwh, decimal ToplamTutar);

public sealed record DonemFarki(string Ad, decimal Onceki, decimal Simdiki)
{
    /// <summary>Önceki döneme göre yüzde değişim; önceki değer 0 ise hesaplanamaz.</summary>
    public decimal? Yuzde => Onceki == 0 ? null : Math.Round((Simdiki - Onceki) / Onceki * 100, 1);

    public bool Olagandisi => Yuzde is not decimal y || Math.Abs(y) > DonemKarsilastirma.SapmaEsigiYuzde;
}

/// <summary>
/// Yanlış Excel'in en belirgin işareti önceki aydan çok farklı olmasıdır (eksik dosya, yanlış ay, iki kez eklenmiş satırlar).
/// </summary>
public static class DonemKarsilastirma
{
    public const decimal SapmaEsigiYuzde = 30m;

    public static List<DonemFarki> Karsilastir(RaporOzeti onceki, RaporOzeti simdiki) => new()
    {
        new("Plaka sayısı", onceki.PlakaSayisi, simdiki.PlakaSayisi),
        new("Toplam kWh", onceki.ToplamKwh, simdiki.ToplamKwh),
        new("Toplam tutar", onceki.ToplamTutar, simdiki.ToplamTutar),
    };
}
