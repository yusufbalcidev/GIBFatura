using System.IO.Compression;

namespace GibEsu.Core.Rapor;

/// <summary>
/// e-Arşiv kılavuzu kuralları: dosya adı UUID + ".zip" (40 karakter), zip içinde aynı adlı tek XML,
/// açık boyut en fazla 100 MB.
/// </summary>
public static class PaketOlusturucu
{
    public const long EnBuyukAcikBoyut = 100L * 1024 * 1024;

    public static string DosyaAdi(Guid paketId) => paketId.ToString("D") + ".zip";

    public static byte[] Zip(Guid paketId, byte[] imzaliXml)
    {
        if (imzaliXml.LongLength > EnBuyukAcikBoyut)
            throw new InvalidOperationException(
                $"Rapor {imzaliXml.LongLength / 1024 / 1024} MB; GİB sınırı 100 MB. Rapor bölünmeli.");

        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            var kayit = zip.CreateEntry(paketId.ToString("D") + ".xml", CompressionLevel.Optimal);
            using var yazici = kayit.Open();
            yazici.Write(imzaliXml);
        }
        return ms.ToArray();
    }
}
