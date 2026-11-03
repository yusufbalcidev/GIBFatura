using System.Globalization;
using System.Xml;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Core.Xml;

namespace GibEsu.Tests;

/// <summary>
/// ESÜ Kılavuzu 3.0, 2.2: aylık bildirimde veriler "plaka bazında tek bir satır" olarak alınır.
/// Excel seans seans hazırlanabilir; aynı aracın seansları toplanmalıdır.
/// </summary>
public class PlakaBirlestirmeTestleri
{
    [Fact]
    public void FarkliYazilmisAyniPlakaninSeanslariToplanir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34 ABC 123", 10.0, 120.0 },
            new object?[] { "34-abc-123", 15.0, 180.0 },
            new object?[] { " 34abc123 ", 8.0, 100.0 },
            new object?[] { "34.ABC.123", 1.25, 15.5 },
            new object?[] { "06XYZ99", 5.0, 60.0 }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Equal(5, sonuc.OkunanSatirSayisi);
        Assert.Equal(2, sonuc.Satirlar.Count);
        var arac = sonuc.Satirlar.Single(s => s.Plaka == "34ABC123");
        Assert.Equal(34.25m, arac.Kwh);
        Assert.Equal(415.50m, arac.Tutar);
        Assert.Equal(39.25m, sonuc.ToplamKwh);
        Assert.Equal(475.50m, sonuc.ToplamTutar);
    }

    [Fact]
    public void TurkceIHarfleriAyniPlakaSayilir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34MİA906", 1.0, 10.0 },
            new object?[] { "34MIA906", 2.0, 20.0 },
            new object?[] { "34mia906", 3.0, 30.0 },
            new object?[] { "34mıa906", 4.0, 40.0 }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        var s = Assert.Single(sonuc.Satirlar);
        Assert.Equal("34MIA906", s.Plaka);
        Assert.Equal(10.0m, s.Kwh);
        Assert.Equal(100.0m, s.Tutar);
    }

    [Fact]
    public void KurusluToplamlarKaybolmaz()
    {
        // Excel sayıları double tutar (0.1 + 0.2 = 0.30000000000000004); toplam yine kuruşu kuruşuna doğru olmalı.
        var satirlar = new List<object?[]> { new object?[] { "plaka", "kwh", "tl" } };
        for (var i = 0; i < 1000; i++)
            satirlar.Add(new object?[] { "34ABC123", i % 2 == 0 ? 0.1 : 0.2, i % 3 == 0 ? 0.01 : 12.37 });

        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(satirlar.ToArray()));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar.Take(3)));
        var s = Assert.Single(sonuc.Satirlar);
        Assert.Equal(150.00m, s.Kwh);
        Assert.Equal(334 * 0.01m + 666 * 12.37m, s.Tutar);
    }

    [Fact]
    public void RoamingSeanslariAyriSatirdaKendiArasindaToplanir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl", "roaming vkn", "roaming unvan" },
            new object?[] { "34ABC123", 10.0, 100.0, null, null },
            new object?[] { "34ABC123", 5.0, 50.0, "1234567890", "ÖRNEK ŞARJ A.Ş." },
            new object?[] { "34ABC123", 7.0, 70.0, "1234567890", "ÖRNEK ŞARJ A.Ş." },
            new object?[] { "34ABC123", 2.0, 20.0, null, null },
            new object?[] { "34ABC123", 1.0, 10.0, "9876543210", "BAŞKA AĞ A.Ş." }));

        Assert.True(sonuc.Gecerli, string.Join("\n", sonuc.Hatalar));
        Assert.Equal(3, sonuc.Satirlar.Count);
        Assert.Contains(sonuc.Satirlar, s => s.RoamingVkn == null && s.Kwh == 12.0m && s.Tutar == 120.0m);
        Assert.Contains(sonuc.Satirlar, s => s.RoamingVkn == "1234567890" && s.Kwh == 12.0m && s.Tutar == 120.0m);
        Assert.Contains(sonuc.Satirlar, s => s.RoamingVkn == "9876543210" && s.Kwh == 1.0m);
    }

    [Fact]
    public void FarkliAracKimlikNumaralariUyariVerir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl", "aracKimlikNo" },
            new object?[] { "34ABC123", 1.0, 10.0, "AAA111" },
            new object?[] { "34ABC123", 1.0, 10.0, null },
            new object?[] { "34ABC123", 1.0, 10.0, "BBB222" }));

        Assert.True(sonuc.Gecerli);
        var s = Assert.Single(sonuc.Satirlar);
        Assert.Equal("AAA111", s.AracKimlikNo);
        Assert.Contains(sonuc.Uyarilar, u => u.Contains("34ABC123") && u.Contains("araç kimlik"));
    }

    [Fact]
    public void ArsivlenmisPakettenSatirlarGeriOkunur()
    {
        var veri = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl", "aracKimlikNo", "roaming vkn", "roaming unvan" },
            new object?[] { "34ABC123", 10.5, 120.25, "AAA111", null, null },
            new object?[] { "06XYZ99", 3.0, 36.0, null, "1234567890", "ÖRNEK ŞARJ A.Ş." }));
        using var imzaci = TestYardimci.EcImzaci();
        var paket = EsuGonderimServisi.PaketHazirla(veri, 2026, 10, imzaci);

        var okunan = PaketOkuyucu.Satirlar(paket.Zip);

        Assert.Equal(veri.Satirlar.OrderBy(s => s.Plaka), okunan.OrderBy(s => s.Plaka));
    }

    [Fact]
    public void AyniRoamingVknFarkliUnvanUyariVerir()
    {
        var sonuc = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl", "roaming vkn", "roaming unvan" },
            new object?[] { "34ABC123", 1.0, 10.0, "1234567890", "ÖRNEK ŞARJ A.Ş." },
            new object?[] { "34ABC123", 1.0, 10.0, "1234567890", "Örnek Şarj AŞ" }));

        Assert.True(sonuc.Gecerli);
        var s = Assert.Single(sonuc.Satirlar);
        Assert.Equal("ÖRNEK ŞARJ A.Ş.", s.RoamingUnvan);
        Assert.Contains(sonuc.Uyarilar, u => u.Contains("farklı unvan"));
    }

    [Fact]
    public void GidenRapordaHerPlakaBirKezVeToplamlarExcelleAyni()
    {
        // 300 araç, her biri 1-20 seans (seans başına bir Excel satırı).
        var rastgele = new Random(42);
        var satirlar = new List<object?[]> { new object?[] { "plaka", "kwh", "tl" } };
        decimal excelKwh = 0, excelTl = 0;
        for (var a = 0; a < 300; a++)
        {
            var plaka = $"34 AB {1000 + a}";
            for (var s = rastgele.Next(1, 21); s > 0; s--)
            {
                var kwh = Math.Round((decimal)rastgele.NextDouble() * 60 + 0.01m, 2);
                var tl = Math.Round(kwh * 12.49m, 2);
                excelKwh += kwh;
                excelTl += tl;
                satirlar.Add(new object?[] { s % 2 == 0 ? plaka.ToLowerInvariant() : plaka, (double)kwh, (double)tl });
            }
        }

        var veri = EsuExcelOkuyucu.Oku(TestYardimci.Excel(satirlar.ToArray()));
        Assert.True(veri.Gecerli, string.Join("\n", veri.Hatalar.Take(3)));
        using var imzaci = TestYardimci.EcImzaci();
        var paket = EsuGonderimServisi.PaketHazirla(veri, 2026, 10, imzaci);

        var doc = XmlYardimci.Yukle(paket.ImzaliXml);
        Assert.Empty(EsuSemaDogrulayici.Dogrula(doc));
        var raporlar = doc.GetElementsByTagName("esuRapor", Ad.Earsiv).Cast<XmlElement>().ToList();
        var plakalar = raporlar.Select(r => r["plakaNo", Ad.Earsiv]!.InnerText).ToList();
        Assert.Equal(300, raporlar.Count);
        Assert.Equal(plakalar.Count, plakalar.Distinct().Count());
        static decimal D(XmlElement r, string ad) => decimal.Parse(r[ad, Ad.Earsiv]!.InnerText, CultureInfo.InvariantCulture);
        Assert.Equal(excelKwh, raporlar.Sum(r => D(r, "hizmetMiktari")));
        Assert.Equal(excelTl, raporlar.Sum(r => D(r, "toplamTutar")));
        Assert.Equal(excelTl, paket.ToplamTutar);
        Assert.All(raporlar, r => Assert.Equal("TRY", r["paraBirimi", Ad.Earsiv]!.InnerText));
    }
}
