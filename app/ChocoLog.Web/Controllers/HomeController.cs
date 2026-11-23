using System.Diagnostics;
using ChocoLog.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers
{
    public class HomeController : Controller
    {
        // dashboard: os números e os gráficos são carregados via AJAX pela API (Api/DashboardController)
        public IActionResult Index()
        {
            return View();
        }

        public IActionResult Error()
        {
            return View(new ErrorViewModel { RequestId = Activity.Current?.Id ?? HttpContext.TraceIdentifier });
        }
    }
}
