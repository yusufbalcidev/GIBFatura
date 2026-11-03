using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using GibEsu.Core.Servis;
using Microsoft.Extensions.Options;

namespace GibEsu.Web.Servisler;

/// <summary>
/// GİB'e giden her SOAP isteğini ve dönen yanıtı kaydeder.
/// Ana loga okunabilir bir özet yazılır (rapor içeriği ve sertifika kısaltılır);
/// isteğin ve yanıtın tamamı loglar\soap\yyyyMMdd altında ayrı bir dosyada tutulur.
/// GİB desteğine sorun bildirirken bu dosya olduğu gibi gönderilebilir.
/// </summary>
public sealed partial class SoapGunlugu : ISoapGunlugu
{
    private const int SatirIciSinir = 4000;
    private static readonly UTF8Encoding Utf8 = new(false);

    private readonly string _klasor;
    private readonly ILogger<SoapGunlugu> _log;

    public SoapGunlugu(IOptions<Ayarlar> ayarlar, ILogger<SoapGunlugu> log)
    {
        _klasor = Path.Combine(ayarlar.Value.LogKlasoruYolu, "soap");
        _log = log;
    }

    public void Kaydet(SoapAlisverisi a)
    {
        var istek = Utf8.GetString(a.Istek);
        var yanit = a.Yanit != null ? Utf8.GetString(a.Yanit) : null;
        var paketId = PaketIdBul(istek);
        var dosya = DosyaYaz(a, istek, yanit, paketId);

        var ozet = new StringBuilder();
        ozet.AppendLine($"GİB {Eylem(a.Eylem)} – {a.Ortam} – {a.Adres}");
        ozet.AppendLine($"  Paket: {paketId ?? "-"} | Süre: {a.Sure.TotalMilliseconds:0} ms | " +
                        $"HTTP: {(a.HttpDurumu?.ToString() ?? "yanıt yok")} | İstek: {a.Istek.Length:N0} bayt | Yanıt: {(a.Yanit?.Length ?? 0):N0} bayt");
        var zip = ZipBilgisi(istek);
        if (zip != null)
            ozet.AppendLine("  " + zip);
        ozet.AppendLine("  İstek başlıkları: " + string.Join(" | ", a.IstekBasliklari));
        if (a.YanitBasliklari.Count > 0)
            ozet.AppendLine("  Yanıt başlıkları: " + string.Join(" | ", a.YanitBasliklari));
        ozet.AppendLine("  İstek (kısaltılmış): " + Sinirla(Kisalt(istek)));
        if (yanit != null)
            ozet.AppendLine("  Yanıt: " + Sinirla(yanit.Trim()));
        if (a.Hata != null)
            ozet.AppendLine($"  Bağlantı hatası: {a.Hata.GetType().Name}: {a.Hata.Message}");
        ozet.Append("  Tam kayıt: " + (dosya ?? "yazılamadı"));

        if (a.Hata != null || a.HttpDurumu is null or >= 400 || yanit?.Contains("Fault>", StringComparison.Ordinal) == true)
            _log.LogWarning("{Ozet}", ozet.ToString());
        else
            _log.LogInformation("{Ozet}", ozet.ToString());
    }

