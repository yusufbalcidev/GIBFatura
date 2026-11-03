using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using ClosedXML.Excel;

namespace GibEsu.Core.Rapor;

/// <summary>
/// Muhasebenin hazırladığı Excel'i okur. Zorunlu sütunlar: plaka, kwh, tl.
/// İsteğe bağlı sütunlar: aracKimlikNo, roamingVkn, roamingUnvan.
/// Aynı plakanın satırları toplanır; GİB plaka başına tek satır ister.
/// </summary>
public static class EsuExcelOkuyucu
{
    private const int EnFazlaMesaj = 200;

    /// <summary>Tek satırda kabul edilen en büyük kWh/tutar. Bunun üstü büyük olasılıkla hatalı hücredir.</summary>
    private const decimal EnBuyukDeger = 999_999_999m;

    /// <summary>Plaka başına aylık toplamda dikkat çekilecek eşikler (gönderimi engellemez).</summary>
    private const decimal OlagandisiTutar = 1_000_000m;
    private const decimal OlagandisiKwh = 100_000m;

    private static readonly Regex BelirsizBinlik = new(@"^-?\d{1,3}(\.\d{3})+$", RegexOptions.Compiled);

    private static readonly Regex GecerliPlaka = new("^[A-Z0-9]{2,15}$", RegexOptions.Compiled);
    private static readonly Regex TurkPlakasi = new("^(0[1-9]|[1-7][0-9]|8[01])[A-Z]{1,3}[0-9]{2,5}$", RegexOptions.Compiled);

    private static readonly Dictionary<string, string> SutunAdlari = new()
    {
        ["plaka"] = "plaka", ["plakano"] = "plaka", ["aracplakasi"] = "plaka", ["plate"] = "plaka",
        ["kwh"] = "kwh", ["hizmetmiktari"] = "kwh", ["toplamkwh"] = "kwh", ["kwhtoplam"] = "kwh",
        ["tl"] = "tl", ["tutar"] = "tl", ["toplamtutar"] = "tl", ["toplamtl"] = "tl", ["tutartl"] = "tl",
        ["arackimlikno"] = "arac", ["arackimlik"] = "arac", ["vin"] = "arac",
        ["roamingvkn"] = "rvkn", ["roamingunvan"] = "runvan",
    };

