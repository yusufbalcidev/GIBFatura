using Microsoft.AspNetCore.Mvc;

namespace GibEsu.Web.Controllers;

/// <summary>Kullanım kılavuzu (yalnızca okuma sayfası).</summary>
public class KilavuzController : Controller
{
    [HttpGet]
    public IActionResult Index() => View();
}
