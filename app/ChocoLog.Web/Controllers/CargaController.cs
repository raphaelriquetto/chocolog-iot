using System;
using System.Collections.Generic;
using System.Linq;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace ChocoLog.Web.Controllers
{
    public class CargaController : PadraoController<CargaViewModel>
    {
        public CargaController()
        {
            DAO = new CargaDAO();
            GeraProximoId = true;
        }

        // a listagem mostra a descrição do device no lugar do número
        public override IActionResult Index()
        {
            var resultado = base.Index();
            if (resultado is ViewResult view && view.Model is List<CargaViewModel> lista && lista.Count > 0)
            {
                try
                {
                    var dispositivos = new DispositivoDAO().Listagem();
                    foreach (var carga in lista)
                    {
                        var d = dispositivos.FirstOrDefault(x => x.Id == carga.DispositivoId);
                        carga.DescricaoDispositivo = d == null ? "device " + carga.DispositivoId : d.DeviceId + " · " + d.Local;
                    }
                }
                catch (FiwareException)
                {
                    // sem o IoT Agent a tabela continua aparecendo, só sem a descrição
                }
            }
            return resultado;
        }

        protected override void PreencheDadosParaView(string Operacao, CargaViewModel model)
        {
            base.PreencheDadosParaView(Operacao, model);

            var listaSituacoes = new List<SelectListItem>();
            foreach (var situacao in CargaViewModel.ListaSituacoes)
                listaSituacoes.Add(new SelectListItem(situacao, situacao));
            ViewBag.Situacoes = listaSituacoes;

            var listaDispositivos = new List<SelectListItem> { new SelectListItem("Selecione um device...", "0") };
            foreach (var d in new DispositivoDAO().Listagem())
                listaDispositivos.Add(new SelectListItem(d.DeviceId + " · " + d.Local, d.Id.ToString()));
            ViewBag.Dispositivos = listaDispositivos;
        }

        protected override void ValidaDados(CargaViewModel model, string operacao)
        {
            base.ValidaDados(model, operacao);
            ValidaTextoFiware("Produto", model.Produto, "o produto", 60);
            ValidaTextoFiware("Origem", model.Origem, "a origem", 60);
            ValidaTextoFiware("Destino", model.Destino, "o destino", 60);

            if (model.Caixas <= 0 || model.Caixas > 100000)
                ModelState.AddModelError("Caixas", "Quantidade entre 1 e 100.000 caixas.");
            if (!string.IsNullOrWhiteSpace(model.Origem) &&
                string.Equals(model.Origem.Trim(), model.Destino?.Trim(), StringComparison.OrdinalIgnoreCase))
                ModelState.AddModelError("Destino", "A origem e o destino não podem ser iguais.");
            if (model.DataSaida.Year < 2020 || model.DataSaida > DateTime.Today.AddYears(1))
                ModelState.AddModelError("DataSaida", "Data de saída inválida.");
            if (!CargaViewModel.ListaSituacoes.Contains(model.Situacao))
                ModelState.AddModelError("Situacao", "Selecione a situação.");
            if (model.DispositivoId <= 0)
                ModelState.AddModelError("DispositivoId", "Selecione o device que acompanha a carga.");
        }
    }
}
