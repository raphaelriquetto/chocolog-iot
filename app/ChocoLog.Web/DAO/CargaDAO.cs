using System;
using System.Globalization;
using ChocoLog.Web.Models;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // cargas de chocolate: entidades do tipo "Carga" no Orion
    public class CargaDAO : PadraoDAO<CargaViewModel>
    {
        protected override JObject CriaAtributos(CargaViewModel model)
        {
            return new JObject
            {
                ["produto"] = HelperFiware.Atributo("Text", model.Produto.Trim()),
                ["caixas"] = HelperFiware.Atributo("Number", model.Caixas),
                ["origem"] = HelperFiware.Atributo("Text", model.Origem.Trim()),
                ["destino"] = HelperFiware.Atributo("Text", model.Destino.Trim()),
                ["dataSaida"] = HelperFiware.Atributo("DateTime", model.DataSaida.ToString("yyyy-MM-dd") + "T00:00:00.000Z"),
                ["situacao"] = HelperFiware.Atributo("Text", model.Situacao),
                // relacionamento: liga a carga à entidade do ChocoLog que mede a temperatura dela
                ["refChocoLog"] = HelperFiware.Atributo("Relationship", "urn:ngsi-ld:ChocoLog:" + model.DispositivoId.ToString("000"))
            };
        }

        protected override CargaViewModel MontaModel(JObject registro)
        {
            DateTime data;
            DateTime.TryParse((string)registro["dataSaida"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal, out data);
            return new CargaViewModel
            {
                Id = IdNumerico((string)registro["id"]),
                Produto = (string)registro["produto"],
                Caixas = registro["caixas"] == null ? 0 : registro["caixas"].Value<int>(),
                Origem = (string)registro["origem"],
                Destino = (string)registro["destino"],
                DataSaida = data == DateTime.MinValue ? DateTime.Today : data.Date,
                Situacao = (string)registro["situacao"],
                DispositivoId = IdNumerico((string)registro["refChocoLog"])
            };
        }

        protected override void SetTipoEntidade()
        {
            TipoEntidade = "Carga";
        }
    }
}
