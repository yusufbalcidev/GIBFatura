using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Imza;

/// <summary>Mali mühür (veya testte yazılım anahtarı) ile özet imzalayan bileşen.</summary>
public interface IMuhurImzaci : IDisposable
{
    X509Certificate2 Sertifika { get; }

    /// <summary>
    /// Özeti imzalar. EC anahtarda ham r||s (IEEE P1363), RSA anahtarda PKCS#1 v1.5 imza döner.
    /// </summary>
    byte[] OzetImzala(byte[] ozet, HashAlgorithmName algoritma);
}

public static class ImzaAlgoritmasi
{
    private const string EcOid = "1.2.840.10045.2.1";

    public static bool EcMi(X509Certificate2 sertifika) => sertifika.GetKeyAlgorithm() == EcOid;

    /// <summary>
    /// GİB'in kabul ettiği eşleşme: EC anahtar için ECDSA-SHA384,
    /// RSA anahtar için RSA-SHA256 (e-Arşiv kılavuzu).
    /// </summary>
    public static (string ImzaUri, HashAlgorithmName Ozet) Sec(X509Certificate2 sertifika) =>
        EcMi(sertifika)
            ? (Ad.EcdsaSha384, HashAlgorithmName.SHA384)
            : (Ad.RsaSha256, HashAlgorithmName.SHA256);
}
