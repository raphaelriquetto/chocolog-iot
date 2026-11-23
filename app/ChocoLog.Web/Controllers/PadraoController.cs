using System;
using System.Collections.Generic;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers
{
    // controller base de todos os CRUDs. Os dados ficam no FIWARE: quem fala com ele é o DAO
    public class PadraoController<T> : Controller where T : PadraoViewModel
    {
        protected PadraoDAO<T> DAO { get; set; }
        protected bool GeraProximoId { get; set; }
        protected string NomeViewIndex { get; set; } = "index";
        protected string NomeViewForm { get; set; } = "form";

        public virtual IActionResult Index()
        {
            try
            {
                var lista = DAO.Listagem();
                return View(NomeViewIndex, lista);
            }
            catch (FiwareException erro)
            {
                // FIWARE fora do ar: mostra o aviso e a lista vazia, em vez da página de erro
                TempData["Erro"] = erro.Message;
                return View(NomeViewIndex, new List<T>());
            }
            catch (Exception erro)
            {
                return View("Error", new ErrorViewModel(erro.ToString()));
            }
        }

        public virtual IActionResult Create()
        {
            try
            {
                ViewBag.Operacao = "I";
                T model = Activator.CreateInstance<T>();
                PreencheDadosParaView("I", model);
                return View(NomeViewForm, model);
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
                return RedirectToAction(NomeViewIndex);
            }
            catch (Exception erro)
            {
                return View("Error", new ErrorViewModel(erro.ToString()));
            }
        }

        protected virtual void PreencheDadosParaView(string Operacao, T model)
        {
            if (GeraProximoId && Operacao == "I")
                model.Id = DAO.ProximoId();
        }

        [HttpPost]
        public virtual IActionResult Save(T model, string Operacao)
        {
            try
            {
                ValidaDados(model, Operacao);
                if (ModelState.IsValid == false)
                {
                    ViewBag.Operacao = Operacao;
                    PreencheDadosParaView(Operacao, model);
                    return View(NomeViewForm, model);
                }
                else
                {
                    if (Operacao == "I")
                        DAO.Insert(model);
                    else
                        DAO.Update(model);
                    TempData["Mensagem"] = "Registro salvo com sucesso!";
                    return RedirectToAction(NomeViewIndex);
                }
            }
            catch (FiwareException erro)
            {
                // erro do FIWARE (ex.: código repetido): volta para o formulário com a mensagem
                ModelState.AddModelError(string.Empty, erro.Message);
                ViewBag.Operacao = Operacao;
                return View(NomeViewForm, model);
            }
            catch (Exception erro)
            {
                return View("Error", new ErrorViewModel(erro.ToString()));
            }
        }

        protected virtual void ValidaDados(T model, string operacao)
        {
            ModelState.Clear();
            if (model.Id <= 0)
                ModelState.AddModelError("Id", "Código inválido!");
            else if (operacao == "I" && DAO.Consulta(model.Id) != null)
                ModelState.AddModelError("Id", "Código já está em uso!");
            else if (operacao == "A" && DAO.Consulta(model.Id) == null)
                ModelState.AddModelError("Id", "Este registro não existe!");
        }

        // o Orion recusa estes caracteres nos textos: < > " ' = ; ( )
        protected void ValidaTextoFiware(string campo, string valor, string nome, int maximo)
        {
            if (string.IsNullOrWhiteSpace(valor))
                ModelState.AddModelError(campo, "Preencha " + nome + ".");
            else if (valor.Length > maximo)
                ModelState.AddModelError(campo, "Máximo de " + maximo + " caracteres.");
            else if (valor.IndexOfAny(new[] { '<', '>', '"', '\'', '=', ';', '(', ')' }) >= 0)
                ModelState.AddModelError(campo, "Não use os caracteres < > \" ' = ; ( ) (o FIWARE não aceita).");
        }

        public virtual IActionResult Edit(int id)
        {
            try
            {
                ViewBag.Operacao = "A";
                var model = DAO.Consulta(id);
                if (model == null)
                    return RedirectToAction(NomeViewIndex);
                else
                {
                    PreencheDadosParaView("A", model);
                    return View(NomeViewForm, model);
                }
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = erro.Message;
                return RedirectToAction(NomeViewIndex);
            }
            catch (Exception erro)
            {
                return View("Error", new ErrorViewModel(erro.ToString()));
            }
        }

        public virtual IActionResult Delete(int id)
        {
            try
            {
                DAO.Delete(id);
                TempData["Mensagem"] = "Registro excluído com sucesso!";
            }
            catch (FiwareException erro)
            {
                TempData["Erro"] = "Não foi possível excluir: " + erro.Message;
            }
            return RedirectToAction(NomeViewIndex);
        }
    }
}
