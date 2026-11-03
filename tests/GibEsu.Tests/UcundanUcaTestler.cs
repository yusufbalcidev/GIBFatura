using System.Text;
using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Servis;
using GibEsu.Core.Xml;

namespace GibEsu.Tests;

public class UcundanUcaTestler
{
    private static ExcelOkumaSonucu OrnekVeri() => EsuExcelOkuyucu.Oku(TestYardimci.Excel(
        new object?[] { "plaka", "kwh", "tl" },
        new object?[] { "34 NB 1461", 73.81, 944.77 },
        new object?[] { "34nns904", 142.45, 1435.39 },
        new object?[] { "34NB1461", 10.0, 100.0 }));

    public static IEnumerable<object[]> Imzacilar() => new[]
    {
        new object[] { "EC" },
        new object[] { "RSA" },
    };

    private static IMuhurImzaci Imzaci(string tur) => tur == "EC" ? TestYardimci.EcImzaci() : TestYardimci.RsaImzaci();

    [Theory]
    [MemberData(nameof(Imzacilar))]
    public void PaketHazirla_GecerliImzaliPaketUretir(string tur)
    {
        using var imzaci = Imzaci(tur);

        var paket = EsuGonderimServisi.PaketHazirla(OrnekVeri(), 2026, 9, imzaci);

        Assert.Equal(40, paket.DosyaAdi.Length);
        var xml = TestYardimci.ZiptekiXml(paket.Zip, out var kayitAdi);
        Assert.Equal(paket.PaketId + ".xml", kayitAdi);
        Assert.Equal(paket.ImzaliXml, xml);

        var doc = XmlYardimci.Yukle(xml);
        Assert.Empty(EsuSemaDogrulayici.Dogrula(doc));
        var dogrulama = ImzaDogrulayici.XadesDogrula(doc);
        Assert.True(dogrulama.Gecerli, dogrulama.ToString());

        var metin = Encoding.UTF8.GetString(xml);
        Assert.Contains("<earsiv:raporNo>" + paket.PaketId + "</earsiv:raporNo>", metin);
        Assert.Contains("<earsiv:donemBaslangicTarihi>2026-09-01</earsiv:donemBaslangicTarihi>", metin);
        Assert.Contains("<earsiv:donemBitisTarihi>2026-09-30</earsiv:donemBitisTarihi>", metin);
        Assert.Contains("<earsiv:hazirlayan>", metin);
        Assert.Contains("<xades:ClaimedRole>Şarj Ağı İşletmecisi</xades:ClaimedRole>", metin);
        Assert.Contains("<earsiv:plakaNo>34NB1461</earsiv:plakaNo>", metin);
        Assert.Contains("<earsiv:hizmetMiktari unitCode=\"kw/h\">83.81</earsiv:hizmetMiktari>", metin);
        Assert.Contains("<earsiv:toplamTutar>1044.77</earsiv:toplamTutar>", metin);
        Assert.Contains("<earsiv:toplamTutar>1435.39</earsiv:toplamTutar>", metin);
        Assert.DoesNotContain("\r", metin);
        Assert.Equal(2, paket.SatirSayisi);
    }

    [Fact]
    public void ImzaliRapor_WindowsSatirSonlarinaCevrilseDeGecerli()
    {
        using var imzaci = TestYardimci.EcImzaci();
        var paket = EsuGonderimServisi.PaketHazirla(OrnekVeri(), 2026, 9, imzaci);

        var crlf = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(paket.ImzaliXml).Replace("\n", "\r\n"));

        Assert.True(ImzaDogrulayici.XadesDogrula(XmlYardimci.Yukle(crlf)).Gecerli);
    }

    [Fact]
    public void ImzaliRapor_PlakaDegisirseGecersiz()
    {
        using var imzaci = TestYardimci.EcImzaci();
        var paket = EsuGonderimServisi.PaketHazirla(OrnekVeri(), 2026, 9, imzaci);

        var bozuk = Encoding.UTF8.GetBytes(Encoding.UTF8.GetString(paket.ImzaliXml).Replace("34NB1461", "34NB1462"));

        Assert.False(ImzaDogrulayici.XadesDogrula(XmlYardimci.Yukle(bozuk)).Gecerli);
    }

    [Theory]
    [MemberData(nameof(Imzacilar))]
    public void SoapZarfi_ImzaliVeGecerli(string tur)
    {
        using var imzaci = Imzaci(tur);
        var zip = new byte[] { 1, 2, 3 };

        var zarf = WsSecurityImzalayici.ImzaliZarf(
            WsSecurityImzalayici.DosyaGonderGovdesi("550e8400-e29b-41d4-a716-446655440000.zip", zip), imzaci, DateTime.UtcNow);

        var doc = XmlYardimci.Yukle(zarf);
        var sonuc = ImzaDogrulayici.WsSecurityDogrula(doc);
        Assert.True(sonuc.Gecerli, sonuc.ToString());
        Assert.Equal(Convert.ToBase64String(zip), doc.GetElementsByTagName("binaryData")[0]!.InnerText);
        Assert.Equal("550e8400-e29b-41d4-a716-446655440000.zip", doc.GetElementsByTagName("fileName")[0]!.InnerText);
    }

    [Fact]
    public void SoapZarfi_GovdeDegisirseGecersiz()
    {
        using var imzaci = TestYardimci.EcImzaci();
        var zarf = WsSecurityImzalayici.ImzaliZarf(
            WsSecurityImzalayici.DurumSorguGovdesi("550e8400-e29b-41d4-a716-446655440000"), imzaci, DateTime.UtcNow);

        var bozuk = Encoding.UTF8.GetString(zarf).Replace("550e8400", "650e8400");

        Assert.False(ImzaDogrulayici.WsSecurityDogrula(XmlYardimci.Yukle(bozuk)).Gecerli);
    }

    [Fact]
    public void PaketHazirla_VknsizSertifikayiReddeder()
    {
        using var ec = System.Security.Cryptography.ECDsa.Create(System.Security.Cryptography.ECCurve.NamedCurves.nistP384);
        var istek = new System.Security.Cryptography.X509Certificates.CertificateRequest("CN=VKN YOK", ec, System.Security.Cryptography.HashAlgorithmName.SHA384);
        using var imzaci = new YazilimImzaci(istek.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddDays(1)));

        Assert.Throws<GonderimHatasi>(() => EsuGonderimServisi.PaketHazirla(OrnekVeri(), 2026, 9, imzaci));
    }
}
