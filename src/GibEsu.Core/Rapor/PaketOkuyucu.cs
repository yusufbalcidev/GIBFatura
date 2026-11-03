using System.Globalization;
using System.IO.Compression;
using System.Xml;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Rapor;

/// <summary>Arşivlenmiş paketteki (GİB'e giden zip) rapor satırlarını okur; geçmiş gönderimleri görüntülemek için.</summary>
public static class PaketOkuyucu
{
    public static List<EsuSatiri> Satirlar(byte[] zip)
    {
        using var arsiv = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        var kayit = arsiv.Entries.FirstOrDefault(e => e.Name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase))
                    ?? throw new InvalidDataException("Pakette XML dosyası yok.");
        if (kayit.Length > PaketOlusturucu.EnBuyukAcikBoyut)
            throw new InvalidDataException("Paketteki XML çok büyük.");

        using var akis = kayit.Open();
        using var ms = new MemoryStream();
        akis.CopyTo(ms);
        var doc = XmlYardimci.Yukle(ms.ToArray());

        static string? Metin(XmlElement e, string ad) => e[ad, Ad.Earsiv]?.InnerText;
        static decimal Sayi(XmlElement e, string ad) => decimal.Parse(Metin(e, ad) ?? "0", CultureInfo.InvariantCulture);

        return doc.GetElementsByTagName("esuRapor", Ad.Earsiv).Cast<XmlElement>()
            .Select(e =>
            {
                var roaming = e["roamingHizmeti", Ad.Earsiv];
                return new EsuSatiri(
                    Metin(e, "plakaNo") ?? "",
                    Sayi(e, "hizmetMiktari"),
                    Sayi(e, "toplamTutar"),
                    Metin(e, "aracKimlikNo"),
                    roaming == null ? null : Metin(roaming, "vkn"),
                    roaming == null ? null : Metin(roaming, "unvan"));
            })
            .ToList();
    }
}
