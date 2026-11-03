using System.Numerics;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace GibEsu.Core.Imza;

public static class SertifikaBilgisi
{
    private const string SeriNoOid = "2.5.4.5";

    private static readonly Dictionary<string, string> KisaAdlar = new()
    {
        ["2.5.4.3"] = "CN",
        ["2.5.4.6"] = "C",
        ["2.5.4.7"] = "L",
        ["2.5.4.8"] = "ST",
        ["2.5.4.9"] = "STREET",
        ["2.5.4.10"] = "O",
        ["2.5.4.11"] = "OU",
        ["0.9.2342.19200300.100.1.1"] = "UID",
        ["0.9.2342.19200300.100.1.25"] = "DC",
    };

    /// <summary>Mali mühür sertifikasının konu alanındaki seri numarası (VKN veya TCKN).</summary>
    public static string? VknVeyaTckn(X509Certificate2 sertifika)
    {
        foreach (var rdn in sertifika.SubjectName.EnumerateRelativeDistinguishedNames())
        {
            if (rdn.GetSingleElementType().Value == SeriNoOid)
            {
                var deger = rdn.GetSingleElementValue()?.Trim();
                if (deger != null && (deger.Length == 10 || deger.Length == 11) && deger.All(char.IsAsciiDigit))
                    return deger;
            }
        }
        return null;
    }

    public static string OrtakAd(X509Certificate2 sertifika) =>
        sertifika.GetNameInfo(X509NameType.SimpleName, false);

    /// <summary>Seri numarasının ondalık gösterimi (XAdES IssuerSerial için).</summary>
    public static string SeriNoOndalik(X509Certificate2 sertifika) =>
        new BigInteger(sertifika.GetSerialNumber(), isUnsigned: true, isBigEndian: false).ToString();

    /// <summary>Yayıncı adını RFC 2253 biçiminde yazar (ör. "CN=...,OU=...,C=TR").</summary>
    public static string YayinciRfc2253(X509Certificate2 sertifika)
    {
        var parcalar = new List<string>();
        foreach (var rdn in sertifika.IssuerName.EnumerateRelativeDistinguishedNames())
        {
            var oid = rdn.GetSingleElementType().Value ?? "";
            var ad = KisaAdlar.TryGetValue(oid, out var kisa) ? kisa : oid;
            parcalar.Add(ad + "=" + Kacis(rdn.GetSingleElementValue() ?? ""));
        }
        return string.Join(",", parcalar);
    }

    private static string Kacis(string deger)
    {
        var sb = new StringBuilder();
        for (var i = 0; i < deger.Length; i++)
        {
            var c = deger[i];
            var kacisGerekli = c is ',' or '+' or '"' or '\\' or '<' or '>' or ';'
                || (i == 0 && (c == ' ' || c == '#'))
                || (i == deger.Length - 1 && c == ' ');
            if (kacisGerekli)
                sb.Append('\\');
            sb.Append(c);
        }
        return sb.ToString();
    }
}
