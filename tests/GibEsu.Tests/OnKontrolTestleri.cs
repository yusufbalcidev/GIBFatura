using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;

namespace GibEsu.Tests;

public class OnKontrolTestleri
{
    [Fact]
    public void SonTarih_IzleyenAyin15i()
    {
        Assert.Equal(new DateTime(2026, 11, 15), RaporTakvimi.SonTarih(2026, 10));
        Assert.Equal(new DateTime(2027, 1, 15), RaporTakvimi.SonTarih(2026, 12));
    }

    [Fact]
    public void BeklenenDonemVeKalanGun()
    {
        var bugun = new DateTime(2026, 11, 3, 14, 30, 0);

        Assert.Equal((2026, 10), RaporTakvimi.BeklenenDonem(bugun));
        Assert.Equal(12, RaporTakvimi.KalanGun(2026, 10, bugun));
        Assert.Equal(0, RaporTakvimi.KalanGun(2026, 10, new DateTime(2026, 11, 15, 23, 0, 0)));
        Assert.Equal(-1, RaporTakvimi.KalanGun(2026, 10, new DateTime(2026, 11, 16)));
        Assert.Equal((2026, 12), RaporTakvimi.BeklenenDonem(new DateTime(2027, 1, 5)));
    }

    [Fact]
    public void Karsilastirma_BuyukSapmaOlagandisi()
    {
        var farklar = DonemKarsilastirma.Karsilastir(new RaporOzeti(4541, 100_000m, 1_000_000m), new RaporOzeti(2300, 110_000m, 1_000_000m));

        Assert.Equal(-49.4m, farklar[0].Yuzde);
        Assert.True(farklar[0].Olagandisi);
        Assert.Equal(10.0m, farklar[1].Yuzde);
        Assert.False(farklar[1].Olagandisi);
        Assert.Equal(0m, farklar[2].Yuzde);
        Assert.False(farklar[2].Olagandisi);
    }

    [Fact]
    public void Karsilastirma_OncekiSifirsaOlagandisi()
    {
        var fark = DonemKarsilastirma.Karsilastir(new RaporOzeti(0, 0m, 0m), new RaporOzeti(10, 5m, 50m))[0];

        Assert.Null(fark.Yuzde);
        Assert.True(fark.Olagandisi);
    }

    [Theory]
    [InlineData("http://okc.gib.gov.tr/okcesu/services/EArsivWsPort/earsiv")]
    [InlineData("okc.gib.gov.tr")]
    [InlineData("ftp://okc.gib.gov.tr/x")]
    public void GibAdresi_HttpsOlmali(string adres)
    {
        Assert.Throws<ArgumentException>(() => GibEsuIstemcisi.AdresCoz(adres));
    }

    [Fact]
    public async Task CanliKilitliykenCanliyaIstekGitmez()
    {
        Assert.True(GibEsuIstemcisi.CanliKilitli);
        // Ağa çıkılırsa bu işleyici testi düşürür.
        var istemci = new GibEsuIstemcisi(new HttpClient(new AgaCikmaYasak()));

        var hata = await Assert.ThrowsAsync<GonderimHatasi>(() =>
            istemci.DurumSorgulaAsync(new byte[] { 1 }, GibOrtami.Canli, CancellationToken.None));
        Assert.Contains("Canlı ortam kapalı", hata.Message);
        await Assert.ThrowsAsync<GonderimHatasi>(() =>
            istemci.DosyaGonderAsync(new byte[] { 1 }, GibOrtami.Canli, CancellationToken.None));
    }

    [Fact]
    public void AdresGibYazisiylaEylemlerKanitliIstemcilerleAyni()
    {
        // Adresler: GİB'in okcdestek yazısı ve okctest …/EArsivWsPort/earsiv.wsdl.
        // Eylemler: Electroop, eveys ve canperk istemcilerinin gönderdiği küçük harfli değerler.
        Assert.Equal("https://okc.gib.gov.tr/okcesu/services/EArsivWsPort", GibEsuIstemcisi.VarsayilanCanliAdres);
        Assert.Equal("https://okctest.gib.gov.tr/okcesu/services/EArsivWsPort", GibEsuIstemcisi.VarsayilanTestAdres);
        Assert.Equal("urn:sendDocumentFile", GibEsuIstemcisi.GonderEylemi);
        Assert.Equal("urn:getBatchStatus", GibEsuIstemcisi.DurumEylemi);
    }

    private sealed class AgaCikmaYasak : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Canlı kilitliyken istek gönderildi: " + request.RequestUri);
    }

    [Fact]
    public void GibAdresi_HttpsKabulEdilir()
    {
        Assert.Equal(new Uri(GibEsuIstemcisi.VarsayilanCanliAdres), GibEsuIstemcisi.AdresCoz(" " + GibEsuIstemcisi.VarsayilanCanliAdres + " "));
        Assert.Equal(new Uri(GibEsuIstemcisi.VarsayilanCanliAdres), GibEsuIstemcisi.Adres(GibOrtami.Canli));
    }
}
