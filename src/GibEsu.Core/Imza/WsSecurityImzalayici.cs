using System.Security;
using System.Security.Cryptography;
using System.Xml;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Imza;

/// <summary>
/// SOAP 1.2 zarfını WS-Security ile imzalar: Timestamp ve Body, mali mühürle imzalanır,
/// sertifika BinarySecurityToken olarak eklenir ve DirectReference ile gösterilir.
/// Yapı ve kanonikleştirme ayarları GİB'in kabul ettiği referans mesajla (WSS4J) aynıdır.
/// </summary>
public static class WsSecurityImzalayici
{
    private const string ImzaOnekListesi = "soap web";
    private const string ZamanOnekListesi = "wsse soap web";
    private const string GovdeOnekListesi = "web";

    /// <param name="govdeIcerigi">soap:Body içine konacak XML (web: önekiyle).</param>
    /// <param name="simdiUtc">Zaman damgası için güncel UTC saat.</param>
    public static byte[] ImzaliZarf(string govdeIcerigi, IMuhurImzaci imzaci, DateTime simdiUtc)
    {
        var sertifika = imzaci.Sertifika;
        var (imzaUri, imzaOzeti) = ImzaAlgoritmasi.Sec(sertifika);

        var govdeId = "id-" + RastgeleHex();
        var zamanId = "TS-" + RastgeleHex();
        var tokenId = "X509-" + RastgeleHex();
        var olusturma = simdiUtc.ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");
        var bitis = simdiUtc.AddMinutes(5).ToString("yyyy-MM-ddTHH:mm:ss.fff'Z'");

        // Security başlığında boşluk karakteri bulunmaz (WSS4J çıktısı gibi); gövde girintilidir.
        var zarf =
            $"<soap:Envelope xmlns:soap=\"{Ad.Soap12}\" xmlns:web=\"{Ad.Web}\">\n" +
            "    <soap:Header>" +
            $"<wsse:Security xmlns:wsse=\"{Ad.Wsse}\" xmlns:wsu=\"{Ad.Wsu}\">" +
            $"<wsse:BinarySecurityToken EncodingType=\"{Ad.Base64Binary}\" ValueType=\"{Ad.X509v3}\" wsu:Id=\"{tokenId}\">{Convert.ToBase64String(sertifika.RawData)}</wsse:BinarySecurityToken>" +
            $"<ds:Signature xmlns:ds=\"{Ad.Ds}\" Id=\"SIG-{RastgeleHex()}\">" +
            "<ds:SignedInfo>" +
            $"<ds:CanonicalizationMethod Algorithm=\"{Ad.ExcC14n}\"><ec:InclusiveNamespaces xmlns:ec=\"{Ad.ExcC14n}\" PrefixList=\"{ImzaOnekListesi}\"/></ds:CanonicalizationMethod>" +
            $"<ds:SignatureMethod Algorithm=\"{imzaUri}\"/>" +
            Referans(zamanId, ZamanOnekListesi) +
            Referans(govdeId, GovdeOnekListesi) +
            "</ds:SignedInfo>" +
            "<ds:SignatureValue></ds:SignatureValue>" +
            $"<ds:KeyInfo Id=\"KI-{RastgeleHex()}\"><wsse:SecurityTokenReference wsu:Id=\"STR-{RastgeleHex()}\"><wsse:Reference URI=\"#{tokenId}\" ValueType=\"{Ad.X509v3}\"/></wsse:SecurityTokenReference></ds:KeyInfo>" +
            "</ds:Signature>" +
            $"<wsu:Timestamp wsu:Id=\"{zamanId}\"><wsu:Created>{olusturma}</wsu:Created><wsu:Expires>{bitis}</wsu:Expires></wsu:Timestamp>" +
            "</wsse:Security>" +
            "</soap:Header>\n" +
            $"    <soap:Body xmlns:wsu=\"{Ad.Wsu}\" wsu:Id=\"{govdeId}\">\n" +
            "        " + govdeIcerigi + "\n" +
            "    </soap:Body>\n" +
            "</soap:Envelope>";

        var doc = XmlYardimci.Yukle(zarf);
        var ns = AdYoneticisi(doc);

        var zaman = XmlYardimci.IdIleBul(doc, zamanId)!;
        var govde = XmlYardimci.IdIleBul(doc, govdeId)!;
        var ozetler = doc.SelectNodes("//ds:SignedInfo/ds:Reference/ds:DigestValue", ns)!;
        ozetler[0]!.InnerText = Convert.ToBase64String(
            XmlYardimci.Ozet(HashAlgorithmName.SHA1, XmlYardimci.Kanonik(zaman, true, ZamanOnekListesi)));
        ozetler[1]!.InnerText = Convert.ToBase64String(
            XmlYardimci.Ozet(HashAlgorithmName.SHA1, XmlYardimci.Kanonik(govde, true, GovdeOnekListesi)));

        var signedInfo = (XmlElement)doc.SelectSingleNode("//ds:SignedInfo", ns)!;
        var siOzeti = XmlYardimci.Ozet(imzaOzeti, XmlYardimci.Kanonik(signedInfo, true, ImzaOnekListesi));
        var imzaDegeri = imzaci.OzetImzala(siOzeti, imzaOzeti);
        doc.SelectSingleNode("//ds:SignatureValue", ns)!.InnerText = Convert.ToBase64String(imzaDegeri);

        return XmlYardimci.Kaydet(doc, xmlBildirimi: false);
    }

    /// <summary>sendDocumentFile gövdesi.</summary>
    public static string DosyaGonderGovdesi(string dosyaAdi, byte[] zip) =>
        "<web:sendDocumentFile><Attachment>" +
        $"<fileName>{SecurityElement.Escape(dosyaAdi)}</fileName>" +
        $"<binaryData>{Convert.ToBase64String(zip)}</binaryData>" +
        "</Attachment></web:sendDocumentFile>";

    /// <summary>getBatchStatus gövdesi.</summary>
    public static string DurumSorguGovdesi(string paketId) =>
        $"<web:getBatchStatus><paketId>{SecurityElement.Escape(paketId)}</paketId></web:getBatchStatus>";

    private static string Referans(string id, string onekListesi) =>
        $"<ds:Reference URI=\"#{id}\">" +
        $"<ds:Transforms><ds:Transform Algorithm=\"{Ad.ExcC14n}\"><ec:InclusiveNamespaces xmlns:ec=\"{Ad.ExcC14n}\" PrefixList=\"{onekListesi}\"/></ds:Transform></ds:Transforms>" +
        $"<ds:DigestMethod Algorithm=\"{Ad.Sha1}\"/>" +
        "<ds:DigestValue></ds:DigestValue>" +
        "</ds:Reference>";

    private static string RastgeleHex() => Convert.ToHexString(RandomNumberGenerator.GetBytes(16));

    private static XmlNamespaceManager AdYoneticisi(XmlDocument doc)
    {
        var ns = new XmlNamespaceManager(doc.NameTable);
        ns.AddNamespace("ds", Ad.Ds);
        ns.AddNamespace("wsse", Ad.Wsse);
        ns.AddNamespace("wsu", Ad.Wsu);
        ns.AddNamespace("soap", Ad.Soap12);
        return ns;
    }
}
