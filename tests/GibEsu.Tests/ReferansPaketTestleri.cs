using System.Security.Cryptography.X509Certificates;
using System.Xml;
using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Xml;

namespace GibEsu.Tests;

/// <summary>
/// GİB'in Eylül 2026'da kabul ettiği gerçek gönderimi bizim kodumuzla doğrular.
/// Bu testler geçiyorsa kanonikleştirme, özet ve imza hesaplarımız GİB'in beklediğiyle aynıdır.
/// Log dosyası bulunamazsa testler bir şey yapmadan geçer.
/// </summary>
public class ReferansPaketTestleri
{
    private static (XmlDocument Zarf, byte[] RaporXml)? Referans()
    {
        var log = TestYardimci.ReferansLogu();
        if (log == null)
        {
            // GIBESU_REFERANS_ZORUNLU=1 iken log'un bulunması şart.
            Assert.NotEqual("1", Environment.GetEnvironmentVariable("GIBESU_REFERANS_ZORUNLU"));
            return null;
        }
        var soap = TestYardimci.ImzaliSoap(log);
        Assert.NotNull(soap);

        var zarf = XmlYardimci.Yukle(soap!);
        var binaryData = zarf.GetElementsByTagName("binaryData")[0]!.InnerText;
        var xml = TestYardimci.ZiptekiXml(Convert.FromBase64String(binaryData), out _);
        return (zarf, xml);
    }

    [Fact]
    public void RaporImzasi_BizimDogrulayiciIleGecerli()
    {
        var r = Referans();
        if (r == null) return;

        var sonuc = ImzaDogrulayici.XadesDogrula(XmlYardimci.Yukle(r.Value.RaporXml));

        Assert.True(sonuc.Gecerli, sonuc.ToString());
        // Mühür sahibi raporun başlığındaki VKN ile aynı olmalı.
        var vkn = XmlYardimci.Yukle(r.Value.RaporXml).GetElementsByTagName("vkn", Ad.Earsiv)[0]!.InnerText;
        Assert.Equal(vkn, SertifikaBilgisi.VknVeyaTckn(sonuc.Sertifika!));
    }

    [Fact]
    public void RaporImzasi_DegistirilirseGecersiz()
    {
        var r = Referans();
        if (r == null) return;

        var doc = XmlYardimci.Yukle(r.Value.RaporXml);
        var tutar = doc.GetElementsByTagName("toplamTutar", Ad.Earsiv)[0]!;
        tutar.InnerText = "1.00";

        Assert.False(ImzaDogrulayici.XadesDogrula(doc).Gecerli);
    }

    [Fact]
    public void SoapImzasi_BizimDogrulayiciIleGecerli()
    {
        var r = Referans();
        if (r == null) return;

        // Log, Security başlığını girintili yazmış; GİB'e giden mesajda bu boşluklar yoktu.
        var zarf = r.Value.Zarf;
        var guvenlik = zarf.GetElementsByTagName("Security", Ad.Wsse)[0]!;
        foreach (var bosluk in guvenlik.SelectNodes(".//text()")!.Cast<XmlNode>()
                     .Where(n => string.IsNullOrWhiteSpace(n.Value)).ToList())
            bosluk.ParentNode!.RemoveChild(bosluk);

        var sonuc = ImzaDogrulayici.WsSecurityDogrula(zarf);

        Assert.True(sonuc.Gecerli, sonuc.ToString());
    }

    [Fact]
    public void SertifikaAlanlari_ReferanslaAyniBicimde()
    {
        var r = Referans();
        if (r == null) return;

        var doc = XmlYardimci.Yukle(r.Value.RaporXml);
        var sertifika = new X509Certificate2(Convert.FromBase64String(
            doc.GetElementsByTagName("X509Certificate", Ad.Ds)[0]!.InnerText));

        Assert.Equal(doc.GetElementsByTagName("X509IssuerName", Ad.Ds)[0]!.InnerText,
            SertifikaBilgisi.YayinciRfc2253(sertifika));
        Assert.Equal(doc.GetElementsByTagName("X509SerialNumber", Ad.Ds)[0]!.InnerText,
            SertifikaBilgisi.SeriNoOndalik(sertifika));
    }

