using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;

namespace GibEsu.Core.Xml;

public static class XmlYardimci
{
    private static readonly XmlReaderSettings OkumaAyarlari = new()
    {
        DtdProcessing = DtdProcessing.Prohibit,
        XmlResolver = null,
    };

    /// <summary>
    /// XML'i standart (satır sonlarını normalleştiren) okuyucu ile yükler.
    /// XmlDocument.Load/LoadXml eski okuyucuyu kullanır ve CR karakterlerini korur; bu da
    /// Windows'ta yazılmış belgelerde imza özetlerini bozar.
    /// </summary>
    public static XmlDocument Yukle(byte[] veri)
    {
        using var ms = new MemoryStream(veri, writable: false);
        using var okuyucu = XmlReader.Create(ms, OkumaAyarlari);
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        doc.Load(okuyucu);
        return doc;
    }

    public static XmlDocument Yukle(string metin)
    {
        using var okuyucu = XmlReader.Create(new StringReader(metin), OkumaAyarlari);
        var doc = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        doc.Load(okuyucu);
        return doc;
    }

    /// <summary>Belgeyi biçim değiştirmeden UTF-8 (BOM'suz) olarak yazar.</summary>
    public static byte[] Kaydet(XmlDocument doc, bool xmlBildirimi)
    {
        var ayarlar = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineHandling = NewLineHandling.None,
            OmitXmlDeclaration = !xmlBildirimi,
        };
        using var ms = new MemoryStream();
        using (var yazici = XmlWriter.Create(ms, ayarlar))
            doc.Save(yazici);
        return ms.ToArray();
    }

    /// <summary>
    /// Bir öğeyi kanonik biçime çevirir. Öğe kendi belgesine kopyalanır ve atalarından gelen
    /// ad alanı bildirimleri kopyaya taşınır; böylece belge içindeki bağlam korunmuş olur.
    /// </summary>
    public static byte[] Kanonik(XmlElement oge, bool exclusive, string? onekListesi = null)
    {
        var d = new XmlDocument { PreserveWhitespace = true, XmlResolver = null };
        var kopya = (XmlElement)d.ImportNode(oge, true);
        d.AppendChild(kopya);
        for (var ata = oge.ParentNode; ata is XmlElement ataOge; ata = ataOge.ParentNode)
        {
            foreach (XmlAttribute nitelik in ataOge.Attributes)
            {
                var adAlaniBildirimi = nitelik.Prefix == "xmlns" || nitelik.Name == "xmlns";
                if (adAlaniBildirimi && !kopya.HasAttribute(nitelik.Name))
                    kopya.SetAttribute(nitelik.Name, nitelik.Value);
            }
        }

        Transform donusum = exclusive
            ? new XmlDsigExcC14NTransform(false, onekListesi)
            : new XmlDsigC14NTransform(false);
        donusum.LoadInput(d);
        using var cikti = (Stream)donusum.GetOutput(typeof(Stream));
        using var ms = new MemoryStream();
        cikti.CopyTo(ms);
        return ms.ToArray();
    }

    /// <summary>Id, ID, id veya wsu:Id niteliği verilen değere eşit olan öğeyi bulur.</summary>
    public static XmlElement? IdIleBul(XmlDocument doc, string id)
    {
        foreach (XmlElement oge in doc.GetElementsByTagName("*"))
        {
            if (oge.GetAttribute("Id") == id || oge.GetAttribute("ID") == id || oge.GetAttribute("id") == id
                || oge.GetAttribute("Id", Ad.Wsu) == id)
                return oge;
        }
        return null;
    }

    public static HashAlgorithmName OzetAlgoritmasi(string uri) => uri switch
    {
        Ad.Sha1 => HashAlgorithmName.SHA1,
        Ad.Sha256 => HashAlgorithmName.SHA256,
        Ad.Sha384 => HashAlgorithmName.SHA384,
        Ad.Sha512 => HashAlgorithmName.SHA512,
        _ => throw new NotSupportedException("Desteklenmeyen özet algoritması: " + uri),
    };

    public static byte[] Ozet(HashAlgorithmName algoritma, byte[] veri)
    {
        using var ozet = IncrementalHash.CreateHash(algoritma);
        ozet.AppendData(veri);
        return ozet.GetHashAndReset();
    }

    public static XmlElement Ekle(XmlNode ebeveyn, string onek, string ad, string adAlani, string? metin = null)
    {
        var doc = ebeveyn as XmlDocument ?? ebeveyn.OwnerDocument!;
        var oge = doc.CreateElement(onek, ad, adAlani);
        if (metin != null)
            oge.InnerText = metin;
        ebeveyn.AppendChild(oge);
        return oge;
    }

    public static void SatirSonu(XmlNode ebeveyn) =>
        ebeveyn.AppendChild((ebeveyn.OwnerDocument ?? (XmlDocument)ebeveyn).CreateTextNode("\n"));
}
