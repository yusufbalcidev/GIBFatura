namespace GibEsu.Core.Servis;

public enum PaketDurumTuru
{
    Bilinmiyor,
    Isleniyor,
    Basarili,
    Hata,
}

/// <summary>getBatchStatus durum kodları (e-Arşiv Teknik Kılavuzu, Bölüm 10).</summary>
public static class DurumKodlari
{
    private static readonly Dictionary<int, string> Aciklamalar = new()
    {
        [30] = "Paket başarıyla işlendi",
        [15] = "Paket işlenmeye başlandı",
        [10] = "Dosya kuyrukta",
        [150] = "Aynı Paket ID",
        [151] = "Dosya okunamadı",
        [152] = "Zip dosyası açılamadı",
        [153] = "Pakette birden fazla dosya var",
        [154] = "İmza doğrulanamadı",
        [155] = "Başlık XML validasyon hatası",
        [156] = "Uyumsuz doküman versiyon tipi",
        [157] = "XML validasyon hatası",
        [158] = "Başlık paket ID ile paket adlandırılması uyumsuz",
        [159] = "İmza bulunamadı",
        [160] = "XML veritabanı yazım hatası",
        [161] = "Okuma hatası",
        [162] = "Veri tabanı hatası",
        [163] = "Sistem hatası",
        [164] = "Pakette dosya yok",
        [165] = "Max dosya boyutu hatası",
        [166] = "İstek imzası ve paket imzası uyuşmuyor",
        [167] = "İmza sahibi ile hazırlayan VKN/TCKN uyuşmuyor",
    };

    public static string Aciklama(int kod) =>
        Aciklamalar.TryGetValue(kod, out var a) ? a : "Bilinmeyen durum kodu";

    public static PaketDurumTuru Tur(int kod) => kod switch
    {
        30 => PaketDurumTuru.Basarili,
        10 or 15 => PaketDurumTuru.Isleniyor,
        >= 150 => PaketDurumTuru.Hata,
        _ => PaketDurumTuru.Bilinmiyor,
    };
}