    private string? DosyaYaz(SoapAlisverisi a, string istek, string? yanit, string? paketId)
    {
        try
        {
            var gunKlasoru = Path.Combine(_klasor, a.Baslangic.ToString("yyyyMMdd"));
            Directory.CreateDirectory(gunKlasoru);
            var ad = $"{a.Baslangic:HHmmss-fff}_{Eylem(a.Eylem)}_{a.Ortam}" + (paketId != null ? "_" + paketId : "") + ".txt";
            var yol = Path.Combine(gunKlasoru, ad);

            var sb = new StringBuilder();
            sb.AppendLine($"Zaman      : {a.Baslangic:yyyy-MM-dd HH:mm:ss.fff}");
            sb.AppendLine($"Ortam      : {a.Ortam}");
            sb.AppendLine($"Adres      : {a.Adres}");
            sb.AppendLine($"Eylem      : {a.Eylem}");
            sb.AppendLine($"Paket ID   : {paketId ?? "-"}");
            sb.AppendLine($"Süre       : {a.Sure.TotalMilliseconds:0} ms");
            sb.AppendLine($"HTTP durumu: {(a.HttpDurumu?.ToString() ?? "yanıt alınamadı")}");
            if (a.Hata != null)
                sb.AppendLine($"Hata       : {a.Hata}");
            sb.AppendLine();
            sb.AppendLine("========== GİDEN İSTEK ==========");
            foreach (var b in a.IstekBasliklari)
                sb.AppendLine(b);
            sb.AppendLine();
            sb.AppendLine(istek);
            sb.AppendLine();
            sb.AppendLine("========== DÖNEN YANIT ==========");
            foreach (var b in a.YanitBasliklari)
                sb.AppendLine(b);
            sb.AppendLine();
            sb.AppendLine(yanit ?? "(yanıt yok)");

            File.WriteAllText(yol, sb.ToString(), Utf8);
            return yol;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            _log.LogWarning("SOAP kaydı dosyaya yazılamadı: {Mesaj}", ex.Message);
            return null;
        }
    }

    private static string Eylem(string eylem) => eylem.StartsWith("urn:", StringComparison.Ordinal) ? eylem[4..] : eylem;

    internal static string? PaketIdBul(string istek)
    {
        var m = DosyaAdiDeseni().Match(istek);
        if (m.Success)
            return m.Groups[1].Value;
        m = PaketIdDeseni().Match(istek);
        return m.Success ? m.Groups[1].Value : null;
    }

    /// <summary>Gönderilen zip'in adı, boyutu ve SHA-256'sı; arşivdeki paketle karşılaştırmak için.</summary>
    private static string? ZipBilgisi(string istek)
    {
        var m = IkiliVeriDeseni().Match(istek);
        if (!m.Success)
            return null;
        try
        {
            var zip = Convert.FromBase64String(m.Groups[1].Value);
            return $"Zip: {zip.Length:N0} bayt, SHA-256 {Convert.ToHexString(SHA256.HashData(zip))}";
        }
        catch (FormatException)
        {
            return "Zip: base64 çözülemedi";
        }
    }

    /// <summary>Rapor içeriğini (zip) ve sertifikayı ana logda yer kaplamasın diye kısaltır.</summary>
    internal static string Kisalt(string istek)
    {
        var s = IkiliVeriDeseni().Replace(istek, m => $"<binaryData>[{m.Groups[1].Length:N0} karakter base64 – tam kayıt dosyada]</binaryData>");
        s = SertifikaDeseni().Replace(s, m => $"{m.Groups[1].Value}[sertifika, {m.Groups[2].Length:N0} karakter]{m.Groups[3].Value}");
        return BoslukDeseni().Replace(s, " ").Trim();
    }

    private static string Sinirla(string s) => s.Length <= SatirIciSinir ? s : s[..SatirIciSinir] + $" …[{s.Length - SatirIciSinir:N0} karakter daha, tam kayıt dosyada]";

    [GeneratedRegex(@"<fileName>([^<]+?)\.zip</fileName>")]
    private static partial Regex DosyaAdiDeseni();

    [GeneratedRegex(@"<paketId>([^<]+)</paketId>")]
    private static partial Regex PaketIdDeseni();

    [GeneratedRegex(@"<binaryData>([^<]*)</binaryData>")]
    private static partial Regex IkiliVeriDeseni();

    [GeneratedRegex(@"(<wsse:BinarySecurityToken[^>]*>)([^<]*)(</wsse:BinarySecurityToken>)")]
    private static partial Regex SertifikaDeseni();

    [GeneratedRegex(@"\s*\n\s*")]
    private static partial Regex BoslukDeseni();
}
