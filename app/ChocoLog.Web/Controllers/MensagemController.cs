using System;
using System.Collections.Generic;
using System.Linq;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ChocoLog.Web.Controllers
{
    // consulta das mensagens IoT (histórico do STH-Comet). O filtro é aplicado por AJAX e volta como partial view
    public class MensagemController : Controller
    {
        public IActionResult ConsultaMensagens()
        {
            ViewBag.ListaGrandezas = new List<SelectListItem>
            {
                new SelectListItem("Todas", "todas"),
                new SelectListItem("Temperatura", "temperature"),
                new SelectListItem("Umidade", "humidity"),
                new SelectListItem("Luminosidade", "luminosity"),
                new SelectListItem("Alerta confirmado pelo ESP32", "alertState")
            };
            return View();
        }

        // chamado pelo AJAX: devolve a tabela pronta (pvGridMensagens) ou um JSON com o erro
        public IActionResult ObtemDadosConsultaMensagens(string grandeza, int quantidade, DateTime? dataInicial, DateTime? dataFinal)
        {
            try
            {
                var permitidas = new List<string>(LeituraDAO.Grandezas) { "alertState" };
                var grandezas = grandeza == "todas" ? permitidas : permitidas.Where(g => g == grandeza).ToList();
                if (grandezas.Count == 0)
                    return Json(new { erro = true, msg = "Grandeza inválida." });
                if (dataInicial != null && dataFinal != null && dataInicial > dataFinal)
                    return Json(new { erro = true, msg = "A data inicial deve ser menor que a data final." });
                if (quantidade < 1 || quantidade > 100)
                    quantidade = 50;

                // o campo date do HTML vem sem hora: a data final vai até 23:59:59
                DateTime? fim = dataFinal?.Date.AddDays(1).AddSeconds(-1);

                var dao = new LeituraDAO();
                var lista = new List<LeituraViewModel>();
                foreach (var g in grandezas)
                    lista.AddRange(dao.Historico(g, quantidade, dataInicial, fim));

                lista = lista.OrderByDescending(l => l.Data).Take(quantidade * grandezas.Count).ToList();
                return PartialView("pvGridMensagens", lista);
            }
            catch (Exception erro)
            {
                return Json(new { erro = true, msg = erro.Message });
            }
        }
    }
}
