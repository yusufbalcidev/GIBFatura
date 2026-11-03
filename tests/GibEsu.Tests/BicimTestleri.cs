using System.Text;
using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Core.Xml;

namespace GibEsu.Tests;

/// <summary>Gönderilen baytların GİB'in kabul ettiği referans pakettekiyle aynı biçimde olduğunu doğrular.</summary>
public class BicimTestleri
{
    [Fact]
    public void RaporXml_ReferanstakiBildirimleBaslarVeImzaGecerli()
    {
        var veri = EsuExcelOkuyucu.Oku(TestYardimci.Excel(
            new object?[] { "plaka", "kwh", "tl" },
            new object?[] { "34ABC123", 12.5, 160.25 }));
        using var imzaci = TestYardimci.EcImzaci();

        var paket = EsuGonderimServisi.PaketHazirla(veri, 2026, 10, imzaci);
        var metin = Encoding.UTF8.GetString(paket.ImzaliXml);

        Assert.StartsWith("<?xml version=\"1.0\" encoding=\"UTF-8\"?><earsiv:eArsivRaporu ", metin);
        Assert.True(ImzaDogrulayici.XadesDogrula(XmlYardimci.Yukle(paket.ImzaliXml)).Gecerli);
    }

    [Fact]
    public async Task IstekteTraceparentBasligiYok()
    {
        // traceparent'ı .NET'in ağ katmanı eklediği için gerçek bir yerel bağlantı üzerinden denenir.
        var dinleyici = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        dinleyici.Start();
        var port = ((System.Net.IPEndPoint)dinleyici.LocalEndpoint).Port;
        var sunucu = Task.Run(async () =>
        {
            using var istemci = await dinleyici.AcceptTcpClientAsync();
            var akis = istemci.GetStream();
            var tampon = new byte[8192];
            var okunan = await akis.ReadAsync(tampon);
            await akis.WriteAsync("HTTP/1.1 200 OK\r\nContent-Length: 0\r\nConnection: close\r\n\r\n"u8.ToArray());
            return Encoding.ASCII.GetString(tampon, 0, okunan);
        });

        using var http = GibEsuIstemcisi.HttpIstemcisiOlustur(TimeSpan.FromSeconds(10));
        using (new System.Diagnostics.ActivitySource("GibEsu.Test").StartActivity() ?? new System.Diagnostics.Activity("test").Start())
            await http.PostAsync($"http://127.0.0.1:{port}/", new ByteArrayContent(new byte[] { 1 }));
        var istek = await sunucu;
        dinleyici.Stop();

        Assert.StartsWith("POST / HTTP/1.1", istek);
        Assert.DoesNotContain("traceparent", istek, StringComparison.OrdinalIgnoreCase);
    }
}
