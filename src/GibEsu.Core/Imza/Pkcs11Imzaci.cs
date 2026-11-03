using System.Formats.Asn1;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using Net.Pkcs11Interop.Common;
using Net.Pkcs11Interop.HighLevelAPI;

namespace GibEsu.Core.Imza;

/// <summary>Kartta bulunan bir sertifika ve kart bilgileri.</summary>
public sealed record KartSertifikasi(
    string KutuphaneYolu,
    ulong SlotId,
    string KartEtiketi,
    string KartSeriNo,
    X509Certificate2 Sertifika,
    byte[] AnahtarId,
    string? PinUyarisi,
    bool PinKilitli,
    bool SonDeneme)
{
    public string ParmakIzi => Sertifika.Thumbprint;
    public string? Vkn => SertifikaBilgisi.VknVeyaTckn(Sertifika);

    /// <summary>
    /// GİB raporu mali mühürle imzalanmalı; kişisel e-imza (NES) "Gönderici imza yetkisi yok" hatası alır.
    /// </summary>
    public bool MaliMuhurMu => Sertifika.Issuer.Contains("Mali Mühür", StringComparison.OrdinalIgnoreCase);
}

public sealed class KartHatasi : Exception
{
    public KartHatasi(string mesaj, Exception? ic = null) : base(mesaj, ic)
    {
    }
}

/// <summary>
/// PKCS#11 (AKİS sürücüsü akisp11.dll) üzerinden mali mühür kartıyla imza atar.
/// Özel anahtar kartın dışına çıkmaz; kart yalnızca verilen özeti imzalar.
/// </summary>
public sealed class Pkcs11Imzaci : IMuhurImzaci
{
    /// <summary>Kart sürücüsü için aranacak varsayılan konumlar.</summary>
    public static readonly string[] VarsayilanKutuphaneler =
    {
        Path.Combine(Environment.SystemDirectory, "akisp11.dll"),
    };

    private static readonly Pkcs11InteropFactories Fabrikalar = new();

    /// <summary>Aynı anda tek bir kart işlemi yapılır.</summary>
    public static readonly SemaphoreSlim KartKilidi = new(1, 1);

    private readonly IPkcs11Library _kutuphane;
    private readonly ISession _oturum;
    private readonly IObjectHandle _anahtar;
    private bool _kapatildi;

    private Pkcs11Imzaci(IPkcs11Library kutuphane, ISession oturum, IObjectHandle anahtar, X509Certificate2 sertifika)
    {
        _kutuphane = kutuphane;
        _oturum = oturum;
        _anahtar = anahtar;
        Sertifika = sertifika;
    }

    public X509Certificate2 Sertifika { get; }

    /// <summary>Takılı kartlardaki imza sertifikalarını listeler. PIN gerekmez.</summary>
    public static List<KartSertifikasi> SertifikalariListele(IEnumerable<string> kutuphaneler)
    {
        var sonuc = new List<KartSertifikasi>();
        var bulunanKutuphane = false;
        foreach (var yol in kutuphaneler.Where(File.Exists))
        {
            bulunanKutuphane = true;
            using var kutuphane = KutuphaneYukle(yol);
            foreach (var slot in kutuphane.GetSlotList(SlotsType.WithTokenPresent))
            {
                var kart = slot.GetTokenInfo();
                using var oturum = slot.OpenSession(SessionType.ReadOnly);
                foreach (var (sertifika, id) in SertifikalariOku(oturum))
                {
                    sonuc.Add(new KartSertifikasi(
                        yol,
                        slot.SlotId,
                        kart.Label.Trim(),
                        kart.SerialNumber.Trim(),
                        sertifika,
                        id,
                        PinUyarisi(kart),
                        kart.TokenFlags.UserPinLocked,
                        kart.TokenFlags.UserPinFinalTry));
                }
            }
        }

        if (!bulunanKutuphane)
            throw new KartHatasi("Kart sürücüsü bulunamadı (akisp11.dll). AKİS kart sürücüsünün kurulu olduğundan emin olun.");
        return sonuc;
    }

