using System.Net.Http.Headers;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Xml;
using GibEsu.Core.Xml;

namespace GibEsu.Core.Servis;

public enum GibOrtami
{
    Test,
    Canli,
}

public sealed record PaketDurumu(int Kod, string? GibAciklamasi, string? PaketId, string? Vkn)
{
    public string Aciklama => DurumKodlari.Aciklama(Kod);
    public PaketDurumTuru Tur => DurumKodlari.Tur(Kod);
}

public sealed class GibYaniti
{
    public int HttpDurumu { get; init; }
    public string HamYanit { get; init; } = "";
    public string? DonusMetni { get; init; }
    public string? HataMesaji { get; init; }
    public PaketDurumu? Durum { get; init; }
    public bool HataVar => HataMesaji != null;
}

/// <summary>GİB'e giden bir SOAP isteği ve dönen yanıt, olduğu gibi (destek ve hata ayıklama için).</summary>
public sealed record SoapAlisverisi(
    string Eylem,
    GibOrtami Ortam,
    Uri Adres,
    DateTime Baslangic,
    TimeSpan Sure,
    IReadOnlyList<string> IstekBasliklari,
    byte[] Istek,
    int? HttpDurumu,
    IReadOnlyList<string> YanitBasliklari,
    byte[]? Yanit,
    Exception? Hata);

/// <summary>GİB ile yapılan her istek/yanıt alışverişini kaydeder.</summary>
public interface ISoapGunlugu
{
    void Kaydet(SoapAlisverisi alisveris);
}

/// <summary>GİB ESÜ rapor servisi (EArsivWsPort) istemcisi.</summary>
public sealed class GibEsuIstemcisi
{
    private readonly HttpClient _http;
    private readonly ISoapGunlugu? _gunluk;

    public GibEsuIstemcisi(HttpClient http, ISoapGunlugu? gunluk = null)
    {
        _http = http;
        _gunluk = gunluk;
    }

    // GİB'in okcdestek yazısında ve WSDL'de (…/EArsivWsPort/earsiv.wsdl) verilen gönderim adresleri.
    public const string VarsayilanCanliAdres = "https://okc.gib.gov.tr/okcesu/services/EArsivWsPort";
    public const string VarsayilanTestAdres = "https://okctest.gib.gov.tr/okcesu/services/EArsivWsPort";

    // WSDL'deki soapAction değerleri.
    internal const string GonderEylemi = "urn:SendDocumentFile";
    internal const string DurumEylemi = "urn:GetBatchStatus";

    private static Uri _canliAdres = new(VarsayilanCanliAdres);
    private static Uri _testAdres = new(VarsayilanTestAdres);

    public static Uri Adres(GibOrtami ortam) => ortam == GibOrtami.Canli ? _canliAdres : _testAdres;

    /// <summary>GİB adresi değişirse yeniden derlemeden appsettings.json'dan verilebilir. Boş değer varsayılanı korur.</summary>
    public static void AdresleriAyarla(string? canli, string? test)
    {
        var yeniCanli = string.IsNullOrWhiteSpace(canli) ? new Uri(VarsayilanCanliAdres) : AdresCoz(canli);
        var yeniTest = string.IsNullOrWhiteSpace(test) ? new Uri(VarsayilanTestAdres) : AdresCoz(test);
        _canliAdres = yeniCanli;
        _testAdres = yeniTest;
    }

    /// <summary>Mühürle imzalı istekler yalnızca https adreslere gönderilir.</summary>
    internal static Uri AdresCoz(string adres) =>
        Uri.TryCreate(adres.Trim(), UriKind.Absolute, out var uri) && uri.Scheme == Uri.UriSchemeHttps
            ? uri
            : throw new ArgumentException($"GİB adresi geçersiz, https:// ile başlamalı: {adres}");

