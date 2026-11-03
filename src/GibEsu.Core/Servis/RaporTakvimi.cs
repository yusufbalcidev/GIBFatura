namespace GibEsu.Core.Servis;

/// <summary>Aylık ESÜ raporunun takvimi: bir ayın raporu izleyen ayın 15'ine kadar gönderilir.</summary>
public static class RaporTakvimi
{
    public const int SonGun = 15;

    public static DateTime SonTarih(int yil, int ay) => new DateTime(yil, ay, SonGun).AddMonths(1);

    /// <summary>Bugün itibarıyla gönderilmesi gereken dönem (geçen ay).</summary>
    public static (int Yil, int Ay) BeklenenDonem(DateTime bugun)
    {
        var d = bugun.AddMonths(-1);
        return (d.Year, d.Month);
    }

    /// <summary>Son güne kalan gün; son gün bugünse 0, geçtiyse eksi.</summary>
    public static int KalanGun(int yil, int ay, DateTime bugun) => (SonTarih(yil, ay) - bugun.Date).Days;
}
