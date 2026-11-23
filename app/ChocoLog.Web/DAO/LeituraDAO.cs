using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using ChocoLog.Web.Models;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // medidas do ESP32: estado atual no Orion e histórico no STH-Comet
    public class LeituraDAO
    {
        public static readonly string[] Grandezas = { "temperature", "humidity", "luminosity" };

        // GET /v2/entities/{id}?attrs=...&options=keyValues
        public JObject EstadoAtual()
        {
            return (JObject)HelperFiware.Orion("GET", "/v2/entities/" + ConexaoFiware.EntidadeMonitorada +
                "?attrs=temperature,humidity,luminosity,alertState,alert_status,TimeInstant&options=keyValues");
        }

        // GET /STH/v1/contextEntities/type/{tipo}/id/{id}/attributes/{grandeza}
        // sem período: as N medidas mais recentes (lastN, até 100); com período: as do intervalo (até 100)
        public List<LeituraViewModel> Historico(string grandeza, int quantidade, DateTime? dataInicial = null, DateTime? dataFinal = null)
        {
            string filtro = dataInicial == null && dataFinal == null
                ? "lastN=" + Math.Min(quantidade, 100)
                : "hLimit=100&hOffset=0";
            if (dataInicial != null)
                filtro += "&dateFrom=" + dataInicial.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            if (dataFinal != null)
                filtro += "&dateTo=" + dataFinal.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);

            JToken resposta;
            try
            {
                resposta = HelperFiware.Sth("GET", "/STH/v1/contextEntities/type/" + ConexaoFiware.TipoDispositivo +
                    "/id/" + ConexaoFiware.EntidadeMonitorada + "/attributes/" + grandeza + "?" + filtro);
            }
            catch (FiwareException erro) when (erro.NaoEncontrado)
            {
                return new List<LeituraViewModel>(); // ainda sem histórico
            }

            var valores = resposta?["contextResponses"]?[0]?["contextElement"]?["attributes"]?[0]?["values"];
            if (valores == null)
                return new List<LeituraViewModel>();

            var lista = new List<LeituraViewModel>();
            foreach (var v in valores)
            {
                lista.Add(new LeituraViewModel
                {
                    Grandeza = grandeza,
                    Data = DateTime.Parse((string)v["recvTime"], CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal).ToLocalTime(),
                    // o STH devolve o valor como texto ou número, conforme a versão
                    Valor = v["attrValue"].Type == JTokenType.String
                        ? (string)v["attrValue"]
                        : v["attrValue"].Value<double>().ToString(CultureInfo.InvariantCulture)
                });
            }
            return lista;
        }
    }
}
