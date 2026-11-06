using ClosedXML.Excel;
using GibEsu.Core.Rapor;

namespace GibEsu.Tests;

/// <summary>Gönderim sonrası indirilen "GİB'e giden" Excel: plaka başına toplam, yeniden yüklenebilir.</summary>
public class ToplamExcelTestleri
{
    private static readonly List<EsuSatiri> Satirlar = new()
    {
        new("34MIU906", 186.5m, 2387.20m),
        new("31E0025", 56.4m, 648.04m),
        new("34MIN407", 54.41m, 696.45m),
    };

    [Fact]
    public void YenidenYuklenincePlakalarVeToplamlarAyniKalir()
    {
        var excel = ToplamExcelUretici.Uret(Satirlar, new[] { ("Dönem", "2026-09") });
        var sonuc = EsuExcelOkuyucu.Oku(new MemoryStream(excel));

        Assert.True(sonuc.Gecerli, string.Join(" | ", sonuc.Hatalar));
        Assert.Equal(Satirlar.Select(s => (s.Plaka, s.Kwh, s.Tutar)).OrderBy(x => x.Plaka),
            sonuc.Satirlar.Select(s => (s.Plaka, s.Kwh, s.Tutar)).OrderBy(x => x.Plaka));
    }

    [Fact]
    public void PlakaMetinOlarakYazilirOzetteToplamVardir()
    {
        var excel = ToplamExcelUretici.Uret(Satirlar, new[] { ("Paket ID", "abc") });
        using var kitap = new XLWorkbook(new MemoryStream(excel));

        var rapor = kitap.Worksheet("Rapor");
        Assert.Equal(XLDataType.Text, rapor.Cell(3, 1).Value.Type);
        Assert.Equal("31E0025", rapor.Cell(3, 1).GetString());
        Assert.Equal(4, rapor.LastRowUsed()!.RowNumber());

        var ozet = kitap.Worksheet("Özet");
        Assert.Equal("abc", ozet.Cell(1, 2).GetString());
        Assert.Equal(3, ozet.Cell(2, 2).GetDouble());
        Assert.Equal(297.31, ozet.Cell(3, 2).GetDouble(), 2);
        Assert.Equal(3731.69, ozet.Cell(4, 2).GetDouble(), 2);
    }
}
