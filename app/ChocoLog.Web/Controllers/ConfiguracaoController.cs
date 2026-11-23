using System;
using System.Collections.Generic;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.Controllers
{
    // endereço do FIWARE, teste dos serviços, provisionamento em um clique e assinaturas do histórico
    public class ConfiguracaoController : Controller
    {
        public IActionResult Index()
        {
            CarregaProvisionamento();
            return View(ConexaoFiware.Atual);
        }

        [HttpPost]
        public IActionResult Save(ConfiguracaoViewModel model)
        {
            ValidaDados(model);
            if (ModelState.IsValid == false)
            {
                CarregaProvisionamento();
                return View("Index", model);
            }
            model.Host = model.Host.Trim();
            ConexaoFiware.Salva(model);
            TempData["Mensagem"] = "Configuração salva! Servidor: " + model.Host;
            return RedirectToAction("Index");
        }

        // AJAX: testa os três serviços e devolve o resultado para a mensagem rápida
        public IActionResult TestaConexao()
        {
            var resultados = new List<string>();
            bool tudoOk = true;
            void Testa(string nome, Func<string> versao)
            {
                try { resultados.Add(nome + " OK (" + versao() + ")"); }
                catch (FiwareException) { resultados.Add(nome + " sem resposta"); tudoOk = false; }
            }
            Testa("Orion", () => (string)HelperFiware.Orion("GET", "/version")["orion"]["version"]);
            Testa("IoT Agent", () => (string)HelperFiware.IotAgent("GET", "/iot/about")["version"]);
            Testa("STH-Comet", () => (string)HelperFiware.Sth("GET", "/version")["version"]);
            return Json(new { erro = !tudoOk, msg = string.Join(" · ", resultados) });
        }

        // cria o que falta, na ordem: service group, device, limites e assinatura do histórico.
        // Pode clicar mais de uma vez: o que já existe é pulado
        [HttpPost]
        public IActionResult Provisionar()
        {
            try
            {
                var criados = new List<string>();
                var dispositivoDAO = new DispositivoDAO();
                int id = ConexaoFiware.Atual.DispositivoMonitorado;

                if (!dispositivoDAO.ExisteServiceGroup())
                {
                    dispositivoDAO.CriaServiceGroup();
                    criados.Add("service group");
                }
                if (dispositivoDAO.Consulta(id) == null)
                {
                    dispositivoDAO.Insert(new DispositivoViewModel { Id = id, Descricao = "ChocoLog", Local = "Caminhão 1" });
                    criados.Add("device choco" + id.ToString("000"));
                }
                var limiteDAO = new LimiteDAO();
                if (!limiteDAO.Consulta().SalvoNoOrion)
                {
                    limiteDAO.Salva(new LimiteViewModel());
                    criados.Add("limites");
                }
                var assinaturaDAO = new AssinaturaDAO();
                if (!assinaturaDAO.ExisteHistorico())
                {
                    assinaturaDAO.CriaHistorico();
                    criados.Add("assinatura do histórico");
                }
                TempData["Mensagem"] = criados.Count == 0 ? "Tudo já estava provisionado." : "Criado: " + string.Join(", ", criados) + ".";
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
            }
            return RedirectToAction("Index");
        }

        public IActionResult CriaAssinatura()
        {
            try
            {
                new AssinaturaDAO().CriaHistorico();
                TempData["Mensagem"] = "Assinatura do STH-Comet criada.";
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
            }
            return RedirectToAction("Index");
        }

        public IActionResult DeleteAssinatura(string id)
        {
            try
            {
                new AssinaturaDAO().Delete(id);
                TempData["Mensagem"] = "Assinatura excluída. Sem ela, o histórico para de receber medidas.";
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
            }
            return RedirectToAction("Index");
        }

        // situação de cada etapa do provisionamento e lista de assinaturas
        private void CarregaProvisionamento()
        {
            var etapas = new List<KeyValuePair<string, bool>>();
            var assinaturas = new List<AssinaturaViewModel>();
            try
            {
                var dispositivoDAO = new DispositivoDAO();
                int id = ConexaoFiware.Atual.DispositivoMonitorado;
                var assinaturaDAO = new AssinaturaDAO();
                assinaturas = assinaturaDAO.Listagem();
                etapas.Add(new KeyValuePair<string, bool>("Service group (apikey " + ConexaoFiware.Atual.ApiKey + ")", dispositivoDAO.ExisteServiceGroup()));
                etapas.Add(new KeyValuePair<string, bool>("Device choco" + id.ToString("000"), dispositivoDAO.Consulta(id) != null));
                etapas.Add(new KeyValuePair<string, bool>("Limites de conservação", new LimiteDAO().Consulta().SalvoNoOrion));
                etapas.Add(new KeyValuePair<string, bool>("Assinatura do histórico (STH-Comet)", assinaturaDAO.ExisteHistorico()));
            }
            catch (FiwareException erro)
            {
                if (TempData["Erro"] == null)
                    TempData["Erro"] = erro.Message;
            }
            ViewBag.Etapas = etapas;
            ViewBag.Assinaturas = assinaturas;
        }

        private void ValidaDados(ConfiguracaoViewModel model)
        {
            ModelState.Clear();
            if (string.IsNullOrWhiteSpace(model.Host))
                ModelState.AddModelError("Host", "Informe o IP ou o nome do servidor.");
            else if (model.Host.Contains("http") || model.Host.Contains(":") || model.Host.Contains("/"))
                ModelState.AddModelError("Host", "Use só o IP, sem http:// e sem porta. Ex.: 54.123.45.67");
            foreach (var porta in new[] { model.OrionPort, model.IotAgentPort, model.SthPort })
                if (porta < 1 || porta > 65535)
                    ModelState.AddModelError("OrionPort", "Porta inválida.");
            if (string.IsNullOrWhiteSpace(model.Service))
                ModelState.AddModelError("Service", "Informe o fiware-service.");
            if (string.IsNullOrWhiteSpace(model.ServicePath) || !model.ServicePath.StartsWith("/"))
                ModelState.AddModelError("ServicePath", "O fiware-servicepath começa com /.");
            if (string.IsNullOrWhiteSpace(model.ApiKey))
                ModelState.AddModelError("ApiKey", "Informe a apikey.");
            if (model.DispositivoMonitorado < 1 || model.DispositivoMonitorado > 999)
                ModelState.AddModelError("DispositivoMonitorado", "Use um código de 1 a 999.");
        }
    }
}
