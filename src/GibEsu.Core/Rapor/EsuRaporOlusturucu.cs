using System.Globalization;
using System.Text;
using System.Xml;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Rapor;

public sealed record RaporBilgisi(
    string MukellefVkn,
    string HazirlayanVkn,
    Guid RaporNo,
    DateOnly DonemBaslangic,
    DateOnly DonemBitis,
    IReadOnlyList<EsuSatiri> Satirlar)
{
    public static (DateOnly Baslangic, DateOnly Bitis) Ay(int yil, int ay)
    {
        var bas = new DateOnly(yil, ay, 1);
        return (bas, bas.AddMonths(1).AddDays(-1));
    }
}

/// <summary>
/// Aylık ESÜ raporunu (eArsivRaporu / esuRapor) GİB şemasına (esuRapor.xsd) uygun şekilde oluşturur.
/// İmza sonradan earsiv:baslik içine eklenir.
/// </summary>
public static class EsuRaporOlusturucu
{
    public const string BirimKodu = "kw/h";
    public const string ParaBirimi = "TRY";

    public static XmlDocument Olustur(RaporBilgisi bilgi)
    {
        var ayarlar = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = true,
            IndentChars = "    ",
            NewLineChars = "\n",
            NewLineHandling = NewLineHandling.Replace,
        };

        var sb = new StringBuilder();
        using (var w = XmlWriter.Create(new StringWriterUtf8(sb), ayarlar))
        {
            w.WriteStartDocument();
            w.WriteStartElement("earsiv", "eArsivRaporu", Ad.Earsiv);
            w.WriteAttributeString("xmlns", "earsiv", null, Ad.Earsiv);
            w.WriteAttributeString("xmlns", "http://www.w3.org/2000/xmlns/", Ad.Ds);
            w.WriteAttributeString("xmlns", "ns1", null, "http://zraporu.efatura.gov.tr");
            w.WriteAttributeString("xmlns", "xades", null, Ad.Xades);
            w.WriteAttributeString("xmlns", "xades141", null, Ad.Xades141);
            w.WriteAttributeString("xmlns", "xerces", null, "http://xerces.apache.org");
            w.WriteAttributeString("xmlns", "xsi", null, Ad.Xsi);
            w.WriteAttributeString("xsi", "schemaLocation", Ad.Xsi, "http://earsiv.efatura.gov.tr EsuRapor.xsd");

            w.WriteStartElement("earsiv", "baslik", Ad.Earsiv);
            Yaz(w, "versiyon", "1.0");
            KimlikYaz(w, "mukellef", bilgi.MukellefVkn);
            KimlikYaz(w, "hazirlayan", bilgi.HazirlayanVkn);
            Yaz(w, "raporNo", bilgi.RaporNo.ToString("D"));
            Yaz(w, "donemBaslangicTarihi", Tarih(bilgi.DonemBaslangic));
            Yaz(w, "donemBitisTarihi", Tarih(bilgi.DonemBitis));
            Yaz(w, "bolumBaslangicTarihi", Tarih(bilgi.DonemBaslangic));
            Yaz(w, "bolumBitisTarihi", Tarih(bilgi.DonemBitis));
            Yaz(w, "bolumNo", "1");
            w.WriteEndElement();

            foreach (var s in bilgi.Satirlar)
            {
                w.WriteStartElement("earsiv", "esuRapor", Ad.Earsiv);
                Yaz(w, "UUID", Guid.NewGuid().ToString("D"));
                Yaz(w, "plakaNo", s.Plaka);
                w.WriteStartElement("earsiv", "hizmetMiktari", Ad.Earsiv);
                w.WriteAttributeString("unitCode", BirimKodu);
                w.WriteString(Tutar(s.Kwh));
                w.WriteEndElement();
                Yaz(w, "toplamTutar", Tutar(s.Tutar));
                Yaz(w, "paraBirimi", ParaBirimi);
                if (s.AracKimlikNo != null)
                    Yaz(w, "aracKimlikNo", s.AracKimlikNo);
                if (s.RoamingVkn != null && s.RoamingUnvan != null)
                {
                    w.WriteStartElement("earsiv", "roamingHizmeti", Ad.Earsiv);
                    Yaz(w, "vkn", s.RoamingVkn);
                    Yaz(w, "unvan", s.RoamingUnvan);
                    w.WriteEndElement();
                }
                w.WriteEndElement();
            }

            w.WriteEndElement();
            w.WriteEndDocument();
        }

        return XmlYardimci.Yukle(sb.ToString());
    }

    private static void Yaz(XmlWriter w, string ad, string deger) =>
        w.WriteElementString("earsiv", ad, Ad.Earsiv, deger);

    private static void KimlikYaz(XmlWriter w, string ad, string vknVeyaTckn)
    {
        w.WriteStartElement("earsiv", ad, Ad.Earsiv);
        Yaz(w, vknVeyaTckn.Length == 11 ? "tckn" : "vkn", vknVeyaTckn);
        w.WriteEndElement();
    }

    private static string Tarih(DateOnly t) => t.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>GİB'in kabul ettiği paketteki biçim: 54.0, 43.1, 495.22.</summary>
    private static string Tutar(decimal d) => Math.Round(d, 2).ToString("0.0#", CultureInfo.InvariantCulture);

    private sealed class StringWriterUtf8 : StringWriter
    {
        public StringWriterUtf8(StringBuilder sb) : base(sb, CultureInfo.InvariantCulture)
        {
        }

        public override Encoding Encoding => new UTF8Encoding(false);
    }
}
