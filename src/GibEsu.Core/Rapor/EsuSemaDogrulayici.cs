using System.Text;
using System.Xml;
using System.Xml.Schema;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Rapor;

/// <summary>
/// Raporu GİB'in resmi esuRapor.xsd şemasıyla doğrular. Şemanın içe aktardığı XMLDSig/XAdES şemaları
/// yerine yalnızca imza öğesinin varlığını kontrol eden küçük taslaklar kullanılır;
/// imzanın kendisi ImzaDogrulayici ile ayrıca doğrulanır.
/// </summary>
public static class EsuSemaDogrulayici
{
    private static readonly Lazy<XmlSchemaSet> Sema = new(SemaYukle);

    public static List<string> Dogrula(XmlDocument rapor)
    {
        var hatalar = new List<string>();
        var ayarlar = new XmlReaderSettings
        {
            ValidationType = ValidationType.Schema,
            Schemas = Sema.Value,
            DtdProcessing = DtdProcessing.Prohibit,
            XmlResolver = null,
        };
        ayarlar.ValidationEventHandler += (_, e) =>
        {
            if (hatalar.Count < 50)
                hatalar.Add($"Şema hatası (satır {e.Exception?.LineNumber}): {e.Message}");
        };

        using var ms = new MemoryStream(XmlYardimci.Kaydet(rapor, xmlBildirimi: true));
        using var okuyucu = XmlReader.Create(ms, ayarlar);
        while (okuyucu.Read())
        {
        }
        return hatalar;
    }

    private static XmlSchemaSet SemaYukle()
    {
        var set = new XmlSchemaSet { XmlResolver = new TaslakCozucu() };
        using var akis = typeof(EsuSemaDogrulayici).Assembly.GetManifestResourceStream("GibEsu.esuRapor.xsd")
            ?? throw new InvalidOperationException("esuRapor.xsd gömülü kaynağı bulunamadı.");
        using var okuyucu = XmlReader.Create(akis, new XmlReaderSettings { XmlResolver = null, DtdProcessing = DtdProcessing.Prohibit });
        set.Add(Ad.Earsiv, okuyucu);
        set.Compile();
        return set;
    }

    private sealed class TaslakCozucu : XmlResolver
    {
        private const string DsTaslagi =
            "<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='http://www.w3.org/2000/09/xmldsig#' elementFormDefault='qualified'>" +
            "<xs:element name='Signature'><xs:complexType><xs:sequence>" +
            "<xs:any processContents='skip' minOccurs='0' maxOccurs='unbounded'/>" +
            "</xs:sequence><xs:anyAttribute processContents='skip'/></xs:complexType></xs:element></xs:schema>";

        public override object? GetEntity(Uri absoluteUri, string? role, Type? ofObjectToReturn)
        {
            var dosya = absoluteUri.Segments.LastOrDefault() ?? "";
            var icerik = dosya switch
            {
                "xmldsig-core-schema.xsd" => DsTaslagi,
                "XAdES.xsd" => Bos(Ad.Xades),
                "XAdESv141.xsd" => Bos(Ad.Xades141),
                _ => throw new XmlException("Beklenmeyen şema içe aktarımı: " + absoluteUri),
            };
            return new MemoryStream(Encoding.UTF8.GetBytes(icerik));
        }

        public override Uri ResolveUri(Uri? baseUri, string? relativeUri) =>
            new("taslak:///" + relativeUri);

        private static string Bos(string adAlani) =>
            $"<xs:schema xmlns:xs='http://www.w3.org/2001/XMLSchema' targetNamespace='{adAlani}'/>";
    }
}