    /// <summary>Karta PIN ile giriş yapar ve seçilen sertifikanın anahtarıyla imzalamaya hazır hale gelir.</summary>
    public static Pkcs11Imzaci Ac(KartSertifikasi secim, string pin, bool sonDenemeyeIzinVer = false)
    {
        if (string.IsNullOrWhiteSpace(pin))
            throw new KartHatasi("PIN girilmedi.");
        if (secim.PinKilitli)
            throw new KartHatasi("Kartın PIN'i kilitli. Kartı veren kurumdan (Kamu SM) PIN çözme işlemi gerekir.");
        if (secim.SonDeneme && !sonDenemeyeIzinVer)
            throw new KartHatasi("Kartta son PIN deneme hakkı kaldı. Yanlış PIN kartı kilitler. Devam etmek için onay kutusunu işaretleyin.");

        var kutuphane = KutuphaneYukle(secim.KutuphaneYolu);
        ISession? oturum = null;
        try
        {
            var slot = kutuphane.GetSlotList(SlotsType.WithTokenPresent).FirstOrDefault(s => s.SlotId == secim.SlotId)
                ?? throw new KartHatasi("Kart çıkarılmış ya da değişmiş. Sayfayı yenileyip tekrar deneyin.");
            oturum = slot.OpenSession(SessionType.ReadOnly);
            try
            {
                oturum.Login(CKU.CKU_USER, pin);
            }
            catch (Pkcs11Exception ex) when (ex.RV == CKR.CKR_USER_ALREADY_LOGGED_IN)
            {
            }

            var anahtarlar = oturum.FindAllObjects(new List<IObjectAttribute>
            {
                Fabrikalar.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                Fabrikalar.ObjectAttributeFactory.Create(CKA.CKA_ID, secim.AnahtarId),
            });
            if (anahtarlar.Count == 0)
            {
                // Sertifika ile anahtarın kimlikleri eşleşmeyen kartlarda tek anahtar varsa onu kullan.
                anahtarlar = oturum.FindAllObjects(new List<IObjectAttribute>
                {
                    Fabrikalar.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_PRIVATE_KEY),
                });
                if (anahtarlar.Count != 1)
                    throw new KartHatasi("Sertifikaya ait özel anahtar kartta bulunamadı.");
            }
            return new Pkcs11Imzaci(kutuphane, oturum, anahtarlar[0], secim.Sertifika);
        }
        catch (Pkcs11Exception ex)
        {
            oturum?.Dispose();
            kutuphane.Dispose();
            throw new KartHatasi(HataMesaji(ex.RV), ex);
        }
        catch
        {
            oturum?.Dispose();
            kutuphane.Dispose();
            throw;
        }
    }

    public byte[] OzetImzala(byte[] ozet, HashAlgorithmName algoritma)
    {
        try
        {
            if (ImzaAlgoritmasi.EcMi(Sertifika))
            {
                using var mekanizma = Fabrikalar.MechanismFactory.Create(CKM.CKM_ECDSA);
                var imza = _oturum.Sign(mekanizma, _anahtar, ozet);
                using var ec = Sertifika.GetECDsaPublicKey()!;
                return HamImzayaCevir(imza, (ec.KeySize + 7) / 8);
            }

            using var rsaMekanizma = Fabrikalar.MechanismFactory.Create(CKM.CKM_RSA_PKCS);
            return _oturum.Sign(rsaMekanizma, _anahtar, DigestInfo(ozet, algoritma));
        }
        catch (Pkcs11Exception ex)
        {
            throw new KartHatasi(HataMesaji(ex.RV), ex);
        }
    }

    public void Dispose()
    {
        if (_kapatildi)
            return;
        _kapatildi = true;
        try
        {
            _oturum.Logout();
        }
        catch (Pkcs11Exception)
        {
        }
        _oturum.Dispose();
        _kutuphane.Dispose();
    }

    private static IPkcs11Library KutuphaneYukle(string yol)
    {
        try
        {
            return Fabrikalar.Pkcs11LibraryFactory.LoadPkcs11Library(Fabrikalar, yol, AppType.MultiThreaded);
        }
        catch (Pkcs11Exception ex) when (ex.RV is CKR.CKR_CANT_LOCK or CKR.CKR_ARGUMENTS_BAD)
        {
            // AKİS sürücüsü (akisp11 2.75) kilitleme parametreli C_Initialize'ı CKR_ARGUMENTS_BAD ile reddediyor.
            // Kart işlemleri zaten KartKilidi ile sıraya alındığından tek iş parçacıklı mod yeterli.
            try
            {
                return Fabrikalar.Pkcs11LibraryFactory.LoadPkcs11Library(Fabrikalar, yol, AppType.SingleThreaded);
            }
            catch (Pkcs11Exception ic)
            {
                throw new KartHatasi(HataMesaji(ic.RV), ic);
            }
        }
        catch (Pkcs11Exception ex)
        {
            throw new KartHatasi(HataMesaji(ex.RV), ex);
        }
        catch (Exception ex) when (ex is DllNotFoundException or BadImageFormatException or UnmanagedException)
        {
            throw new KartHatasi($"Kart sürücüsü yüklenemedi ({yol}): {ex.Message}", ex);
        }
    }

    private static IEnumerable<(X509Certificate2 Sertifika, byte[] Id)> SertifikalariOku(ISession oturum)
    {
        var nesneler = oturum.FindAllObjects(new List<IObjectAttribute>
        {
            Fabrikalar.ObjectAttributeFactory.Create(CKA.CKA_CLASS, CKO.CKO_CERTIFICATE),
        });
        foreach (var nesne in nesneler)
        {
            var nitelikler = oturum.GetAttributeValue(nesne, new List<CKA> { CKA.CKA_VALUE, CKA.CKA_ID });
            var deger = nitelikler[0].GetValueAsByteArray();
            var id = nitelikler[1].GetValueAsByteArray() ?? Array.Empty<byte>();
            if (deger == null || deger.Length == 0)
                continue;
            X509Certificate2 sertifika;
            try
            {
                sertifika = new X509Certificate2(deger);
            }
            catch (CryptographicException)
            {
                continue;
            }
            if (ImzaSertifikasiMi(sertifika))
                yield return (sertifika, id);
        }
    }

    private static bool ImzaSertifikasiMi(X509Certificate2 sertifika)
    {
        var kullanim = sertifika.Extensions.OfType<X509KeyUsageExtension>().FirstOrDefault();
        if (kullanim == null)
            return true;
        return (kullanim.KeyUsages & (X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.NonRepudiation)) != 0;
    }

    private static string? PinUyarisi(ITokenInfo kart)
    {
        if (kart.TokenFlags.UserPinLocked)
            return "PIN kilitli.";
        if (kart.TokenFlags.UserPinFinalTry)
            return "Son PIN deneme hakkı kaldı!";
        if (kart.TokenFlags.UserPinCountLow)
            return "Daha önce yanlış PIN girilmiş; deneme hakkı azaldı.";
        return null;
    }

    /// <summary>Bazı kartlar ECDSA imzasını DER olarak döndürür; ham r||s biçimine çevirir.</summary>
    internal static byte[] HamImzayaCevir(byte[] imza, int alanUzunlugu)
    {
        if (imza.Length == 2 * alanUzunlugu)
            return imza;
        try
        {
            var okuyucu = new AsnReader(imza, AsnEncodingRules.DER);
            var dizi = okuyucu.ReadSequence();
            var r = dizi.ReadIntegerBytes().ToArray();
            var s = dizi.ReadIntegerBytes().ToArray();
            var ham = new byte[2 * alanUzunlugu];
            Sabitle(r, ham, 0, alanUzunlugu);
            Sabitle(s, ham, alanUzunlugu, alanUzunlugu);
            return ham;
        }
        catch (AsnContentException ex)
        {
            throw new KartHatasi("Kartın döndürdüğü imza biçimi tanınmadı.", ex);
        }
    }

    private static void Sabitle(byte[] tamsayi, byte[] hedef, int konum, int uzunluk)
    {
        var bas = 0;
        while (bas < tamsayi.Length - 1 && tamsayi[bas] == 0)
            bas++;
        var adet = tamsayi.Length - bas;
        if (adet > uzunluk)
            throw new KartHatasi("Kartın döndürdüğü imza boyutu beklenenden büyük.");
        Buffer.BlockCopy(tamsayi, bas, hedef, konum + uzunluk - adet, adet);
    }

    internal static byte[] DigestInfo(byte[] ozet, HashAlgorithmName algoritma)
    {
        var oid = algoritma.Name switch
        {
            "SHA256" => "2.16.840.1.101.3.4.2.1",
            "SHA384" => "2.16.840.1.101.3.4.2.2",
            "SHA512" => "2.16.840.1.101.3.4.2.3",
            "SHA1" => "1.3.14.3.2.26",
            _ => throw new NotSupportedException(algoritma.Name),
        };
        var yazici = new AsnWriter(AsnEncodingRules.DER);
        using (yazici.PushSequence())
        {
            using (yazici.PushSequence())
            {
                yazici.WriteObjectIdentifier(oid);
                yazici.WriteNull();
            }
            yazici.WriteOctetString(ozet);
        }
        return yazici.Encode();
    }

    internal static string HataMesaji(CKR kod) => kod switch
    {
        CKR.CKR_PIN_INCORRECT => "PIN hatalı. Yanlış denemeler kartı kilitler; tekrar denemeden önce PIN'i kontrol edin.",
        CKR.CKR_PIN_LOCKED => "Kartın PIN'i kilitlendi. Kamu SM üzerinden PIN çözme işlemi gerekir.",
        CKR.CKR_PIN_LEN_RANGE => "PIN uzunluğu geçersiz.",
        CKR.CKR_PIN_EXPIRED => "PIN'in süresi dolmuş.",
        CKR.CKR_TOKEN_NOT_PRESENT => "Kart takılı değil.",
        CKR.CKR_TOKEN_NOT_RECOGNIZED => "Kart tanınmadı.",
        CKR.CKR_DEVICE_REMOVED => "Kart işlem sırasında çıkarıldı.",
        CKR.CKR_DEVICE_ERROR => "Kart okuyucu hatası. Kartı çıkarıp tekrar takın.",
        CKR.CKR_SESSION_HANDLE_INVALID or CKR.CKR_SESSION_CLOSED => "Kart oturumu kapandı. İşlemi tekrar başlatın.",
        CKR.CKR_USER_NOT_LOGGED_IN => "Karta giriş yapılmadı.",
        CKR.CKR_KEY_FUNCTION_NOT_PERMITTED or CKR.CKR_KEY_TYPE_INCONSISTENT => "Karttaki anahtar bu imza türüne izin vermiyor.",
        CKR.CKR_MECHANISM_INVALID => "Kart bu imza algoritmasını desteklemiyor.",
        _ => "Kart hatası: " + kod,
    };
}