    /// <summary>
    /// GİB sunucularının SSL sertifikası KamuSM Kurumsal Kök'e bağlanır; bu kök Windows'un güvenilir
    /// listesinde yoktur. Yalnızca gömülü KamuSM kökleriyle doğrulanan zincirler kabul edilir.
    /// </summary>
    public static HttpClient HttpIstemcisiOlustur(TimeSpan? zamanAsimi = null)
    {
        var isleyici = new SocketsHttpHandler
        {
            SslOptions = new SslClientAuthenticationOptions
            {
                RemoteCertificateValidationCallback = (_, sertifika, _, hatalar) => SertifikaGecerliMi(sertifika, hatalar),
            },
            PooledConnectionLifetime = TimeSpan.FromMinutes(5),
        };
        return new HttpClient(isleyici) { Timeout = zamanAsimi ?? TimeSpan.FromMinutes(5) };
    }

    internal static bool SertifikaGecerliMi(X509Certificate? sertifika, SslPolicyErrors hatalar)
    {
        if (hatalar == SslPolicyErrors.None)
            return true;
        if (sertifika == null || (hatalar & ~SslPolicyErrors.RemoteCertificateChainErrors) != 0)
            return false;

        using var zincir = new X509Chain();
        zincir.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
        zincir.ChainPolicy.CustomTrustStore.AddRange(KamuSmKokleri.Value);
        zincir.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
        using var x509 = new X509Certificate2(sertifika);
        return zincir.Build(x509);
    }

    internal static readonly Lazy<X509Certificate2[]> KamuSmKokleri = new(() =>
        new[] { "GibEsu.KamuSM_Kurumsal_Kok_S1.cer", "GibEsu.KamuSM_Kurumsal_Kok_S2.cer" }
            .Select(ad =>
            {
                using var akis = typeof(GibEsuIstemcisi).Assembly.GetManifestResourceStream(ad)!;
                using var ms = new MemoryStream();
                akis.CopyTo(ms);
                return new X509Certificate2(ms.ToArray());
            })
            .ToArray());

    public Task<GibYaniti> DosyaGonderAsync(byte[] imzaliZarf, GibOrtami ortam, CancellationToken iptal) =>
        CagirAsync(imzaliZarf, GonderEylemi, ortam, durumSorgusu: false, iptal);

    public Task<GibYaniti> DurumSorgulaAsync(byte[] imzaliZarf, GibOrtami ortam, CancellationToken iptal) =>
        CagirAsync(imzaliZarf, DurumEylemi, ortam, durumSorgusu: true, iptal);

    /// <summary>
    /// true iken canlı GİB'e hiçbir istek gönderilmez; deneme döneminde yanlışlıkla canlıya gitmeyi önler.
    /// Uygulama açılışta ayardan (CanliAcik) belirler.
    /// </summary>
    public static bool CanliKilitli { get; set; } = true;

    public const string CanliKilitliMesaji = "Canlı ortam kapalı; istek gönderilmedi. Yalnızca test ortamı kullanılabilir.";

    private async Task<GibYaniti> CagirAsync(byte[] zarf, string eylem, GibOrtami ortam, bool durumSorgusu, CancellationToken iptal)
    {
        if (ortam == GibOrtami.Canli && CanliKilitli)
            throw new GonderimHatasi(CanliKilitliMesaji);

        var adres = Adres(ortam);
        using var istek = new HttpRequestMessage(HttpMethod.Post, adres);
        istek.Content = new ByteArrayContent(zarf);
        // SOAP 1.2'de eylem Content-Type'ın action parametresinde taşınır; SOAPAction başlığı da eski istemcilerle uyum için gönderilir.
        istek.Content.Headers.ContentType = MediaTypeHeaderValue.Parse($"application/soap+xml; charset=utf-8; action=\"{eylem}\"");
        istek.Headers.TryAddWithoutValidation("SOAPAction", eylem);

        var baslangic = DateTime.Now;
        var sayac = System.Diagnostics.Stopwatch.StartNew();
        int? httpDurumu = null;
        byte[]? govde = null;
        IReadOnlyList<string> yanitBasliklari = Array.Empty<string>();
        Exception? hata = null;
        try
        {
            using var yanit = await _http.SendAsync(istek, iptal).ConfigureAwait(false);
            httpDurumu = (int)yanit.StatusCode;
            yanitBasliklari = Basliklar(yanit.Headers, yanit.Content.Headers, $"HTTP/{yanit.Version} {httpDurumu} {yanit.ReasonPhrase}");
            govde = await yanit.Content.ReadAsByteArrayAsync(iptal).ConfigureAwait(false);
            return Coz(httpDurumu.Value, Encoding.UTF8.GetString(govde), durumSorgusu);
        }
        catch (Exception ex)
        {
            hata = ex;
            throw;
        }
        finally
        {
            sayac.Stop();
            GunlugeYaz(new SoapAlisverisi(eylem, ortam, adres, baslangic, sayac.Elapsed,
                Basliklar(istek.Headers, istek.Content.Headers, $"POST {adres}"), zarf,
                httpDurumu, yanitBasliklari, govde, hata));
        }
    }

