using System.Net;
using System.Text;
using GibEsu.Core.Servis;

namespace GibEsu.Tests;

public class SoapGunluguTestleri
{
    private sealed class ToplayanGunluk : ISoapGunlugu
    {
        public List<SoapAlisverisi> Kayitlar { get; } = new();
        public void Kaydet(SoapAlisverisi alisveris) => Kayitlar.Add(alisveris);
    }

    private sealed class SabitYanit(Func<HttpResponseMessage> yanit) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            Task.FromResult(yanit());
    }

    private sealed class BaglantiKoptu : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken) =>
            throw new HttpRequestException("bağlantı reddedildi");
    }

    [Fact]
    public async Task HataDonenGonderimIstekVeYanitIleKaydedilir()
    {
        const string hataYaniti =
            "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Body><env:Fault>" +
            "<env:Code><env:Value>env:Receiver</env:Value></env:Code>" +
            "<env:Reason><env:Text xml:lang=\"en\">could not execute statement</env:Text></env:Reason>" +
            "</env:Fault></env:Body></env:Envelope>";
        var gunluk = new ToplayanGunluk();
        var istemci = new GibEsuIstemcisi(new HttpClient(new SabitYanit(() =>
            new HttpResponseMessage(HttpStatusCode.InternalServerError) { Content = new StringContent(hataYaniti, Encoding.UTF8, "application/soap+xml") })), gunluk);
        var zarf = Encoding.UTF8.GetBytes("<soap:Envelope>istek</soap:Envelope>");

        var yanit = await istemci.DosyaGonderAsync(zarf, GibOrtami.Test, CancellationToken.None);

        Assert.Contains("could not execute statement", yanit.HataMesaji);
        var k = Assert.Single(gunluk.Kayitlar);
        Assert.Equal("urn:SendDocumentFile", k.Eylem);
        Assert.Equal(GibOrtami.Test, k.Ortam);
        Assert.Equal(500, k.HttpDurumu);
        Assert.Equal(zarf, k.Istek);
        Assert.Equal(hataYaniti, Encoding.UTF8.GetString(k.Yanit!));
        Assert.Contains(k.IstekBasliklari, b => b.StartsWith("SOAPAction: urn:SendDocumentFile"));
        Assert.Contains(k.IstekBasliklari, b => b.StartsWith("Content-Type: application/soap+xml"));
        Assert.Contains(k.YanitBasliklari, b => b.Contains(" 500 "));
        Assert.Null(k.Hata);
    }

    [Fact]
    public async Task BaglantiHatasiDaKaydedilir()
    {
        var gunluk = new ToplayanGunluk();
        var istemci = new GibEsuIstemcisi(new HttpClient(new BaglantiKoptu()), gunluk);

        await Assert.ThrowsAsync<HttpRequestException>(() =>
            istemci.DurumSorgulaAsync(new byte[] { 1, 2, 3 }, GibOrtami.Test, CancellationToken.None));

        var k = Assert.Single(gunluk.Kayitlar);
        Assert.Equal("urn:GetBatchStatus", k.Eylem);
        Assert.Null(k.HttpDurumu);
        Assert.Null(k.Yanit);
        Assert.IsType<HttpRequestException>(k.Hata);
    }

    [Fact]
    public async Task GunlukHatasiGonderimiBozmaz()
    {
        var istemci = new GibEsuIstemcisi(new HttpClient(new SabitYanit(() => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StringContent("<x><return>Dosya kaydedildi</return></x>"),
        })), new BozukGunluk());

        var yanit = await istemci.DosyaGonderAsync(new byte[] { 1 }, GibOrtami.Test, CancellationToken.None);

        Assert.False(yanit.HataVar);
    }

    private sealed class BozukGunluk : ISoapGunlugu
    {
        public void Kaydet(SoapAlisverisi alisveris) => throw new IOException("disk dolu");
    }
}
