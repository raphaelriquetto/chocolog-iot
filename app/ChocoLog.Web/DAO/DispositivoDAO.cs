using System.Collections.Generic;
using System.Linq;
using System.Net;
using ChocoLog.Web.Models;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // devices ChocoLog no IoT Agent. Sobrescreve o PadraoDAO porque o provisionamento é feito no IoT Agent (porta 4041):
    //   Insert -> POST /iot/devices          Update -> PUT /iot/devices/{id}
    //   Delete -> DELETE /iot/devices/{id}   Consulta / Listagem -> GET /iot/devices
    public class DispositivoDAO : PadraoDAO<DispositivoViewModel>
    {
        protected override void SetTipoEntidade()
        {
            TipoEntidade = ConexaoFiware.TipoDispositivo;
        }

        // atributos estáticos (descrição e local) ficam na entidade do Orion junto com as medidas
        protected override JObject CriaAtributos(DispositivoViewModel model)
        {
            return new JObject
            {
                ["static_attributes"] = new JArray(
                    new JObject { ["name"] = "descricao", ["type"] = "Text", ["value"] = model.Descricao.Trim() },
                    new JObject { ["name"] = "local", ["type"] = "Text", ["value"] = model.Local.Trim() })
            };
        }

        protected override DispositivoViewModel MontaModel(JObject registro)
        {
            var model = new DispositivoViewModel
            {
                Id = IdNumerico((string)registro["entity_name"]),
                Descricao = AtributoEstatico(registro, "descricao"),
                Local = AtributoEstatico(registro, "local"),
                Transporte = (string)registro["transport"]
            };
            if (registro["attributes"] != null)
                model.Atributos = registro["attributes"].Select(a => a["object_id"] + " → " + a["name"]).ToList();
            if (registro["commands"] != null)
                model.Comandos = registro["commands"].Select(c => (string)c["name"]).ToList();
            return model;
        }

        // nomes curtos (t, h, l, a) economizam bytes no MQTT; o IoT Agent traduz para os nomes completos no Orion
        public override void Insert(DispositivoViewModel model)
        {
            var device = CriaAtributos(model);
            device["device_id"] = model.DeviceId;
            device["apikey"] = ConexaoFiware.Atual.ApiKey;
            device["entity_name"] = model.EntityId;
            device["entity_type"] = TipoEntidade;
            device["protocol"] = "PDI-IoTA-UltraLight";
            device["transport"] = "MQTT";
            device["commands"] = new JArray(new JObject { ["name"] = "alert", ["type"] = "command" });
            device["attributes"] = new JArray(
                AtributoMqtt("t", "temperature", "Number"),
                AtributoMqtt("h", "humidity", "Number"),
                AtributoMqtt("l", "luminosity", "Number"),
                AtributoMqtt("a", "alertState", "Text"));
            try
            {
                HelperFiware.IotAgent("POST", "/iot/devices", new JObject { ["devices"] = new JArray(device) });
            }
            catch (FiwareException erro) when (erro.Status == HttpStatusCode.Conflict)
            {
                throw new FiwareException("Já existe um device com o código " + model.Id + ".", erro.Status);
            }
        }

        public override void Update(DispositivoViewModel model)
        {
            HelperFiware.IotAgent("PUT", "/iot/devices/" + model.DeviceId, CriaAtributos(model));
        }

        // remove o device do IoT Agent e a entidade dele no Orion
        public override void Delete(int id)
        {
            var model = new DispositivoViewModel { Id = id };
            HelperFiware.IotAgent("DELETE", "/iot/devices/" + model.DeviceId);
            try
            {
                HelperFiware.Orion("DELETE", "/v2/entities/" + model.EntityId);
            }
            catch (FiwareException erro) when (erro.NaoEncontrado)
            {
                // a entidade só existe depois da primeira medida
            }
        }

        public override DispositivoViewModel Consulta(int id)
        {
            try
            {
                var device = HelperFiware.IotAgent("GET", "/iot/devices/" + new DispositivoViewModel { Id = id }.DeviceId);
                return MontaModel((JObject)device);
            }
            catch (FiwareException erro) when (erro.NaoEncontrado)
            {
                return null;
            }
        }

        public override List<DispositivoViewModel> Listagem()
        {
            var resposta = HelperFiware.IotAgent("GET", "/iot/devices?limit=1000");
            return resposta["devices"]
                .Where(d => ((string)d["device_id"] ?? "").StartsWith("choco"))
                .Select(d => MontaModel((JObject)d))
                .OrderBy(d => d.Id)
                .ToList();
        }

        // comando ao ESP32: o Orion repassa ao IoT Agent, que publica no tópico MQTT /cmd
        // PATCH /v2/entities/{id}/attrs  { "alert": { "type": "command", "value": "T" } }
        public void EnviaComando(string entidade, string comando, string valor)
        {
            HelperFiware.Orion("PATCH", "/v2/entities/" + entidade + "/attrs",
                new JObject { [comando] = HelperFiware.Atributo("command", valor) });
        }

        // service group: cria a apikey usada nos tópicos MQTT (/chocolog/choco001/...)
        public bool ExisteServiceGroup()
        {
            var resposta = HelperFiware.IotAgent("GET", "/iot/services");
            return resposta["services"].Any(s => (string)s["apikey"] == ConexaoFiware.Atual.ApiKey);
        }

        public void CriaServiceGroup()
        {
            var grupo = new JObject
            {
                ["apikey"] = ConexaoFiware.Atual.ApiKey,
                ["resource"] = "/iot/d",
                ["entity_type"] = TipoEntidade,
                ["autoprovision"] = false
            };
            HelperFiware.IotAgent("POST", "/iot/services", new JObject { ["services"] = new JArray(grupo) });
        }

        private static string AtributoEstatico(JObject registro, string nome)
        {
            var atributo = registro["static_attributes"]?.FirstOrDefault(a => (string)a["name"] == nome);
            return atributo == null ? "" : (string)atributo["value"];
        }

        private static JObject AtributoMqtt(string objectId, string nome, string tipo) =>
            new JObject { ["object_id"] = objectId, ["name"] = nome, ["type"] = tipo };
    }
}
