using System.IO.Compression;
using System.Text;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;

namespace GibEsu.Tests;

public class KisaDosyaAdiTestleri
{
    private static HazirPaket Paket()
    {
        var id = Guid.Parse("28e488c0-a796-40cf-aa54-c02d9ae93c74");
        var xml = Encoding.UTF8.GetBytes("<x/>");
        return new HazirPaket
        {
            PaketId = id, DosyaAdi = PaketOlusturucu.DosyaAdi(id), Zip = PaketOlusturucu.Zip(id, xml), ImzaliXml = xml,
            Vkn = "3260693904", DonemBaslangic = new DateOnly(2026, 10, 1), DonemBitis = new DateOnly(2026, 10, 31),
            SatirSayisi = 1, ToplamKwh = 1m, ToplamTutar = 1m,
        };
    }

    [Fact]
    public void CanliHerZamanRaporNoZip()
    {
        var paket = Paket();
        var (ad, zip) = EsuGonderimServisi.GonderilecekDosya(paket, GibOrtami.Canli, testKisaDosyaAdi: true);

        Assert.Equal("28e488c0-a796-40cf-aa54-c02d9ae93c74.zip", ad);
        Assert.Same(paket.Zip, zip);
    }

    [Fact]
    public void AyarKapaliykenTestteDeRaporNoZip()
    {
        var paket = Paket();
        Assert.Equal(paket.DosyaAdi, EsuGonderimServisi.GonderilecekDosya(paket, GibOrtami.Test, testKisaDosyaAdi: false).DosyaAdi);
    }

    [Fact]
    public void TestteKisaAdVeZipIciAyniAd()
    {
        var (ad, zip) = EsuGonderimServisi.GonderilecekDosya(Paket(), GibOrtami.Test, testKisaDosyaAdi: true);

        Assert.Equal("28e488c0.zip", ad);
        using var arsiv = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Equal("28e488c0.xml", Assert.Single(arsiv.Entries).FullName);
    }
}
