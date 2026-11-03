using GibEsu.Core.Imza;
using Microsoft.Extensions.Options;

namespace GibEsu.Web.Servisler;

/// <summary>Kart işlemlerini tek sırada yürütür; aynı anda iki işlem karta erişemez.</summary>
public sealed class KartServisi
{
    private readonly Ayarlar _ayarlar;

    public KartServisi(IOptions<Ayarlar> ayarlar) => _ayarlar = ayarlar.Value;

    public async Task<(List<KartSertifikasi> Sertifikalar, string? Hata)> ListeleAsync()
    {
        await Pkcs11Imzaci.KartKilidi.WaitAsync();
        try
        {
            return (Pkcs11Imzaci.SertifikalariListele(_ayarlar.Kutuphaneler), null);
        }
        catch (KartHatasi ex)
        {
            return (new List<KartSertifikasi>(), ex.Message);
        }
        catch (Exception ex)
        {
            return (new List<KartSertifikasi>(), "Kart okunamadı: " + ex.Message);
        }
        finally
        {
            Pkcs11Imzaci.KartKilidi.Release();
        }
    }

    /// <summary>Karta giriş yapıp verilen işlemi çalıştırır, ardından oturumu kapatır.</summary>
    public async Task<T> ImzaciIleAsync<T>(string parmakIzi, string pin, bool sonDenemeyeIzinVer,
        Func<IMuhurImzaci, Task<T>> islem)
    {
        await Pkcs11Imzaci.KartKilidi.WaitAsync();
        try
        {
            var secim = Pkcs11Imzaci.SertifikalariListele(_ayarlar.Kutuphaneler)
                .FirstOrDefault(s => string.Equals(s.ParmakIzi, parmakIzi, StringComparison.OrdinalIgnoreCase))
                ?? throw new KartHatasi("Seçilen sertifika kartta bulunamadı. Kart çıkarılmış olabilir; sayfayı yenileyin.");
            using var imzaci = Pkcs11Imzaci.Ac(secim, pin, sonDenemeyeIzinVer);
            return await islem(imzaci);
        }
        finally
        {
            Pkcs11Imzaci.KartKilidi.Release();
        }
    }
}
