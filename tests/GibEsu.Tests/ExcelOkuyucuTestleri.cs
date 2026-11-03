using GibEsu.Core.Rapor;

namespace GibEsu.Tests;

public class ExcelOkuyucuTestleri
{
    [Fact]
    public void AyniPlakaToplanirVePlakaNormallesir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "Plaka", "KWH", "TL" },
            new object?[] { "34 abc 123", 10.5, 100.25 },
            new object?[] { "34-ABC-123", 4.5, 50.75 },
            new object?[] { "06xyz99", 1, 2 }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Equal(3, sonuc.OkunanSatirSayisi);
        Assert.Equal(2, sonuc.Satirlar.Count);
        var s = sonuc.Satirlar.Single(x => x.Plaka == "34ABC123");
        Assert.Equal(15.00m, s.Kwh);
        Assert.Equal(151.00m, s.Tutar);
        Assert.Equal(16.00m, sonuc.ToplamKwh);
        Assert.Equal(153.00m, sonuc.ToplamTutar);
    }

    [Fact]
    public void MetinOlarakYazilmisSayilarOkunur()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", "1.234,56", "1234.50" },
            new object?[] { "34ABC124", "12,5", "1,234.75" }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Equal(1234.56m, sonuc.Satirlar[0].Kwh);
        Assert.Equal(1234.50m, sonuc.Satirlar[0].Tutar);
        Assert.Equal(12.5m, sonuc.Satirlar[1].Kwh);
        Assert.Equal(1234.75m, sonuc.Satirlar[1].Tutar);
    }

    [Fact]
    public void BaslikIlkSatirdaDegilseDeBulunur()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "Eylül 2026 Şarj Raporu" },
            new object?[] { null },
            new object?[] { "Araç Plakası", "kWh", "Toplam Tutar" },
            new object?[] { "34ABC123", 1, 2 }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Single(sonuc.Satirlar);
    }

    [Theory]
    [InlineData(0.0, 10.0, "sıfırdan büyük")]
    [InlineData(-1.0, 10.0, "sıfırdan büyük")]
    [InlineData(1.234, 10.0, "2 ondalık")]
    [InlineData(1.0, 10.555, "2 ondalık")]
    public void GecersizSayilarHataVerir(double kwh, double tl, string beklenen)
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", kwh, tl }));

        Assert.False(sonuc.Gecerli);
        Assert.Contains(sonuc.Hatalar, h => h.Contains(beklenen) && h.StartsWith("2. satır"));
    }

    [Fact]
    public void BosVeGecersizPlakaHataVerir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { null, 1, 2 },
            new object?[] { "34/ABC", 1, 2 }));

        Assert.False(sonuc.Gecerli);
        Assert.Contains(sonuc.Hatalar, h => h.StartsWith("2. satır: plaka boş"));
        Assert.Contains(sonuc.Hatalar, h => h.StartsWith("3. satır: plaka geçersiz"));
    }

    [Fact]
    public void YabanciPlakaUyariVerirAmaKabulEdilir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "WOB123AB", 1, 2 }));

        Assert.True(sonuc.Gecerli);
        Assert.Single(sonuc.Uyarilar);
    }

    [Fact]
    public void ZorunluSutunYoksaHataVerir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh" },
            new object?[] { "34ABC123", 1 }));

        Assert.False(sonuc.Gecerli);
        Assert.Contains("'plaka', 'kwh' ve 'tl'", sonuc.Hatalar[0]);
    }

    [Fact]
    public void RoamingSutunlariOkunur()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl", "roaming vkn", "roaming unvan" },
            new object?[] { "34ABC123", 1, 2, "1234567890", "ÖRNEK ŞARJ A.Ş." },
            new object?[] { "34ABC123", 3, 4, null, null }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Equal(2, sonuc.Satirlar.Count);
        Assert.Contains(sonuc.Satirlar, s => s.RoamingVkn == "1234567890" && s.RoamingUnvan == "ÖRNEK ŞARJ A.Ş.");
    }

    [Theory]
    [InlineData(1e30)]
    [InlineData(1e10)]
    public void CokBuyukSayiCokertmezHataVerir(double kwh)
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", kwh, 10 }));

        Assert.False(sonuc.Gecerli);
        Assert.Contains(sonuc.Hatalar, h => h.Contains("çok büyük"));
    }

    [Theory]
    [InlineData("12.500")]
    [InlineData("1.250.000")]
    [InlineData("₺12.500")]
    public void BinlikNoktaliMetinBelirsizSayilir(string tutar)
    {
        // "12.500" Türkçede 12500, İngilizcede 12,5 demek: tahmin etmek yerine hata verilir.
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", 10, tutar }));

        Assert.False(sonuc.Gecerli);
        Assert.Contains(sonuc.Hatalar, h => h.Contains("belirsiz"));
    }

    [Theory]
    [InlineData("₺1.250,75", 1250.75)]
    [InlineData("1.250,75 TL", 1250.75)]
    [InlineData("1250,75 TRY", 1250.75)]
    [InlineData("12.5", 12.5)]
    public void ParaIsaretliMetinOkunur(string tutar, double beklenen)
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", 10, tutar }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Equal((decimal)beklenen, sonuc.Satirlar[0].Tutar);
    }

    [Fact]
    public void OlagandisiYuksekToplamUyariVerir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", 10, 2_000_000 }));

        Assert.True(sonuc.Gecerli);
        Assert.Contains(sonuc.Uyarilar, u => u.Contains("olağandışı"));
    }

    [Fact]
    public void FormulHucreleriHesaplanmisDegerleOkunur()
    {
        using var kitap = new ClosedXML.Excel.XLWorkbook();
        var sayfa = kitap.AddWorksheet("S");
        sayfa.Cell(1, 1).Value = "plaka"; sayfa.Cell(1, 2).Value = "kwh"; sayfa.Cell(1, 3).Value = "tl";
        sayfa.Cell(2, 1).Value = "34ABC123";
        sayfa.Cell(2, 2).FormulaA1 = "=5+5.5";
        sayfa.Cell(2, 3).FormulaA1 = "=B2*12";
        sayfa.Cell(3, 1).Value = "34ABC124";
        sayfa.Cell(3, 2).FormulaA1 = "=1/0";
        sayfa.Cell(3, 3).Value = 5;
        using var ms = new MemoryStream();
        kitap.SaveAs(ms);
        ms.Position = 0;

        var sonuc = EsuExcelOkuyucu.Oku(ms);

        Assert.Contains(sonuc.Hatalar, h => h.StartsWith("3. satır: kWh sayı değil"));
        Assert.DoesNotContain(sonuc.Hatalar, h => h.StartsWith("2. satır"));
    }

    [Fact]
    public void ExcelDegilseHataVerir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(new MemoryStream(new byte[] { 1, 2, 3 }));

        Assert.False(sonuc.Gecerli);
        Assert.StartsWith("Excel dosyası açılamadı", sonuc.Hatalar[0]);
    }
}
