using System.IO.Compression;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using ClosedXML.Excel;
using GibEsu.Core.Imza;

namespace GibEsu.Tests;

internal static class TestYardimci
{
    public const string TestVkn = "1234567890";

    /// <summary>Mali mühür benzeri (konu seri no = VKN) kendinden imzalı test sertifikası.</summary>
    public static YazilimImzaci EcImzaci()
    {
        using var ec = ECDsa.Create(ECCurve.NamedCurves.nistP384);
        var istek = new CertificateRequest($"SERIALNUMBER={TestVkn}, CN=EKA TEST MUHUR", ec, HashAlgorithmName.SHA384);
        istek.CertificateExtensions.Add(new X509KeyUsageExtension(X509KeyUsageFlags.DigitalSignature, true));
        var sertifika = istek.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        return new YazilimImzaci(sertifika);
    }

    public static YazilimImzaci RsaImzaci()
    {
        using var rsa = RSA.Create(2048);
        var istek = new CertificateRequest($"SERIALNUMBER={TestVkn}, CN=EKA TEST MUHUR RSA", rsa, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1);
        var sertifika = istek.CreateSelfSigned(DateTimeOffset.Now.AddDays(-1), DateTimeOffset.Now.AddYears(1));
        return new YazilimImzaci(sertifika);
    }

    public static MemoryStream Excel(params object?[][] satirlar)
    {
        using var kitap = new XLWorkbook();
        var sayfa = kitap.AddWorksheet("Eylül");
        for (var r = 0; r < satirlar.Length; r++)
        for (var c = 0; c < satirlar[r].Length; c++)
        {
            var hucre = sayfa.Cell(r + 1, c + 1);
            switch (satirlar[r][c])
            {
                case null: break;
                case string s: hucre.Value = s; break;
                case double d: hucre.Value = d; break;
                case int i: hucre.Value = i; break;
                case decimal m: hucre.Value = (double)m; break;
            }
        }
        var ms = new MemoryStream();
        kitap.SaveAs(ms);
        ms.Position = 0;
        return ms;
    }

    /// <summary>Eylül 2026 gönderiminin log dosyası (GİB'in kabul ettiği gerçek gönderim). Yoksa null.</summary>
    public static string? ReferansLogu()
    {
        for (var dizin = new DirectoryInfo(AppContext.BaseDirectory); dizin != null; dizin = dizin.Parent)
        {
            var aday = Path.Combine(dizin.FullName, "GİB-Rapor-Eylül2026.log");
            if (File.Exists(aday))
                return aday;
        }
        return null;
    }

    /// <summary>Log'daki imzalı SOAP mesajını çıkarır.</summary>
    public static string? ImzaliSoap(string logYolu)
    {
        var satirlar = File.ReadAllLines(logYolu, Encoding.Latin1);
        for (var i = 0; i < satirlar.Length; i++)
        {
            var satir = satirlar[i].StartsWith("INFO: ") ? satirlar[i][6..] : satirlar[i];
            if (!satir.StartsWith("<soap:Envelope") || i + 3 >= satirlar.Length || !satirlar[i + 3].Contains("BinarySecurityToken"))
                continue;
            var sb = new StringBuilder(satir).Append('\n');
            for (var j = i + 1; j < satirlar.Length; j++)
            {
                sb.Append(satirlar[j]).Append('\n');
                if (satirlar[j].TrimStart().StartsWith("</soap:Envelope>"))
                    return sb.ToString();
            }
        }
        return null;
    }

    public static byte[] ZiptekiXml(byte[] zip, out string kayitAdi)
    {
        using var arsiv = new ZipArchive(new MemoryStream(zip), ZipArchiveMode.Read);
        Assert.Single(arsiv.Entries);
        var kayit = arsiv.Entries[0];
        kayitAdi = kayit.FullName;
        using var akis = kayit.Open();
        using var ms = new MemoryStream();
        akis.CopyTo(ms);
        return ms.ToArray();
    }
}
