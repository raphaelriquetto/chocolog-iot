using System.Collections.Generic;
using System.Linq;
using ChocoLog.Web.Models;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // assinaturas do Orion: a do histórico avisa o STH-Comet a cada nova medida
    public class AssinaturaDAO
    {
        private static readonly string[] AtributosHistorico = { "temperature", "humidity", "luminosity", "alertState" };

        // GET /v2/subscriptions
        public List<AssinaturaViewModel> Listagem()
        {
            var lista = new List<AssinaturaViewModel>();
            foreach (var a in HelperFiware.Orion("GET", "/v2/subscriptions?limit=1000"))
            {
                var notificacao = a["notification"];
                lista.Add(new AssinaturaViewModel
                {
                    Id = (string)a["id"],
                    Descricao = (string)a["description"],
                    Situacao = (string)a["status"],
                    Url = (string)(notificacao?["httpCustom"]?["url"] ?? notificacao?["http"]?["url"]),
                    Atributos = a["subject"]?["condition"]?["attrs"]?.Select(x => (string)x).ToList() ?? new List<string>(),
                    UltimaNotificacao = (string)notificacao?["lastNotification"]
                });
            }
            return lista;
        }

        public bool ExisteHistorico()
        {
            return Listagem().Any(a => (a.Url ?? "").Contains("sth-comet") && a.Atributos.Contains("temperature"));
        }

        // POST /v2/subscriptions
        // O STH-Comet só entende o formato antigo (NGSIv1), então o corpo é montado com httpCustom,
        // igual à requisição 2.6 do FIWARE Made Simple. A URL é interna da rede Docker.
        public void CriaHistorico()
        {
            var atributos = new JArray(AtributosHistorico.Select(a => new JObject
            {
                ["name"] = a,
                ["type"] = a == "alertState" ? "Text" : "Number",
                ["value"] = "${" + a + "}"
            }));

            var assinatura = new JObject
            {
                ["description"] = "ChocoLog: historico no STH-Comet",
                ["subject"] = new JObject
                {
                    ["entities"] = new JArray(new JObject { ["id"] = ConexaoFiware.EntidadeMonitorada, ["type"] = ConexaoFiware.TipoDispositivo }),
                    ["condition"] = new JObject { ["attrs"] = new JArray(AtributosHistorico) }
                },
                ["notification"] = new JObject
                {
                    ["httpCustom"] = new JObject
                    {
                        ["url"] = "http://sth-comet:8666/notify",
                        ["json"] = new JObject
                        {
                            ["contextResponses"] = new JArray(new JObject
                            {
                                ["contextElement"] = new JObject
                                {
                                    ["id"] = "${id}",
                                    ["type"] = "${type}",
                                    ["isPattern"] = "false",
                                    ["attributes"] = atributos
                                },
                                ["statusCode"] = new JObject { ["code"] = "200", ["reasonPhrase"] = "OK" }
                            })
                        }
                    },
                    ["attrs"] = new JArray(AtributosHistorico)
                },
                ["throttling"] = 1
            };
            HelperFiware.Orion("POST", "/v2/subscriptions", assinatura);
        }

        // DELETE /v2/subscriptions/{id}
        public void Delete(string id)
        {
            HelperFiware.Orion("DELETE", "/v2/subscriptions/" + id);
        }
    }
}
