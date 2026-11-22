using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

public class HomeController : Controller
{
    /// <summary>Dashboard: valores atuais e gráficos. Os dados chegam por Ajax (api/dashboard).</summary>
    public IActionResult Index() => View();

    /// <summary>Consulta das mensagens IoT. A tabela é preenchida por Ajax (api/mensagens).</summary>
    public IActionResult Mensagens() => View();

    public IActionResult Error() => View();
}