    private void GunlugeYaz(SoapAlisverisi alisveris)
    {
        try
        {
            _gunluk?.Kaydet(alisveris);
        }
        catch (Exception)
        {
            // Günlük yazılamaması gönderimin sonucunu değiştirmemeli.
        }
    }

    private static IReadOnlyList<string> Basliklar(HttpHeaders basliklar, HttpHeaders? icerikBasliklari, string ilkSatir)
    {
        var liste = new List<string> { ilkSatir };
        foreach (var b in basliklar.Concat(icerikBasliklari ?? Enumerable.Empty<KeyValuePair<string, IEnumerable<string>>>()))
            liste.Add($"{b.Key}: {string.Join(", ", b.Value)}");
        return liste;
    }

    internal static GibYaniti Coz(int httpDurumu, string metin, bool durumSorgusu)
    {
        XmlDocument doc;
        try
        {
            doc = XmlYardimci.Yukle(metin);
        }
        catch (XmlException)
        {
            return new GibYaniti
            {
                HttpDurumu = httpDurumu,
                HamYanit = metin,
                HataMesaji = $"GİB beklenmeyen bir yanıt döndü (HTTP {httpDurumu}).",
            };
        }

        var hata = (XmlElement?)doc.GetElementsByTagName("Fault", Ad.Soap12)[0];
        if (hata != null)
        {
            var neden = hata.GetElementsByTagName("Text", Ad.Soap12)[0]?.InnerText
                        ?? hata.InnerText;
            return new GibYaniti { HttpDurumu = httpDurumu, HamYanit = metin, HataMesaji = "GİB hata döndü: " + neden.Trim() };
        }

        var donus = (XmlElement?)doc.GetElementsByTagName("return")[0];
        if (donus == null)
        {
            return new GibYaniti
            {
                HttpDurumu = httpDurumu,
                HamYanit = metin,
                HataMesaji = $"GİB yanıtında sonuç bulunamadı (HTTP {httpDurumu}).",
            };
        }

        if (!durumSorgusu)
        {
            var mesaj = donus.InnerText.Trim();
            var basarili = mesaj.StartsWith("Dosya kaydedildi", StringComparison.OrdinalIgnoreCase) || mesaj.StartsWith("000");
            return new GibYaniti
            {
                HttpDurumu = httpDurumu,
                HamYanit = metin,
                DonusMetni = mesaj,
                HataMesaji = basarili ? null : "GİB dosyayı kabul etmedi: " + mesaj,
            };
        }

        string? Alan(string ad) => donus.GetElementsByTagName(ad)[0]?.InnerText.Trim();
        var kodMetni = Alan("durumKodu");
        if (!int.TryParse(kodMetni, out var kod))
        {
            return new GibYaniti
            {
                HttpDurumu = httpDurumu,
                HamYanit = metin,
                HataMesaji = "GİB yanıtında durum kodu yok: " + donus.InnerText.Trim(),
            };
        }

        return new GibYaniti
        {
            HttpDurumu = httpDurumu,
            HamYanit = metin,
            Durum = new PaketDurumu(kod, NullIfEmpty(Alan("durumAciklama")), Alan("paketId"), NullIfEmpty(Alan("vergiKimlikNo"))),
        };
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;
}
