using System;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers
{
    // limites de conservação: um único registro (entidade ChocoConfig no Orion).
    // O monitor relê a cada ciclo, então a mudança vale na hora, sem reiniciar nada
    public class LimiteController : Controller
    {
        public IActionResult Index()
        {
            try
            {
                return View("Form", new LimiteDAO().Consulta());
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
                return View("Form", new LimiteViewModel());
            }
            catch (Exception erro)
            {
                return View("Error", new ErrorViewModel(erro.ToString()));
            }
        }

        [HttpPost]
        public IActionResult Save(LimiteViewModel model)
        {
            try
            {
                ValidaDados(model);
                if (ModelState.IsValid == false)
                    return View("Form", model);

                new LimiteDAO().Salva(model);
                TempData["Mensagem"] = "Limites salvos! O monitor usa os novos valores no próximo ciclo (até 10 s).";
                return RedirectToAction("Index");
            }
            catch (FiwareException erro)
            {
                ModelState.AddModelError(string.Empty, erro.Message);
                return View("Form", model);
            }
            catch (Exception erro)
            {
                return View("Error", new ErrorViewModel(erro.ToString()));
            }
        }

        // apaga a entidade: voltam a valer os valores do chocolog.ino
        public IActionResult Restaurar()
        {
            try
            {
                new LimiteDAO().Apaga();
                TempData["Mensagem"] = "Limites restaurados para os valores do chocolog.ino.";
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
            }
            return RedirectToAction("Index");
        }

        private void ValidaDados(LimiteViewModel model)
        {
            ModelState.Clear();
            if (model.TempMin < -10 || model.TempMax > 60)
                ModelState.AddModelError("TempMin", "Temperatura entre -10 e 60 °C.");
            if (model.TempMin >= model.TempMax)
                ModelState.AddModelError("TempMax", "A mínima precisa ser menor que a máxima.");
            if (model.UmidMin < 0 || model.UmidMax > 100)
                ModelState.AddModelError("UmidMin", "Umidade entre 0 e 100 %.");
            if (model.UmidMin >= model.UmidMax)
                ModelState.AddModelError("UmidMax", "A mínima precisa ser menor que a máxima.");
            if (model.LuzMax < 0 || model.LuzMax > 100)
                ModelState.AddModelError("LuzMax", "Luminosidade entre 0 e 100 %.");
            if (model.Janela < 3 || model.Janela > 100)
                ModelState.AddModelError("Janela", "A janela deve ter entre 3 e 100 medidas.");
        }
    }
}
