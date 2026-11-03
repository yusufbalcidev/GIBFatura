using System.Xml.Linq;
using Microsoft.AspNetCore.DataProtection.Repositories;

namespace GibEsu.Web.Servisler;

/// <summary>
/// Form güvenlik anahtarlarını yalnızca bellekte tutar. Varsayılan depo
/// (%LOCALAPPDATA%\ASP.NET\DataProtection-Keys) bazı şirket bilgisayarlarında klasör listeleme
/// sorunları yüzünden hata verdiği için diske hiç dokunulmaz. Uygulama yeniden başlatılınca
/// açık sayfaların yenilenmesi yeterlidir.
/// </summary>
public sealed class BellekAnahtarDeposu : IXmlRepository
{
    private readonly List<XElement> _ogeler = new();
    private readonly object _kilit = new();

    public IReadOnlyCollection<XElement> GetAllElements()
    {
        lock (_kilit)
            return _ogeler.Select(o => new XElement(o)).ToList();
    }

    public void StoreElement(XElement element, string friendlyName)
    {
        lock (_kilit)
            _ogeler.Add(new XElement(element));
    }
}
