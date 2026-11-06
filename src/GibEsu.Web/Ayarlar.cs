using GibEsu.Core.Imza;
using GibEsu.Core.Servis;

// Mali mühür sürücüsü (akisp11.dll) ve DPAPI nedeniyle uygulama yalnızca Windows'ta çalışır.
[assembly: System.Runtime.Versioning.SupportedOSPlatform("windows")]

namespace GibEsu.Web;

public sealed class Ayarlar
{
    public int Port { get; set; } = 5080;
    public bool TarayiciAc { get; set; } = true;
    public GibOrtami VarsayilanOrtam { get; set; } = GibOrtami.Test;

    /// <summary>
    /// false iken canlı GİB'e hiçbir istek (gönderim, durum sorgusu, imza testi) yapılmaz.
    /// Denemeler bitip canlıya geçiş kararı verilince appsettings.json'da true yapılır.
    /// </summary>
    public bool CanliAcik { get; set; }

    public bool OrtamIzinli(GibOrtami ortam) => ortam == GibOrtami.Test || CanliAcik;

    /// <summary>Canlı kapalıyken ayardaki varsayılan Canli olsa bile Test seçili gelir.</summary>
    public GibOrtami EtkinVarsayilanOrtam => OrtamIzinli(VarsayilanOrtam) ? VarsayilanOrtam : GibOrtami.Test;

    public const string CanliKapaliMesaji =
        "Canlı ortam kapalı: şu an yalnızca test ortamına (okctest.gib.gov.tr) istek yapılabilir. "
        + "Canlıya geçiş kararı verilince appsettings.json dosyasında \"CanliAcik\": true yapılıp uygulama yeniden başlatılır.";
    /// <summary>GEÇİCİ DENEME: true iken yalnızca test gönderiminde kısa dosya adı kullanılır (bkz. EsuGonderimServisi).</summary>
    public bool TestKisaDosyaAdi { get; set; }

    public List<string> Pkcs11Kutuphaneleri { get; set; } = new();
    public string ImzaciRolu { get; set; } = XadesImzalayici.VarsayilanRol;

    /// <summary>Boşsa programdaki varsayılan GİB adresi kullanılır.</summary>
    public string CanliAdres { get; set; } = "";
    public string TestAdres { get; set; } = "";

    /// <summary>Mühürün bitişine bu kadar gün kala ana sayfada uyarı gösterilir.</summary>
    public int SertifikaUyariGunu { get; set; } = 60;

    /// <summary>Boşsa %LOCALAPPDATA%\GibEsuGonderim kullanılır.</summary>
    public string VeriKlasoru { get; set; } = "";

    /// <summary>Asıl veri klasörüne yazılamadıysa açılışta doldurulur.</summary>
    public string? VeriKlasoruUyarisi { get; set; }

    public IEnumerable<string> Kutuphaneler =>
        Pkcs11Kutuphaneleri.Count > 0 ? Pkcs11Kutuphaneleri : Pkcs11Imzaci.VarsayilanKutuphaneler;

    /// <summary>Boşsa .exe'nin yanındaki "loglar" klasörü kullanılır.</summary>
    public string LogKlasoru { get; set; } = "";

    public string LogKlasoruYolu => string.IsNullOrWhiteSpace(LogKlasoru)
        ? Path.Combine(AppContext.BaseDirectory, "loglar")
        : LogKlasoru;

    public string VeriKlasoruYolu => string.IsNullOrWhiteSpace(VeriKlasoru)
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GibEsuGonderim")
        : VeriKlasoru;
}