    [Fact]
    public void ReferansVerisi_ExcelUzerindenAyniRaporUretilir()
    {
        var r = Referans();
        if (r == null) return;

        static List<(string Plaka, string Kwh, string Tutar)> Satirlar(XmlDocument d) =>
            d.GetElementsByTagName("esuRapor", Ad.Earsiv).Cast<XmlElement>()
                .Select(e => (e["plakaNo", Ad.Earsiv]!.InnerText, e["hizmetMiktari", Ad.Earsiv]!.InnerText, e["toplamTutar", Ad.Earsiv]!.InnerText))
                .OrderBy(x => x.Item1, StringComparer.Ordinal).ToList();

        var referans = Satirlar(XmlYardimci.Yukle(r.Value.RaporXml));
        var excelSatirlari = new List<object?[]> { new object?[] { "plaka", "kwh", "tl" } };
        excelSatirlari.AddRange(referans.Select(x => new object?[]
        {
            x.Plaka,
            double.Parse(x.Kwh, System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(x.Tutar, System.Globalization.CultureInfo.InvariantCulture),
        }));

        var veri = EsuExcelOkuyucu.Oku(TestYardimci.Excel(excelSatirlari.ToArray()));
        Assert.True(veri.Gecerli, string.Join("\n", veri.Hatalar.Take(5)));
        using var imzaci = TestYardimci.EcImzaci();
        var paket = Core.Servis.EsuGonderimServisi.PaketHazirla(veri, 2026, 9, imzaci);

        var uretilen = Satirlar(XmlYardimci.Yukle(paket.ImzaliXml));
        Assert.True(referans.Count > 1000);
        // Önceki program Türkçe Windows'ta "i" harfini "İ" yapmış ve bu yüzden aynı aracı ("İ"li ve "I"lı)
        // iki ayrı satır göndermiş. Biz plakaları "I" ile yazıyor ve aynı plakayı tek satırda topluyoruz.
        static decimal D(string s) => decimal.Parse(s, System.Globalization.CultureInfo.InvariantCulture);
        static string S(decimal d) => d.ToString("0.0#", System.Globalization.CultureInfo.InvariantCulture);
        var beklenen = referans
            .GroupBy(x => x.Plaka.Replace('İ', 'I'))
            .Select(g => (g.Key, S(g.Sum(x => D(x.Kwh))), S(g.Sum(x => D(x.Tutar)))))
            .OrderBy(x => x.Key, StringComparer.Ordinal).ToList();
        Assert.Equal(referans.Count - 1, beklenen.Count);
        Assert.Equal(beklenen, uretilen);
        var birlesen = referans.GroupBy(x => x.Plaka.Replace('İ', 'I')).First(g => g.Count() > 1).Key;
        Assert.Single(uretilen, x => x.Item1 == birlesen);
        var noktali = referans.First(x => x.Plaka.Contains('İ')).Plaka;
        Assert.Contains(veri.Uyarilar, u => u.Contains(noktali) && u.Contains(noktali.Replace('İ', 'I')));
    }

    [Fact]
    public void GercekMaliMuhurTaninirTestSertifikasiTaninmaz()
    {
        var r = Referans();
        if (r == null) return;

        var doc = XmlYardimci.Yukle(r.Value.RaporXml);
        var muhur = new X509Certificate2(Convert.FromBase64String(
            doc.GetElementsByTagName("X509Certificate", Ad.Ds)[0]!.InnerText));
        using var test = TestYardimci.EcImzaci();

        static KartSertifikasi Kart(X509Certificate2 s) => new("akisp11.dll", 0, "AKIS", "1", s, Array.Empty<byte>(), null, false, false);
        Assert.True(Kart(muhur).MaliMuhurMu);
        Assert.False(Kart(test.Sertifika).MaliMuhurMu);
    }

    [Fact]
    public void ReferansRapor_ResmiSemayaUygun()
    {
        var r = Referans();
        if (r == null) return;

        Assert.Empty(EsuSemaDogrulayici.Dogrula(XmlYardimci.Yukle(r.Value.RaporXml)));
    }
}
