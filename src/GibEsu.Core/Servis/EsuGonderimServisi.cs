using GibEsu.Core.Imza;
using GibEsu.Core.Rapor;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Servis;

public sealed class HazirPaket
{
    public required Guid PaketId { get; init; }
    public required string DosyaAdi { get; init; }
    public required byte[] Zip { get; init; }
    public required byte[] ImzaliXml { get; init; }
    public required string Vkn { get; init; }
    public required DateOnly DonemBaslangic { get; init; }
    public required DateOnly DonemBitis { get; init; }
    public required int SatirSayisi { get; init; }
    public required decimal ToplamKwh { get; init; }
    public required decimal ToplamTutar { get; init; }
}

public sealed class GonderimHatasi : Exception
{
    public GonderimHatasi(string mesaj, Exception? ic = null) : base(mesaj, ic)
    {
    }
}

/// <summary>
/// Uçtan uca akış: Excel verisi → rapor XML → XAdES imza → şema ve imza kontrolü → zip →
/// WS-Security imzalı SOAP → GİB. Her imzadan sonra sonuç kendi doğrulayıcımızla kontrol edilir;
/// hatalı paket GİB'e hiç gönderilmez.
/// </summary>
public sealed class EsuGonderimServisi
{
    private readonly GibEsuIstemcisi _istemci;

    public EsuGonderimServisi(GibEsuIstemcisi istemci) => _istemci = istemci;

    public static HazirPaket PaketHazirla(ExcelOkumaSonucu veri, int yil, int ay, IMuhurImzaci imzaci,
        string rol = XadesImzalayici.VarsayilanRol)
    {
        if (!veri.Gecerli)
            throw new GonderimHatasi("Excel verisinde hata var; önce hataları düzeltin.");

        var vkn = SertifikaBilgisi.VknVeyaTckn(imzaci.Sertifika)
            ?? throw new GonderimHatasi("Mühür sertifikasında VKN bulunamadı. Doğru kartı seçtiğinizden emin olun.");
        if (imzaci.Sertifika.NotAfter < DateTime.Now)
            throw new GonderimHatasi($"Mühür sertifikasının süresi {imzaci.Sertifika.NotAfter:dd.MM.yyyy} tarihinde dolmuş.");

        var (bas, bit) = RaporBilgisi.Ay(yil, ay);
        var paketId = Guid.NewGuid();
        // GİB 167 kodu: imza sahibi ile hazırlayan VKN aynı olmalı.
        var bilgi = new RaporBilgisi(vkn, vkn, paketId, bas, bit, veri.Satirlar);

        var imzali = XadesImzalayici.Imzala(EsuRaporOlusturucu.Olustur(bilgi), imzaci, rol);
        var xml = XmlYardimci.Kaydet(imzali, xmlBildirimi: true);

        // Gönderilecek baytları yeniden okuyup kontrol et (GİB'in göreceği hali).
        var okunan = XmlYardimci.Yukle(xml);
        var semaHatalari = EsuSemaDogrulayici.Dogrula(okunan);
        if (semaHatalari.Count > 0)
            throw new GonderimHatasi("Rapor GİB şemasına uymuyor: " + string.Join(" | ", semaHatalari.Take(5)));
        var dogrulama = ImzaDogrulayici.XadesDogrula(okunan);
        if (!dogrulama.Gecerli)
            throw new GonderimHatasi("Rapor imzası kontrolden geçmedi: " + string.Join("; ", dogrulama.Hatalar));

        return new HazirPaket
        {
            PaketId = paketId,
            DosyaAdi = PaketOlusturucu.DosyaAdi(paketId),
            Zip = PaketOlusturucu.Zip(paketId, xml),
            ImzaliXml = xml,
            Vkn = vkn,
            DonemBaslangic = bas,
            DonemBitis = bit,
            SatirSayisi = veri.Satirlar.Count,
            ToplamKwh = veri.ToplamKwh,
            ToplamTutar = veri.ToplamTutar,
        };
    }

    /// <summary>
    /// GEÇİCİ DENEME (appsettings: TestKisaDosyaAdi): test ortamının 15.10.2026'dan beri verdiği DataException
    /// 40 karakterlik dosya adından mı geliyor? true iken yalnızca TEST gönderiminde dosya adı ve zip içindeki
    /// XML adı paket ID'nin ilk 8 karakterine kısaltılır. Canlı gönderim her zaman raporNo.zip kullanır.
    /// </summary>
    public static bool TestKisaDosyaAdi { get; set; }

    public static (string DosyaAdi, byte[] Zip) GonderilecekDosya(HazirPaket paket, GibOrtami ortam) =>
        GonderilecekDosya(paket, ortam, TestKisaDosyaAdi);

    public static (string DosyaAdi, byte[] Zip) GonderilecekDosya(HazirPaket paket, GibOrtami ortam, bool testKisaDosyaAdi)
    {
        if (ortam != GibOrtami.Test || !testKisaDosyaAdi)
            return (paket.DosyaAdi, paket.Zip);
        var ad = paket.PaketId.ToString("N")[..8];
        return (ad + ".zip", PaketOlusturucu.Zip(ad, paket.ImzaliXml));
    }

    public async Task<GibYaniti> GonderAsync(HazirPaket paket, IMuhurImzaci imzaci, GibOrtami ortam, CancellationToken iptal)
    {
        var saat = await NtpSaati.SimdiAsync(iptal).ConfigureAwait(false);
        var (dosyaAdi, zip) = GonderilecekDosya(paket, ortam);
        var zarf = ImzaliZarf(WsSecurityImzalayici.DosyaGonderGovdesi(dosyaAdi, zip), imzaci, saat.Utc);
        return await _istemci.DosyaGonderAsync(zarf, ortam, iptal).ConfigureAwait(false);
    }

    public async Task<GibYaniti> DurumSorgulaAsync(string paketId, IMuhurImzaci imzaci, GibOrtami ortam, CancellationToken iptal)
    {
        if (!Guid.TryParse(paketId, out var id))
            throw new GonderimHatasi("Paket ID UUID biçiminde olmalı (ör. 550e8400-e29b-41d4-a716-446655440000).");
        var saat = await NtpSaati.SimdiAsync(iptal).ConfigureAwait(false);
        var zarf = ImzaliZarf(WsSecurityImzalayici.DurumSorguGovdesi(id.ToString("D")), imzaci, saat.Utc);
        return await _istemci.DurumSorgulaAsync(zarf, ortam, iptal).ConfigureAwait(false);
    }

    private static byte[] ImzaliZarf(string govde, IMuhurImzaci imzaci, DateTime simdiUtc)
    {
        var zarf = WsSecurityImzalayici.ImzaliZarf(govde, imzaci, simdiUtc);
        var dogrulama = ImzaDogrulayici.WsSecurityDogrula(XmlYardimci.Yukle(zarf));
        if (!dogrulama.Gecerli)
            throw new GonderimHatasi("SOAP imzası kontrolden geçmedi: " + string.Join("; ", dogrulama.Hatalar));
        return zarf;
    }
}
