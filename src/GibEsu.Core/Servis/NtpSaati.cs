using System.Net;
using System.Net.Sockets;

namespace GibEsu.Core.Servis;

public sealed record SaatBilgisi(DateTime Utc, TimeSpan? Sapma, string Kaynak);

/// <summary>
/// WS-Security zaman damgası 5 dakika geçerlidir; bilgisayar saati kaymışsa GİB mesajı reddeder.
/// Saat NTP sunucusundan alınır, ulaşılamazsa bilgisayar saati kullanılır.
/// </summary>
public static class NtpSaati
{
    private static readonly string[] Sunucular = { "time.google.com", "pool.ntp.org", "time.windows.com" };

    public static async Task<SaatBilgisi> SimdiAsync(CancellationToken iptal = default)
    {
        foreach (var sunucu in Sunucular)
        {
            try
            {
                var (ntp, yerel) = await SorgulaAsync(sunucu, iptal).ConfigureAwait(false);
                return new SaatBilgisi(ntp, ntp - yerel, sunucu);
            }
            catch (Exception ex) when (ex is SocketException or TimeoutException or OperationCanceledException)
            {
                if (iptal.IsCancellationRequested)
                    throw;
            }
        }
        return new SaatBilgisi(DateTime.UtcNow, null, "bilgisayar saati");
    }

    private static async Task<(DateTime Ntp, DateTime Yerel)> SorgulaAsync(string sunucu, CancellationToken iptal)
    {
        var adresler = await Dns.GetHostAddressesAsync(sunucu, iptal).ConfigureAwait(false);
        var adres = adresler.FirstOrDefault(a => a.AddressFamily == AddressFamily.InterNetwork) ?? adresler[0];

        using var udp = new UdpClient(adres.AddressFamily);
        udp.Connect(adres, 123);
        var istek = new byte[48];
        istek[0] = 0x1B; // LI=0, VN=3, Mode=3 (istemci)
        var gonderim = DateTime.UtcNow;
        await udp.SendAsync(istek, iptal).ConfigureAwait(false);

        using var zaman = CancellationTokenSource.CreateLinkedTokenSource(iptal);
        zaman.CancelAfter(TimeSpan.FromSeconds(3));
        UdpReceiveResult yanit;
        try
        {
            yanit = await udp.ReceiveAsync(zaman.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (!iptal.IsCancellationRequested)
        {
            throw new TimeoutException();
        }
        var alim = DateTime.UtcNow;
        var veri = yanit.Buffer;
        if (veri.Length < 48)
            throw new TimeoutException();

        ulong saniye = ((ulong)veri[40] << 24) | ((ulong)veri[41] << 16) | ((ulong)veri[42] << 8) | veri[43];
        ulong kesir = ((ulong)veri[44] << 24) | ((ulong)veri[45] << 16) | ((ulong)veri[46] << 8) | veri[47];
        var ms = saniye * 1000 + kesir * 1000 / 0x100000000UL;
        var ntp = new DateTime(1900, 1, 1, 0, 0, 0, DateTimeKind.Utc).AddMilliseconds(ms);
        var gidisDonus = alim - gonderim;
        var yerel = alim - TimeSpan.FromTicks(gidisDonus.Ticks / 2);
        return (ntp, yerel);
    }
}
