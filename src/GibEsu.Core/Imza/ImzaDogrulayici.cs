using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Imza;

public sealed class ImzaDogrulamaSonucu
{
    public bool Gecerli => Hatalar.Count == 0;
    public List<string> Hatalar { get; } = new();
    public List<string> Bilgiler { get; } = new();
    public X509Certificate2? Sertifika { get; set; }

    public override string ToString() =>
        Gecerli ? "İmza geçerli" : "İmza geçersiz: " + string.Join("; ", Hatalar);
}

/// <summary>
/// XMLDSig imzalarını doğrular. Hem rapordaki XAdES imzası hem SOAP'taki WS-Security imzası için kullanılır.
/// Gönderimden önce kendi ürettiğimiz imzaları kontrol etmek ve GİB'in kabul ettiği referans paketin imzalarıyla
/// karşılaştırma yapmak için yazıldı.
/// </summary>
public static class ImzaDogrulayici
{
    public static ImzaDogrulamaSonucu XadesDogrula(XmlDocument doc)
    {
        var sonuc = new ImzaDogrulamaSonucu();
        var imza = (XmlElement?)doc.GetElementsByTagName("Signature", Ad.Ds)[0];
        if (imza == null)
        {
            sonuc.Hatalar.Add("Raporda imza bulunamadı.");
            return sonuc;
        }

        var sertifikaMetni = imza.GetElementsByTagName("X509Certificate", Ad.Ds)[0]?.InnerText;
        if (sertifikaMetni == null)
        {
            sonuc.Hatalar.Add("İmzada sertifika bulunamadı.");
            return sonuc;
        }
        sonuc.Sertifika = new X509Certificate2(Convert.FromBase64String(sertifikaMetni));

        // XAdES: imzalayan sertifikanın özeti.
        var sertifikaOzeti = imza.GetElementsByTagName("CertDigest", Ad.Xades)[0];
        if (sertifikaOzeti != null)
        {
            var yontem = ((XmlElement)sertifikaOzeti).GetElementsByTagName("DigestMethod", Ad.Ds)[0]?.Attributes?["Algorithm"]?.Value ?? Ad.Sha256;
            var beklenen = ((XmlElement)sertifikaOzeti).GetElementsByTagName("DigestValue", Ad.Ds)[0]?.InnerText.Trim();
            var gercek = Convert.ToBase64String(XmlYardimci.Ozet(XmlYardimci.OzetAlgoritmasi(yontem), sonuc.Sertifika.RawData));
            if (beklenen != gercek)
                sonuc.Hatalar.Add("XAdES sertifika özeti uyuşmuyor.");
        }
        else
        {
            sonuc.Hatalar.Add("XAdES SigningCertificate bulunamadı.");
        }

        Dogrula(doc, imza, sonuc.Sertifika, sonuc);
        return sonuc;
    }

    public static ImzaDogrulamaSonucu WsSecurityDogrula(XmlDocument zarf)
    {
        var sonuc = new ImzaDogrulamaSonucu();
        var guvenlik = (XmlElement?)zarf.GetElementsByTagName("Security", Ad.Wsse)[0];
        var imza = guvenlik == null ? null : (XmlElement?)guvenlik.GetElementsByTagName("Signature", Ad.Ds)[0];
        if (imza == null)
        {
            sonuc.Hatalar.Add("SOAP mesajında WS-Security imzası yok.");
            return sonuc;
        }

        var tokenRef = (XmlElement?)imza.GetElementsByTagName("Reference", Ad.Wsse)[0];
        var tokenId = tokenRef?.GetAttribute("URI").TrimStart('#');
        var token = tokenId == null ? null : XmlYardimci.IdIleBul(zarf, tokenId);
        if (token == null)
        {
            sonuc.Hatalar.Add("BinarySecurityToken bulunamadı.");
            return sonuc;
        }
        sonuc.Sertifika = new X509Certificate2(Convert.FromBase64String(token.InnerText));

        var referanslar = imza.GetElementsByTagName("Reference", Ad.Ds).Cast<XmlElement>()
            .Select(r => r.GetAttribute("URI").TrimStart('#')).ToList();
        var govde = (XmlElement?)zarf.GetElementsByTagName("Body", Ad.Soap12)[0];
        var zaman = (XmlElement?)zarf.GetElementsByTagName("Timestamp", Ad.Wsu)[0];
        if (govde == null || !referanslar.Contains(govde.GetAttribute("Id", Ad.Wsu)))
            sonuc.Hatalar.Add("SOAP Body imzalanmamış.");
        if (zaman == null || !referanslar.Contains(zaman.GetAttribute("Id", Ad.Wsu)))
            sonuc.Hatalar.Add("Timestamp imzalanmamış.");

        Dogrula(zarf, imza, sonuc.Sertifika, sonuc);
        return sonuc;
    }

