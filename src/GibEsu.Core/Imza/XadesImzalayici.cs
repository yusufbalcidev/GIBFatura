using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Xml;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Imza;

/// <summary>
/// ESÜ raporunu XAdES-BES olarak imzalar. Yapı, GİB'in kabul ettiği referans paketle aynıdır:
/// imza earsiv:baslik içine eklenir (enveloped), SignedInfo exclusive C14N, referanslar inclusive C14N
/// ve SHA-256, imza EC anahtarda ECDSA-SHA384 (DER), RSA anahtarda RSA-SHA256.
/// </summary>
public static class XadesImzalayici
{
    public const string VarsayilanRol = "Şarj Ağı İşletmecisi";

    public static XmlDocument Imzala(XmlDocument rapor, IMuhurImzaci imzaci, string rol = VarsayilanRol)
    {
        var sertifika = imzaci.Sertifika;
        var (imzaUri, imzaOzeti) = ImzaAlgoritmasi.Sec(sertifika);

        var imzaId = "Signature-Id-" + Guid.NewGuid();
        var ozelliklerId = "Signed-Properties-Id-" + Guid.NewGuid();
        var belgeRefId = "Reference-Id-" + Guid.NewGuid();

        // 1) İmza iskeletini ekle, belgeyi yazıp yeniden oku: tüm ad alanı bildirimleri gerçek nitelik olur.
        var calisma = XmlYardimci.Yukle(XmlYardimci.Kaydet(rapor, xmlBildirimi: true));
        var baslik = Baslik(calisma);
        var imza = IskeletOlustur(calisma, sertifika, imzaUri, rol, imzaId, ozelliklerId, belgeRefId);
        baslik.AppendChild(imza);
        var doc = XmlYardimci.Yukle(XmlYardimci.Kaydet(calisma, xmlBildirimi: true));

        var ns = AdYoneticisi(doc);
        var imzaOgesi = (XmlElement)doc.SelectSingleNode("//ds:Signature", ns)!;
        var signedInfo = (XmlElement)imzaOgesi.SelectSingleNode("ds:SignedInfo", ns)!;

        // 2) SignedProperties özeti (dönüşüm yok => inclusive C14N).
        var ozellikler = XmlYardimci.IdIleBul(doc, ozelliklerId)!;
        var ozellikOzeti = XmlYardimci.Ozet(HashAlgorithmName.SHA256, XmlYardimci.Kanonik(ozellikler, exclusive: false));

        // 3) Belge özeti: imza çıkarılmış belge, inclusive C14N.
        var belgeOzeti = XmlYardimci.Ozet(HashAlgorithmName.SHA256, ImzasizBelgeKanonik(doc, imzaOgesi));

        var ozetler = signedInfo.SelectNodes("ds:Reference/ds:DigestValue", ns)!;
        ozetler[0]!.InnerText = Convert.ToBase64String(ozellikOzeti);
        ozetler[1]!.InnerText = Convert.ToBase64String(belgeOzeti);

        // 4) SignedInfo'yu imzala.
        var siOzeti = XmlYardimci.Ozet(imzaOzeti, XmlYardimci.Kanonik(signedInfo, exclusive: true));
        var imzaDegeri = imzaci.OzetImzala(siOzeti, imzaOzeti);
        if (ImzaAlgoritmasi.EcMi(sertifika))
            imzaDegeri = HamdanDer(imzaDegeri);
        imzaOgesi.SelectSingleNode("ds:SignatureValue", ns)!.InnerText = Convert.ToBase64String(imzaDegeri);

        return doc;
    }

    internal static byte[] ImzasizBelgeKanonik(XmlDocument doc, XmlElement imza)
    {
        var kopya = (XmlDocument)doc.Clone();
        var imzaKopyasi = KopyadakiOge(kopya, imza);
        imzaKopyasi.ParentNode!.RemoveChild(imzaKopyasi);
        return XmlYardimci.Kanonik(kopya.DocumentElement!, exclusive: false);
    }

    private static XmlElement KopyadakiOge(XmlDocument kopya, XmlElement asil)
    {
        var id = asil.GetAttribute("Id");
        if (id.Length > 0)
            return XmlYardimci.IdIleBul(kopya, id)!;
        var tumu = kopya.GetElementsByTagName(asil.LocalName, asil.NamespaceURI);
        return (XmlElement)tumu[0]!;
    }

    private static XmlElement Baslik(XmlDocument doc) =>
        (XmlElement?)doc.DocumentElement!.GetElementsByTagName("baslik", Ad.Earsiv)[0]
        ?? throw new InvalidOperationException("Raporda earsiv:baslik bulunamadı.");

