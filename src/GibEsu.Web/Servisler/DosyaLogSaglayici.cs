using System.Text;

namespace GibEsu.Web.Servisler;

/// <summary>
/// Günlük log dosyası (veri klasörü\logs). Plaka ve tutar gibi rapor içerikleri loglanmaz;
/// yalnızca işlem adımları, paket ID'leri ve hatalar yazılır. GİB ile alışverişin tamamı
/// (rapor içeriği dahil) ayrıca loglar\soap altında tutulur (bkz. SoapGunlugu).
/// </summary>
public sealed class DosyaLogSaglayici : ILoggerProvider
{
    private readonly string _klasor;
    private readonly object _kilit = new();

    public DosyaLogSaglayici(string klasor)
    {
        _klasor = klasor;
        Directory.CreateDirectory(klasor);
    }

    public ILogger CreateLogger(string kategori) => new Kaydedici(this, kategori);

    public void Dispose()
    {
    }

    private void Yaz(string satir)
    {
        lock (_kilit)
        {
            try
            {
                Directory.CreateDirectory(_klasor);
                var dosya = Path.Combine(_klasor, $"gibesu-{DateTime.Now:yyyyMMdd}.log");
                using var akis = new FileStream(dosya, FileMode.Append, FileAccess.Write, FileShare.ReadWrite);
                using var yazici = new StreamWriter(akis, new UTF8Encoding(false));
                yazici.WriteLine(satir);
            }
            catch (IOException)
            {
                // Log yazılamaması işlemi bozmamalı.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private sealed class Kaydedici : ILogger
    {
        private readonly DosyaLogSaglayici _saglayici;
        private readonly string _kategori;

        public Kaydedici(DosyaLogSaglayici saglayici, string kategori)
        {
            _saglayici = saglayici;
            _kategori = kategori;
        }

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel seviye) =>
            seviye >= LogLevel.Information && (seviye >= LogLevel.Warning || _kategori.StartsWith("GibEsu"));

        public void Log<TState>(LogLevel seviye, EventId olay, TState durum, Exception? hata, Func<TState, Exception?, string> bicim)
        {
            if (!IsEnabled(seviye))
                return;
            var satir = $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} [{seviye}] {_kategori}: {bicim(durum, hata)}";
            if (hata != null)
                satir += Environment.NewLine + hata;
            _saglayici.Yaz(satir);
        }
    }
}