    private static void Dogrula(XmlDocument doc, XmlElement imza, X509Certificate2 sertifika, ImzaDogrulamaSonucu sonuc)
    {
        var signedInfo = (XmlElement?)imza.GetElementsByTagName("SignedInfo", Ad.Ds)[0];
        if (signedInfo == null)
        {
            sonuc.Hatalar.Add("SignedInfo yok.");
            return;
        }

        foreach (XmlElement referans in signedInfo.GetElementsByTagName("Reference", Ad.Ds))
        {
            var uri = referans.GetAttribute("URI");
            var yontem = ((XmlElement)referans.GetElementsByTagName("DigestMethod", Ad.Ds)[0]!).GetAttribute("Algorithm");
            var beklenen = referans.GetElementsByTagName("DigestValue", Ad.Ds)[0]!.InnerText.Trim();
            var donusumler = referans.GetElementsByTagName("Transform", Ad.Ds).Cast<XmlElement>().ToList();
            var zarfli = donusumler.Any(t => t.GetAttribute("Algorithm") == Ad.Enveloped);
            var excDonusum = donusumler.FirstOrDefault(t => t.GetAttribute("Algorithm") == Ad.ExcC14n);

            byte[] kanonik;
            if (uri.Length == 0)
            {
                if (!zarfli)
                {
                    sonuc.Hatalar.Add("Belge referansında enveloped dönüşümü yok.");
                    continue;
                }
                kanonik = XadesImzalayici.ImzasizBelgeKanonik(doc, imza);
            }
            else
            {
                var hedef = XmlYardimci.IdIleBul(doc, uri.TrimStart('#'));
                if (hedef == null)
                {
                    sonuc.Hatalar.Add($"Referans hedefi bulunamadı: {uri}");
                    continue;
                }
                kanonik = excDonusum != null
                    ? XmlYardimci.Kanonik(hedef, true, OnekListesi(excDonusum))
                    : XmlYardimci.Kanonik(hedef, false);
            }

            var gercek = Convert.ToBase64String(XmlYardimci.Ozet(XmlYardimci.OzetAlgoritmasi(yontem), kanonik));
            if (gercek != beklenen)
                sonuc.Hatalar.Add($"Özet uyuşmuyor: {(uri.Length == 0 ? "belge" : uri)}");
            else
                sonuc.Bilgiler.Add($"Özet doğru: {(uri.Length == 0 ? "belge" : uri)}");
        }

        var kanonikYontem = (XmlElement)signedInfo.GetElementsByTagName("CanonicalizationMethod", Ad.Ds)[0]!;
        var siKanonik = kanonikYontem.GetAttribute("Algorithm") == Ad.ExcC14n
            ? XmlYardimci.Kanonik(signedInfo, true, OnekListesi(kanonikYontem))
            : XmlYardimci.Kanonik(signedInfo, false);
        var imzaYontemi = ((XmlElement)signedInfo.GetElementsByTagName("SignatureMethod", Ad.Ds)[0]!).GetAttribute("Algorithm");
        var imzaDegeri = Convert.FromBase64String(imza.GetElementsByTagName("SignatureValue", Ad.Ds)[0]!.InnerText);

        bool dogru;
        switch (imzaYontemi)
        {
            case Ad.EcdsaSha384:
            case Ad.EcdsaSha256:
            {
                var algoritma = imzaYontemi == Ad.EcdsaSha384 ? HashAlgorithmName.SHA384 : HashAlgorithmName.SHA256;
                using var ec = sertifika.GetECDsaPublicKey();
                if (ec == null)
                {
                    sonuc.Hatalar.Add("Sertifika EC anahtarı içermiyor.");
                    return;
                }
                var ozet = XmlYardimci.Ozet(algoritma, siKanonik);
                // MA3 DER, WSS4J ham r||s üretir; ikisini de kabul et.
                dogru = ec.VerifyHash(ozet, imzaDegeri, DSASignatureFormat.IeeeP1363FixedFieldConcatenation)
                        || DerDogrula(ec, ozet, imzaDegeri);
                break;
            }
            case Ad.RsaSha256:
            {
                using var rsa = sertifika.GetRSAPublicKey();
                if (rsa == null)
                {
                    sonuc.Hatalar.Add("Sertifika RSA anahtarı içermiyor.");
                    return;
                }
                dogru = rsa.VerifyData(siKanonik, imzaDegeri, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
                break;
            }
            default:
                sonuc.Hatalar.Add("Desteklenmeyen imza algoritması: " + imzaYontemi);
                return;
        }

        if (dogru)
            sonuc.Bilgiler.Add("İmza değeri doğru.");
        else
            sonuc.Hatalar.Add("İmza değeri doğrulanamadı.");
    }

    private static bool DerDogrula(ECDsa ec, byte[] ozet, byte[] imza)
    {
        try
        {
            return ec.VerifyHash(ozet, imza, DSASignatureFormat.Rfc3279DerSequence);
        }
        catch (CryptographicException)
        {
            return false;
        }
    }

    private static string? OnekListesi(XmlElement yontem)
    {
        var dahil = (XmlElement?)yontem.GetElementsByTagName("InclusiveNamespaces", Ad.ExcC14n)[0];
        return dahil?.GetAttribute("PrefixList");
    }
}