    public static ExcelOkumaSonucu Oku(string dosyaYolu)
    {
        // Excel'de açık dosya da okunabilsin.
        using var akis = new FileStream(dosyaYolu, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return Oku(akis);
    }

    public static ExcelOkumaSonucu Oku(Stream akis)
    {
        var sonuc = new ExcelOkumaSonucu();
        XLWorkbook kitap;
        try
        {
            kitap = new XLWorkbook(akis);
        }
        catch (Exception ex)
        {
            sonuc.Hatalar.Add("Excel dosyası açılamadı. Dosyanın .xlsx biçiminde olduğundan emin olun. (" + ex.Message + ")");
            return sonuc;
        }

        using (kitap)
        {
            var (sayfa, baslikSatiri, sutunlar) = BaslikBul(kitap);
            if (sayfa == null)
            {
                sonuc.Hatalar.Add("Excel'de 'plaka', 'kwh' ve 'tl' sütun başlıkları bulunamadı. İlk satırlardan biri bu başlıkları içermeli.");
                return sonuc;
            }

            var satirlar = new List<(int SatirNo, EsuSatiri Satir)>();
            var sonSatir = sayfa.LastRowUsed()?.RowNumber() ?? baslikSatiri;
            for (var r = baslikSatiri + 1; r <= sonSatir; r++)
            {
                var satir = sayfa.Row(r);
                var plakaHam = Metin(satir.Cell(sutunlar["plaka"]));
                var kwhHucre = satir.Cell(sutunlar["kwh"]);
                var tlHucre = satir.Cell(sutunlar["tl"]);
                if (plakaHam.Length == 0 && kwhHucre.Value.IsBlank && tlHucre.Value.IsBlank)
                    continue;
                sonuc.OkunanSatirSayisi++;

                var plaka = PlakaNormallestir(plakaHam);
                var hataVar = false;
                if (plaka.Length == 0)
                {
                    Hata(sonuc, $"{r}. satır: plaka boş.");
                    hataVar = true;
                }
                else if (!GecerliPlaka.IsMatch(plaka))
                {
                    Hata(sonuc, $"{r}. satır: plaka geçersiz ('{plakaHam}'). Yalnızca harf ve rakam olmalı.");
                    hataVar = true;
                }
                else if (!TurkPlakasi.IsMatch(plaka))
                {
                    Uyari(sonuc, $"{r}. satır: '{plaka}' Türkiye plaka biçimine uymuyor (yabancı plaka olabilir), kontrol edin.");
                }
                if (!hataVar && plakaHam.IndexOfAny(new[] { 'İ', 'ı' }) >= 0)
                    Uyari(sonuc, $"{r}. satır: '{plakaHam}' plakasındaki Türkçe karakter düzeltildi → '{plaka}'.");

                var kwh = Sayi(kwhHucre, "kWh", r, sonuc);
                var tl = Sayi(tlHucre, "tutar", r, sonuc);
                if (hataVar || kwh == null || tl == null)
                    continue;

                string? Istege(string anahtar) => sutunlar.TryGetValue(anahtar, out var s) ? Bos(Metin(satir.Cell(s))) : null;
                var roamingVkn = Istege("rvkn");
                if (roamingVkn != null && !(roamingVkn.Length is 10 or 11 && roamingVkn.All(char.IsAsciiDigit)))
                {
                    Hata(sonuc, $"{r}. satır: roaming VKN 10 haneli (veya TCKN 11 haneli) olmalı.");
                    continue;
                }
                var roamingUnvan = Istege("runvan");
                if ((roamingVkn == null) != (roamingUnvan == null))
                {
                    Hata(sonuc, $"{r}. satır: roaming için VKN ve unvan birlikte doldurulmalı.");
                    continue;
                }
                satirlar.Add((r, new EsuSatiri(plaka, kwh.Value, tl.Value, Istege("arac"), roamingVkn, roamingUnvan)));
            }

            if (sonuc.Hatalar.Count > 0)
                return sonuc;

            // Aynı plaka (ve aynı roaming işletmecisi) tek satıra toplanır.
            foreach (var grup in satirlar.GroupBy(x => (x.Satir.Plaka, x.Satir.RoamingVkn)))
            {
                var ilk = grup.First().Satir;
                var aracNolari = grup.Select(x => x.Satir.AracKimlikNo).Where(a => a != null).Distinct().ToList();
                if (aracNolari.Count > 1)
                    Uyari(sonuc, $"{ilk.Plaka}: birden fazla araç kimlik numarası var, ilki ({aracNolari[0]}) kullanıldı.");
                var unvanlar = grup.Select(x => x.Satir.RoamingUnvan).Where(u => u != null).Distinct().ToList();
                if (unvanlar.Count > 1)
                    Uyari(sonuc, $"{ilk.Plaka}: roaming VKN {ilk.RoamingVkn} için farklı unvanlar yazılmış, ilki ({unvanlar[0]}) kullanıldı.");
                sonuc.Satirlar.Add(ilk with
                {
                    Kwh = grup.Sum(x => x.Satir.Kwh),
                    Tutar = grup.Sum(x => x.Satir.Tutar),
                    AracKimlikNo = aracNolari.FirstOrDefault(),
                });
            }

            foreach (var s in sonuc.Satirlar.Where(s => s.Tutar > OlagandisiTutar || s.Kwh > OlagandisiKwh))
                Uyari(sonuc, $"{s.Plaka}: aylık toplam olağandışı yüksek ({s.Kwh:N2} kWh, {s.Tutar:N2} TL). Excel'i kontrol edin.");

            if (sonuc.Satirlar.Count == 0)
                sonuc.Hatalar.Add("Excel'de veri satırı bulunamadı.");
        }
        return sonuc;
    }

    public static string PlakaNormallestir(string plaka)
    {
        var sb = new StringBuilder(plaka.Length);
        foreach (var c in plaka.Trim())
        {
            if (c is ' ' or '-' or '.' or '\t' or ' ')
                continue;
            sb.Append(c switch
            {
                'i' or 'ı' or 'İ' => 'I',
                _ => char.ToUpperInvariant(c),
            });
        }
        return sb.ToString();
    }

    private static (IXLWorksheet? Sayfa, int Satir, Dictionary<string, int> Sutunlar) BaslikBul(XLWorkbook kitap)
    {
        foreach (var sayfa in kitap.Worksheets)
        {
            var son = Math.Min(sayfa.LastRowUsed()?.RowNumber() ?? 0, 10);
            for (var r = 1; r <= son; r++)
            {
                var sutunlar = new Dictionary<string, int>();
                foreach (var hucre in sayfa.Row(r).CellsUsed())
                {
                    if (SutunAdlari.TryGetValue(BaslikAnahtari(Metin(hucre)), out var anahtar) && !sutunlar.ContainsKey(anahtar))
                        sutunlar[anahtar] = hucre.Address.ColumnNumber;
                }
                if (sutunlar.ContainsKey("plaka") && sutunlar.ContainsKey("kwh") && sutunlar.ContainsKey("tl"))
                    return (sayfa, r, sutunlar);
            }
        }
        return (null, 0, new Dictionary<string, int>());
    }

    private static string BaslikAnahtari(string baslik)
    {
        var sb = new StringBuilder();
        foreach (var c in baslik.Trim())
        {
            var k = c switch
            {
                'İ' or 'I' or 'ı' => 'i',
                'Ş' or 'ş' => 's',
                'Ğ' or 'ğ' => 'g',
                'Ü' or 'ü' => 'u',
                'Ö' or 'ö' => 'o',
                'Ç' or 'ç' => 'c',
                _ => char.ToLowerInvariant(c),
            };
            if (char.IsLetterOrDigit(k))
                sb.Append(k);
        }
        return sb.ToString();
    }

    private static string Metin(IXLCell hucre)
    {
        var deger = hucre.Value;
        return deger.Type switch
        {
            XLDataType.Blank => "",
            XLDataType.Text => deger.GetText().Trim(),
            XLDataType.Number => deger.GetNumber().ToString(CultureInfo.InvariantCulture),
            _ => deger.ToString(CultureInfo.InvariantCulture).Trim(),
        };
    }

    private static decimal? Sayi(IXLCell hucre, string ad, int satirNo, ExcelOkumaSonucu sonuc)
    {
        var deger = hucre.Value;
        decimal sayi;
        if (deger.Type == XLDataType.Number)
        {
            var d = deger.GetNumber();
            if (double.IsNaN(d) || double.IsInfinity(d) || Math.Abs(d) > (double)EnBuyukDeger)
            {
                Hata(sonuc, $"{satirNo}. satır: {ad} çok büyük ({d.ToString(CultureInfo.InvariantCulture)}). Hücrede yanlış değer olabilir.");
                return null;
            }
            sayi = (decimal)d;
        }
        else if (deger.Type == XLDataType.Text && BinlikMiBelirsiz(deger.GetText()))
        {
            Hata(sonuc, $"{satirNo}. satır: {ad} '{deger.GetText().Trim()}' belirsiz: nokta binlik ayırıcı mı, ondalık mı anlaşılamıyor. " +
                        "Hücreyi sayı biçiminde girin ya da 12500 / 12,5 gibi yazın.");
            return null;
        }
        else if (deger.Type == XLDataType.Text && MetindenSayi(deger.GetText(), out var okunan))
        {
            sayi = okunan;
        }
        else if (deger.IsBlank)
        {
            Hata(sonuc, $"{satirNo}. satır: {ad} boş.");
            return null;
        }
        else
        {
            Hata(sonuc, $"{satirNo}. satır: {ad} sayı değil ('{Metin(hucre)}').");
            return null;
        }

        if (sayi <= 0)
        {
            Hata(sonuc, $"{satirNo}. satır: {ad} sıfırdan büyük olmalı.");
            return null;
        }
        if (sayi > EnBuyukDeger)
        {
            Hata(sonuc, $"{satirNo}. satır: {ad} çok büyük ({sayi.ToString(CultureInfo.InvariantCulture)}). Hücrede yanlış değer olabilir.");
            return null;
        }
        if (sayi != Math.Round(sayi, 2))
        {
            Hata(sonuc, $"{satirNo}. satır: {ad} en fazla 2 ondalık basamak içermeli ({sayi.ToString(CultureInfo.InvariantCulture)}).");
            return null;
        }
        return Math.Round(sayi, 2);
    }

    /// <summary>
    /// "12.500" gibi metinler Türkçede on iki bin beş yüz, İngilizcede on iki buçuk demektir.
    /// Yanlış yorumlamak tutarı bin kat değiştireceği için bu biçim kabul edilmez.
    /// </summary>
    internal static bool BinlikMiBelirsiz(string metin) =>
        BelirsizBinlik.IsMatch(ParaIsaretleriniAt(metin));

    /// <summary>"1.234,56", "1234,56", "1234.56" ve "₺1.250,75" biçimlerini okur.</summary>
    internal static bool MetindenSayi(string metin, out decimal sayi)
    {
        metin = ParaIsaretleriniAt(metin);
        var sonVirgul = metin.LastIndexOf(',');
        var sonNokta = metin.LastIndexOf('.');
        if (sonVirgul >= 0 && sonNokta >= 0)
        {
            metin = sonVirgul > sonNokta
                ? metin.Replace(".", "").Replace(',', '.')
                : metin.Replace(",", "");
        }
        else if (sonVirgul >= 0)
        {
            metin = metin.Replace(',', '.');
        }
        return decimal.TryParse(metin, NumberStyles.AllowDecimalPoint | NumberStyles.AllowLeadingSign,
            CultureInfo.InvariantCulture, out sayi);
    }

    private static string ParaIsaretleriniAt(string metin) => metin.Trim()
        .Replace(" ", "").Replace(" ", "").Replace("₺", "")
        .Replace("TRY", "", StringComparison.OrdinalIgnoreCase)
        .Replace("TL", "", StringComparison.OrdinalIgnoreCase);

    private static string? Bos(string s) => s.Length == 0 ? null : s;

    private static void Hata(ExcelOkumaSonucu sonuc, string mesaj)
    {
        if (sonuc.Hatalar.Count < EnFazlaMesaj)
            sonuc.Hatalar.Add(mesaj);
        else if (sonuc.Hatalar.Count == EnFazlaMesaj)
            sonuc.Hatalar.Add("... (daha fazla hata var, ilk " + EnFazlaMesaj + " tanesi gösteriliyor)");
    }

    private static void Uyari(ExcelOkumaSonucu sonuc, string mesaj)
    {
        if (sonuc.Uyarilar.Count < EnFazlaMesaj)
            sonuc.Uyarilar.Add(mesaj);
        else if (sonuc.Uyarilar.Count == EnFazlaMesaj)
            sonuc.Uyarilar.Add("... (daha fazla uyarı var)");
    }
}
