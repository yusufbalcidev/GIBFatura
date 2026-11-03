using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;

namespace GibEsu.Core.Imza;

/// <summary>Özel anahtarı bellekte olan sertifika ile imzalar. Testler ve deneme için.</summary>
public sealed class YazilimImzaci : IMuhurImzaci
{
    public YazilimImzaci(X509Certificate2 anahtarliSertifika)
    {
        if (!anahtarliSertifika.HasPrivateKey)
            throw new ArgumentException("Sertifikanın özel anahtarı yok.", nameof(anahtarliSertifika));
        Sertifika = anahtarliSertifika;
    }

    public X509Certificate2 Sertifika { get; }

    public byte[] OzetImzala(byte[] ozet, HashAlgorithmName algoritma)
    {
        if (ImzaAlgoritmasi.EcMi(Sertifika))
        {
            using var ec = Sertifika.GetECDsaPrivateKey()!;
            return ec.SignHash(ozet, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);
        }
        using var rsa = Sertifika.GetRSAPrivateKey()!;
        return rsa.SignHash(ozet, algoritma, RSASignaturePadding.Pkcs1);
    }

    public void Dispose()
    {
    }
}
