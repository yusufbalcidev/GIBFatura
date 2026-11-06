using ClosedXML.Excel;

namespace GibEsu.Core.Rapor;

/// <summary>
/// GİB'e gönderilen satırlardan (plaka başına aylık toplam) Excel üretir.
/// "Rapor" sayfası yalnızca plaka satırlarını içerir ve uygulamaya yeniden yüklenebilir;
/// toplam satırı plaka sanılmasın diye "Özet" sayfasındadır.
/// </summary>
public static class ToplamExcelUretici
{
    private const string SayiBicimi = "#,##0.00";

    /// <param name="bilgiler">Özet sayfasının başına yazılacak bilgiler (paket, dönem, ortam...).</param>
    public static byte[] Uret(IReadOnlyList<EsuSatiri> satirlar, IEnumerable<(string Ad, string Deger)> bilgiler)
    {
        using var kitap = new XLWorkbook();
        var aracVar = satirlar.Any(s => s.AracKimlikNo != null);
        var roamingVar = satirlar.Any(s => s.RoamingVkn != null);

        var sayfa = kitap.AddWorksheet("Rapor");
        var basliklar = new List<string> { "Plaka", "kWh", "Tutar (TL)" };
        if (aracVar) basliklar.Add("Araç kimlik no");
        if (roamingVar) { basliklar.Add("Roaming VKN"); basliklar.Add("Roaming unvan"); }
        for (var i = 0; i < basliklar.Count; i++)
            sayfa.Cell(1, i + 1).Value = basliklar[i];

        var r = 2;
        foreach (var s in satirlar)
        {
            // Plaka metin olarak yazılır: "31E0025" gibi plakalar bilimsel sayıya dönmesin.
            sayfa.Cell(r, 1).Style.NumberFormat.Format = "@";
            sayfa.Cell(r, 1).SetValue(s.Plaka);
            sayfa.Cell(r, 2).Value = s.Kwh;
            sayfa.Cell(r, 3).Value = s.Tutar;
            var c = 4;
            if (aracVar) sayfa.Cell(r, c++).SetValue(s.AracKimlikNo ?? "");
            if (roamingVar)
            {
                sayfa.Cell(r, c++).SetValue(s.RoamingVkn ?? "");
                sayfa.Cell(r, c).SetValue(s.RoamingUnvan ?? "");
            }
            r++;
        }
        sayfa.Range(2, 2, Math.Max(2, r - 1), 3).Style.NumberFormat.Format = SayiBicimi;
        sayfa.Row(1).Style.Font.Bold = true;
        sayfa.SheetView.FreezeRows(1);
        if (satirlar.Count > 0)
            sayfa.Range(1, 1, r - 1, basliklar.Count).SetAutoFilter();
        sayfa.Columns(1, basliklar.Count).Width = 18;

        var ozet = kitap.AddWorksheet("Özet");
        var satir = 1;
        foreach (var (ad, deger) in bilgiler)
        {
            ozet.Cell(satir, 1).Value = ad;
            ozet.Cell(satir, 2).SetValue(deger);
            satir++;
        }
        ozet.Cell(satir, 1).Value = "Plaka sayısı";
        ozet.Cell(satir++, 2).Value = satirlar.Count;
        ozet.Cell(satir, 1).Value = "Toplam kWh";
        ozet.Cell(satir, 2).Value = satirlar.Sum(s => s.Kwh);
        ozet.Cell(satir++, 2).Style.NumberFormat.Format = SayiBicimi;
        ozet.Cell(satir, 1).Value = "Toplam tutar (TL)";
        ozet.Cell(satir, 2).Value = satirlar.Sum(s => s.Tutar);
        ozet.Cell(satir, 2).Style.NumberFormat.Format = SayiBicimi;
        ozet.Column(1).Style.Font.Bold = true;
        ozet.Column(1).Width = 20;
        ozet.Column(2).Width = 42;

        sayfa.SetTabActive();
        using var ms = new MemoryStream();
        kitap.SaveAs(ms);
        return ms.ToArray();
    }
}