    private static XmlNamespaceManager AdYoneticisi(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", Ad.Ds);
        ns.AddNamespace("xades", Ad.Xades);
        ns.AddNamespace("earsiv", Ad.Earsiv);
        return ns;
    }

    private static XmlElement IskeletOlustur(XmlDocument doc, X509Certificate2 sertifika, string imzaUri, string rol,
        string imzaId, string ozelliklerId, string belgeRefId)
    {
        const string ds = "ds";
        var imza = doc.CreateElement(ds, "Signature", Ad.Ds);
        imza.SetAttribute("xmlns:ds", Ad.Ds);
        imza.SetAttribute("Id", imzaId);
        XmlYardimci.SatirSonu(imza);

        var si = XmlYardimci.Ekle(imza, ds, "SignedInfo", Ad.Ds);
        XmlYardimci.SatirSonu(si);
        XmlYardimci.Ekle(si, ds, "CanonicalizationMethod", Ad.Ds).SetAttribute("Algorithm", Ad.ExcC14n);
        XmlYardimci.SatirSonu(si);
        XmlYardimci.Ekle(si, ds, "SignatureMethod", Ad.Ds).SetAttribute("Algorithm", imzaUri);
        XmlYardimci.SatirSonu(si);

        var ref1 = XmlYardimci.Ekle(si, ds, "Reference", Ad.Ds);
        ref1.SetAttribute("Id", "Reference-Id-" + Guid.NewGuid());
        ref1.SetAttribute("Type", Ad.SignedPropertiesType);
        ref1.SetAttribute("URI", "#" + ozelliklerId);
        XmlYardimci.SatirSonu(ref1);
        XmlYardimci.Ekle(ref1, ds, "DigestMethod", Ad.Ds).SetAttribute("Algorithm", Ad.Sha256);
        XmlYardimci.SatirSonu(ref1);
        XmlYardimci.Ekle(ref1, ds, "DigestValue", Ad.Ds, "");
        XmlYardimci.SatirSonu(ref1);
        XmlYardimci.SatirSonu(si);

        var ref2 = XmlYardimci.Ekle(si, ds, "Reference", Ad.Ds);
        ref2.SetAttribute("Id", belgeRefId);
        ref2.SetAttribute("URI", "");
        XmlYardimci.SatirSonu(ref2);
        var donusumler = XmlYardimci.Ekle(ref2, ds, "Transforms", Ad.Ds);
        XmlYardimci.SatirSonu(donusumler);
        XmlYardimci.Ekle(donusumler, ds, "Transform", Ad.Ds).SetAttribute("Algorithm", Ad.Enveloped);
        XmlYardimci.SatirSonu(donusumler);
        XmlYardimci.SatirSonu(ref2);
        XmlYardimci.Ekle(ref2, ds, "DigestMethod", Ad.Ds).SetAttribute("Algorithm", Ad.Sha256);
        XmlYardimci.SatirSonu(ref2);
        XmlYardimci.Ekle(ref2, ds, "DigestValue", Ad.Ds, "");
        XmlYardimci.SatirSonu(ref2);
        XmlYardimci.SatirSonu(si);
        XmlYardimci.SatirSonu(imza);

        XmlYardimci.Ekle(imza, ds, "SignatureValue", Ad.Ds, "").SetAttribute("Id", "Signature-Value-Id-" + Guid.NewGuid());
        XmlYardimci.SatirSonu(imza);

        var ki = XmlYardimci.Ekle(imza, ds, "KeyInfo", Ad.Ds);
        XmlYardimci.SatirSonu(ki);
        var x509Data = XmlYardimci.Ekle(ki, ds, "X509Data", Ad.Ds);
        XmlYardimci.SatirSonu(x509Data);
        XmlYardimci.Ekle(x509Data, ds, "X509Certificate", Ad.Ds, Convert.ToBase64String(sertifika.RawData));
        XmlYardimci.SatirSonu(x509Data);
        XmlYardimci.SatirSonu(ki);
        XmlYardimci.SatirSonu(imza);

        var nesne = XmlYardimci.Ekle(imza, ds, "Object", Ad.Ds);
        nesne.SetAttribute("Id", "Object-Id-" + Guid.NewGuid());
        var qp = XmlYardimci.Ekle(nesne, "xades", "QualifyingProperties", Ad.Xades);
        qp.SetAttribute("Target", "#" + imzaId);
        XmlYardimci.SatirSonu(qp);
        var sp = XmlYardimci.Ekle(qp, "xades", "SignedProperties", Ad.Xades);
        sp.SetAttribute("Id", ozelliklerId);
        XmlYardimci.SatirSonu(sp);

        var ssp = XmlYardimci.Ekle(sp, "xades", "SignedSignatureProperties", Ad.Xades);
        XmlYardimci.SatirSonu(ssp);
        var sc = XmlYardimci.Ekle(ssp, "xades", "SigningCertificate", Ad.Xades);
        XmlYardimci.SatirSonu(sc);
        var cert = XmlYardimci.Ekle(sc, "xades", "Cert", Ad.Xades);
        XmlYardimci.SatirSonu(cert);
        var certDigest = XmlYardimci.Ekle(cert, "xades", "CertDigest", Ad.Xades);
        XmlYardimci.SatirSonu(certDigest);
        XmlYardimci.Ekle(certDigest, ds, "DigestMethod", Ad.Ds).SetAttribute("Algorithm", Ad.Sha256);
        XmlYardimci.SatirSonu(certDigest);
        XmlYardimci.Ekle(certDigest, ds, "DigestValue", Ad.Ds, Convert.ToBase64String(SHA256.HashData(sertifika.RawData)));
        XmlYardimci.SatirSonu(certDigest);
        XmlYardimci.SatirSonu(cert);
        var issuerSerial = XmlYardimci.Ekle(cert, "xades", "IssuerSerial", Ad.Xades);
        XmlYardimci.SatirSonu(issuerSerial);
        XmlYardimci.Ekle(issuerSerial, ds, "X509IssuerName", Ad.Ds, SertifikaBilgisi.YayinciRfc2253(sertifika));
        XmlYardimci.SatirSonu(issuerSerial);
        XmlYardimci.Ekle(issuerSerial, ds, "X509SerialNumber", Ad.Ds, SertifikaBilgisi.SeriNoOndalik(sertifika));
        XmlYardimci.SatirSonu(issuerSerial);
        XmlYardimci.SatirSonu(cert);
        XmlYardimci.SatirSonu(sc);
        XmlYardimci.SatirSonu(ssp);

        var signerRole = XmlYardimci.Ekle(ssp, "xades", "SignerRole", Ad.Xades);
        XmlYardimci.SatirSonu(signerRole);
        var claimedRoles = XmlYardimci.Ekle(signerRole, "xades", "ClaimedRoles", Ad.Xades);
        XmlYardimci.SatirSonu(claimedRoles);
        XmlYardimci.Ekle(claimedRoles, "xades", "ClaimedRole", Ad.Xades, rol);
        XmlYardimci.SatirSonu(claimedRoles);
        XmlYardimci.SatirSonu(signerRole);
        XmlYardimci.SatirSonu(ssp);
        XmlYardimci.SatirSonu(sp);

        var sdop = XmlYardimci.Ekle(sp, "xades", "SignedDataObjectProperties", Ad.Xades);
        XmlYardimci.SatirSonu(sdop);
        var dof = XmlYardimci.Ekle(sdop, "xades", "DataObjectFormat", Ad.Xades);
        dof.SetAttribute("ObjectReference", "#" + belgeRefId);
        XmlYardimci.SatirSonu(dof);
        XmlYardimci.Ekle(dof, "xades", "MimeType", Ad.Xades, "text/xml");
        XmlYardimci.SatirSonu(dof);
        XmlYardimci.SatirSonu(sdop);
        XmlYardimci.SatirSonu(sp);
        XmlYardimci.SatirSonu(qp);
        XmlYardimci.SatirSonu(nesne);
        XmlYardimci.SatirSonu(imza);
        return imza;
    }

    /// <summary>Ham r||s ECDSA imzasını DER SEQUENCE biçimine çevirir (referans paketteki imzayla aynı).</summary>
    internal static byte[] HamdanDer(byte[] ham)
    {
        var yari = ham.Length / 2;
        var yazici = new System.Formats.Asn1.AsnWriter(System.Formats.Asn1.AsnEncodingRules.DER);
        using (yazici.PushSequence())
        {
            yazici.WriteIntegerUnsigned(BastakiSifirlariAt(ham.AsSpan(0, yari)));
            yazici.WriteIntegerUnsigned(BastakiSifirlariAt(ham.AsSpan(yari)));
        }
        return yazici.Encode();
    }

    private static ReadOnlySpan<byte> BastakiSifirlariAt(ReadOnlySpan<byte> sayi)
    {
        var bas = 0;
        while (bas < sayi.Length - 1 && sayi[bas] == 0)
            bas++;
        return sayi[bas..];
    }
}
