using System.Security.Cryptography;
using GibEsu.Core.Imza;
using GibEsu.Core.Servis;

namespace GibEsu.Tests;

public class ServisTestleri
{
    [Fact]
    public void DosyaGonderYaniti_DosyaKaydedildi()
    {
        const string yanit = "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Header/><env:Body><ns3:sendDocumentFileResponse xmlns:ns3=\"http://earsiv.vedop3.ggm.gov.org/\"><return>Dosya kaydedildi</return></ns3:sendDocumentFileResponse></env:Body></env:Envelope>";

        var sonuc = GibEsuIstemcisi.Coz(200, yanit, durumSorgusu: false);

        Assert.False(sonuc.HataVar);
        Assert.Equal("Dosya kaydedildi", sonuc.DonusMetni);
    }

    [Fact]
    public void DurumYaniti_15IslenmeyeBaslandiDirBasariDegil()
    {
        const string yanit = "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Header/><env:Body><ns3:getBatchStatusResponse xmlns:ns3=\"http://earsiv.vedop3.ggm.gov.org/\"><return><durumAciklama/><durumKodu>15</durumKodu><paketId>550e8400-e29b-41d4-a716-446655440000</paketId><TCKimlikNo/><vergiKimlikNo>1234567890</vergiKimlikNo></return></ns3:getBatchStatusResponse></env:Body></env:Envelope>";

        var sonuc = GibEsuIstemcisi.Coz(200, yanit, durumSorgusu: true);

        Assert.NotNull(sonuc.Durum);
        Assert.Equal(15, sonuc.Durum!.Kod);
        Assert.Equal(PaketDurumTuru.Isleniyor, sonuc.Durum.Tur);
        Assert.Equal("Paket işlenmeye başlandı", sonuc.Durum.Aciklama);
        Assert.Equal("1234567890", sonuc.Durum.Vkn);
    }

    [Fact]
    public void SoapHatasiOkunur()
    {
        const string yanit = "<env:Envelope xmlns:env=\"http://www.w3.org/2003/05/soap-envelope\"><env:Body><env:Fault><env:Code><env:Value>env:Receiver</env:Value></env:Code><env:Reason><env:Text xml:lang=\"en\">001 Gönderici imza yetkisi yok</env:Text></env:Reason></env:Fault></env:Body></env:Envelope>";

        var sonuc = GibEsuIstemcisi.Coz(500, yanit, durumSorgusu: false);

        Assert.True(sonuc.HataVar);
        Assert.Contains("001 Gönderici imza yetkisi yok", sonuc.HataMesaji);
    }

    [Theory]
    [InlineData(30, PaketDurumTuru.Basarili)]
    [InlineData(10, PaketDurumTuru.Isleniyor)]
    [InlineData(15, PaketDurumTuru.Isleniyor)]
    [InlineData(154, PaketDurumTuru.Hata)]
    [InlineData(167, PaketDurumTuru.Hata)]
    public void DurumKoduTurleri(int kod, PaketDurumTuru tur) => Assert.Equal(tur, DurumKodlari.Tur(kod));

    [Fact]
    public void DerImzaHamBicimeCevrilir()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var ozet = SHA384.HashData(new byte[] { 1, 2, 3 });
        var der = ec.SignHash(ozet, DSASignatureFormat.Rfc3279DerSequence);

        var ham = Pkcs11Imzaci.HamImzayaCevir(der, 48);

        Assert.Equal(96, ham.Length);
        Assert.True(ec.VerifyHash(ozet, ham, DSASignatureFormat.IeeeP1363FixedFieldConcatenation));
    }

    [Fact]
    public void HamImzaDerBicimineCevrilir()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var ozet = SHA384.HashData(new byte[] { 4, 5, 6 });
        var ham = ec.SignHash(ozet, DSASignatureFormat.IeeeP1363FixedFieldConcatenation);

        var der = XadesImzalayici.HamdanDer(ham);

        Assert.True(ec.VerifyHash(ozet, der, DSASignatureFormat.Rfc3279DerSequence));
    }

    [Fact]
    public void RsaDigestInfoDogru()
    {
        using var rsa = RSA.Create(2048);
        var ozet = SHA256.HashData(new byte[] { 7 });
        var digestInfo = Pkcs11Imzaci.DigestInfo(ozet, HashAlgorithmName.SHA256);

        // CKM_RSA_PKCS ile imzalanan DigestInfo, standart PKCS#1 v1.5 SHA-256 imzasıyla aynıdır.
        Assert.Equal("3031300D060960864801650304020105000420", Convert.ToHexString(digestInfo[..19]));
        Assert.Equal(ozet, digestInfo[19..]);
    }

    [Fact]
    public void KamuSmKokleriYuklenir()
    {
        var kokler = GibEsuIstemcisi.KamuSmKokleri.Value;

        Assert.Contains(kokler, k => k.Thumbprint == "6D46A9987D551DC897D620B31C96088F078E5492");
    }
}
