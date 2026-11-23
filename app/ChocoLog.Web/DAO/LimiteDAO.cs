using ChocoLog.Web.Models;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // limites de conservação: uma única entidade ChocoConfig no Orion
    public class LimiteDAO
    {
        public const string IdEntidade = "urn:ngsi-ld:ChocoConfig:001";

        // se ainda não foi salva, valem os valores do chocolog.ino
        public LimiteViewModel Consulta()
        {
            try
            {
                var r = HelperFiware.Orion("GET", "/v2/entities/" + IdEntidade + "?options=keyValues");
                return new LimiteViewModel
                {
                    TempMin = r["tempMin"].Value<double>(),
                    TempMax = r["tempMax"].Value<double>(),
                    UmidMin = r["humMin"].Value<double>(),
                    UmidMax = r["humMax"].Value<double>(),
                    LuzMax = r["lumMax"].Value<double>(),
                    Janela = r["window"].Value<int>(),
                    SalvoNoOrion = true
                };
            }
            catch (FiwareException erro) when (erro.NaoEncontrado)
            {
                return new LimiteViewModel();
            }
        }

        // POST /v2/entities?options=upsert : cria na primeira vez e atualiza nas seguintes
        public void Salva(LimiteViewModel model)
        {
            var corpo = new JObject
            {
                ["id"] = IdEntidade,
                ["type"] = "ChocoConfig",
                ["tempMin"] = HelperFiware.Atributo("Number", model.TempMin),
                ["tempMax"] = HelperFiware.Atributo("Number", model.TempMax),
                ["humMin"] = HelperFiware.Atributo("Number", model.UmidMin),
                ["humMax"] = HelperFiware.Atributo("Number", model.UmidMax),
                ["lumMax"] = HelperFiware.Atributo("Number", model.LuzMax),
                ["window"] = HelperFiware.Atributo("Number", model.Janela)
            };
            HelperFiware.Orion("POST", "/v2/entities?options=upsert", corpo);
        }

        public void Apaga()
        {
            HelperFiware.Orion("DELETE", "/v2/entities/" + IdEntidade);
        }
    }
}
