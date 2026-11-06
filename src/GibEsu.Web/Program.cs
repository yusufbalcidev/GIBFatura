using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Unicode;
using GibEsu.Core.Servis;
using GibEsu.Web;
using GibEsu.Web.Servisler;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.DataProtection.KeyManagement;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.WebEncoders;

Console.OutputEncoding = Encoding.UTF8;

var builder = WebApplication.CreateBuilder(new WebApplicationOptions
{
    Args = args,
    ContentRootPath = AppContext.BaseDirectory,
});

var ayarlar = builder.Configuration.GetSection("GibEsu").Get<Ayarlar>() ?? new Ayarlar();
var adres = $"http://127.0.0.1:{ayarlar.Port}/";

// Bazı şirket bilgisayarlarında güvenlik yazılımı AppData altına yazmayı sessizce engelleyebiliyor.
// Veri klasörüne gerçekten yazılıp okunabildiğini dene; olmazsa .exe'nin yanındaki "veri" klasörüne geç.
var istenenKlasor = ayarlar.VeriKlasoruYolu;
if (!YazilabilirMi(istenenKlasor))
{
    var yedekKlasor = Path.Combine(AppContext.BaseDirectory, "veri");
    var uyari = YazilabilirMi(yedekKlasor)
        ? $"{istenenKlasor} klasörüne yazılamadı; veriler {yedekKlasor} klasöründe tutuluyor."
        : $"{istenenKlasor} ve {yedekKlasor} klasörlerine yazılamadı. Gönderim geçmişi ve loglar kaydedilemeyebilir.";
    Console.WriteLine("  UYARI: " + uyari);
    builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
    {
        ["GibEsu:VeriKlasoru"] = yedekKlasor,
        ["GibEsu:VeriKlasoruUyarisi"] = uyari,
    });
    ayarlar.VeriKlasoru = yedekKlasor;
}

// Loglar varsayılan olarak .exe'nin yanındaki "loglar" klasörüne yazılır; yazılamazsa veri klasörüne.
var logKlasoru = ayarlar.LogKlasoruYolu;
if (!YazilabilirMi(logKlasoru))
{
    var yedekLog = Path.Combine(ayarlar.VeriKlasoruYolu, "logs");
    Console.WriteLine($"  UYARI: {logKlasoru} klasörüne yazılamadı; loglar {yedekLog} klasörüne yazılıyor.");
    logKlasoru = yedekLog;
}
builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?> { ["GibEsu:LogKlasoru"] = logKlasoru });

GibEsuIstemcisi.CanliKilitli = !ayarlar.CanliAcik;
EsuGonderimServisi.TestKisaDosyaAdi = ayarlar.TestKisaDosyaAdi;
if (ayarlar.TestKisaDosyaAdi)
    Console.WriteLine("  DENEME: TestKisaDosyaAdi açık – test gönderimlerinde kısa dosya adı kullanılıyor (canlı etkilenmez).");
try
{
    GibEsuIstemcisi.AdresleriAyarla(ayarlar.CanliAdres, ayarlar.TestAdres);
}
catch (ArgumentException ex)
{
    Console.WriteLine($"  UYARI: appsettings.json: {ex.Message} Varsayılan GİB adresleri kullanılıyor.");
}

// Aynı veri klasörünü (geçmiş, anahtarlar, log) aynı anda tek kopya kullanır.
var kilitAdi = @"Local\GibEsuGonderim-" + Convert.ToHexString(
    System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(ayarlar.VeriKlasoruYolu.ToLowerInvariant())))[..16];
using var tekKopya = new Mutex(initiallyOwned: true, kilitAdi, out var ilkKopya);

// Uygulama zaten açıksa yalnızca tarayıcıyı aç.
if (!ilkKopya || !PortBosMu(ayarlar.Port))
{
    Console.WriteLine($"Uygulama zaten çalışıyor: {adres}");
    TarayiciAc(adres);
    return;
}

builder.WebHost.ConfigureKestrel(k =>
{
    k.Listen(IPAddress.Loopback, ayarlar.Port);
    k.Limits.MaxRequestBodySize = GibEsu.Web.Controllers.HomeController.EnBuyukYukleme + 1024 * 1024;
});
builder.Services.Configure<FormOptions>(o => o.MultipartBodyLengthLimit = GibEsu.Web.Controllers.HomeController.EnBuyukYukleme);

builder.Services.Configure<Ayarlar>(builder.Configuration.GetSection("GibEsu"));
builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o => o.SingleLine = true);
builder.Logging.AddProvider(new DosyaLogSaglayici(logKlasoru));

builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.Configure<WebEncoderOptions>(o => o.TextEncoderSettings = new TextEncoderSettings(UnicodeRanges.All));
// Form güvenlik anahtarları yalnızca bellekte tutulur (BellekAnahtarDeposu); varsayılan disk deposu hiç okunmaz.
builder.Services.AddDataProtection().SetApplicationName("GibEsuGonderim");
builder.Services.Configure<KeyManagementOptions>(o => o.XmlRepository = new BellekAnahtarDeposu());
// Bellekteki anahtar şifrelenmediği için verilen "XML encryptor yok" uyarısı burada anlamsızdır.
builder.Logging.AddFilter("Microsoft.AspNetCore.DataProtection", LogLevel.Error);
// Anahtarlar her açılışta yenilendiği için tarayıcıda kalan eski form çerezi çözülemez ve yenisi verilir.
// Bu beklenen bir durumdur; "fail" kaydı kullanıcıyı gereksiz yere korkutuyordu.
builder.Logging.AddFilter("Microsoft.AspNetCore.Antiforgery", LogLevel.Critical);
builder.Services.AddMemoryCache();
builder.Services.AddSingleton<GecmisDeposu>();
builder.Services.AddSingleton<KartServisi>();
builder.Services.AddSingleton(_ => GibEsuIstemcisi.HttpIstemcisiOlustur());
builder.Services.AddSingleton<ISoapGunlugu, SoapGunlugu>();
builder.Services.AddSingleton<GibEsuIstemcisi>();
builder.Services.AddSingleton<EsuGonderimServisi>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
    app.UseExceptionHandler("/Home/Error");

app.Use(async (baglam, sonraki) =>
{
    baglam.Response.Headers["X-Frame-Options"] = "DENY";
    baglam.Response.Headers["X-Content-Type-Options"] = "nosniff";
    await sonraki();
});
app.UseStatusCodePagesWithReExecute("/Home/HttpDurumu", "?kod={0}");
app.UseRouting();
app.MapControllerRoute(name: "default", pattern: "{controller=Home}/{action=Index}/{id?}");

await app.StartAsync();
// Log dosyası açılışta oluşsun; ne zaman, hangi sürüm ve hangi ortamla çalıştığı da kayıtta görünsün.
app.Services.GetRequiredService<ILoggerFactory>().CreateLogger("GibEsu.Baslangic").LogInformation(
    "Uygulama başlatıldı. Sürüm {Surum}, {Ortam}, veri klasörü {Veri}",
    typeof(Ayarlar).Assembly.GetName().Version?.ToString(3),
    ayarlar.CanliAcik ? "canlı AÇIK" : "yalnızca test",
    ayarlar.VeriKlasoruYolu);
Console.WriteLine();
Console.WriteLine("  GİB ESÜ Rapor Gönderimi çalışıyor.");
Console.WriteLine($"  Adres: {adres}");
Console.WriteLine($"  Veri klasörü: {ayarlar.VeriKlasoruYolu}");
Console.WriteLine($"  Log klasörü: {logKlasoru}");
Console.WriteLine(ayarlar.CanliAcik
    ? "  GİB ortamı: CANLI AÇIK (gerçek bildirim yapılabilir)"
    : "  GİB ortamı: yalnızca TEST (canlı kapalı)");
Console.WriteLine("  Kapatmak için bu pencereyi kapatın.");
Console.WriteLine();
if (ayarlar.TarayiciAc)
    TarayiciAc(adres);
await app.WaitForShutdownAsync();

static bool YazilabilirMi(string klasor)
{
    try
    {
        Directory.CreateDirectory(klasor);
        var deneme = Path.Combine(klasor, $".yazma-denemesi-{Guid.NewGuid():N}");
        File.WriteAllText(deneme, "ok");
        var okunan = File.ReadAllText(deneme);
        File.Delete(deneme);
        return okunan == "ok" && Directory.Exists(klasor);
    }
    catch (Exception)
    {
        return false;
    }
}

static bool PortBosMu(int port)
{
    try
    {
        var dinleyici = new TcpListener(IPAddress.Loopback, port);
        dinleyici.Start();
        dinleyici.Stop();
        return true;
    }
    catch (SocketException)
    {
        return false;
    }
}

static void TarayiciAc(string adres)
{
    try
    {
        Process.Start(new ProcessStartInfo(adres) { UseShellExecute = true });
    }
    catch (Exception)
    {
        // Tarayıcı açılamazsa kullanıcı adresi konsoldan görebilir.
    }
}
