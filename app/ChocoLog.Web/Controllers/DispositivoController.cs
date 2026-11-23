using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;

namespace ChocoLog.Web.Controllers
{
    public class DispositivoController : PadraoController<DispositivoViewModel>
    {
        public DispositivoController()
        {
            DAO = new DispositivoDAO();
            GeraProximoId = true;
        }

        protected override void ValidaDados(DispositivoViewModel model, string operacao)
        {
            base.ValidaDados(model, operacao);
            if (model.Id > 999)
                ModelState.AddModelError("Id", "Use um código de 1 a 999 (vira choco001 a choco999).");
            ValidaTextoFiware("Descricao", model.Descricao, "a descrição", 80);
            ValidaTextoFiware("Local", model.Local, "o local", 80);

            // sem o service group (apikey) o IoT Agent recusa o device
            if (ModelState.IsValid && operacao == "I" && !((DispositivoDAO)DAO).ExisteServiceGroup())
                ModelState.AddModelError(string.Empty, "Falta o service group (apikey). Use o botão Provisionar tudo em Configuração.");
        }
    }
}
