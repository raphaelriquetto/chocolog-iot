using System.Collections.Generic;
using System.Linq;
using System.Net;
using ChocoLog.Web.Models;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // DAO genérico para entidades do Orion. No lugar das stored procedures, cada método chama a API NGSI-v2:
    //   Insert -> POST   /v2/entities
    //   Update -> PATCH  /v2/entities/{id}/attrs
    //   Delete -> DELETE /v2/entities/{id}
    //   Consulta / Listagem -> GET /v2/entities?options=keyValues
    public abstract class PadraoDAO<T> where T : PadraoViewModel
    {
        public PadraoDAO()
        {
            SetTipoEntidade();
        }

        protected string TipoEntidade { get; set; }
        protected abstract JObject CriaAtributos(T model);
        protected abstract T MontaModel(JObject registro);
        protected abstract void SetTipoEntidade();

        // o Id numérico vira o ID da entidade: 1 -> urn:ngsi-ld:Carga:001
        public string IdEntidade(int id) => "urn:ngsi-ld:" + TipoEntidade + ":" + id.ToString("000");

        public static int IdNumerico(string idEntidade)
        {
            int id;
            if (string.IsNullOrEmpty(idEntidade))
                return 0;
            int.TryParse(idEntidade.Substring(idEntidade.LastIndexOf(':') + 1), out id);
            return id;
        }

        public virtual void Insert(T model)
        {
            var corpo = CriaAtributos(model);
            corpo["id"] = IdEntidade(model.Id);
            corpo["type"] = TipoEntidade;
            try
            {
                HelperFiware.Orion("POST", "/v2/entities", corpo);
            }
            catch (FiwareException erro) when (erro.Status == (HttpStatusCode)422)
            {
                throw new FiwareException("Já existe um cadastro com o código " + model.Id + ".", erro.Status);
            }
        }

        public virtual void Update(T model)
        {
            HelperFiware.Orion("PATCH", "/v2/entities/" + IdEntidade(model.Id) + "/attrs", CriaAtributos(model));
        }

        public virtual void Delete(int id)
        {
            HelperFiware.Orion("DELETE", "/v2/entities/" + IdEntidade(id));
        }

        public virtual T Consulta(int id)
        {
            try
            {
                var registro = HelperFiware.Orion("GET", "/v2/entities/" + IdEntidade(id) + "?options=keyValues");
                return MontaModel((JObject)registro);
            }
            catch (FiwareException erro) when (erro.NaoEncontrado)
            {
                return null;
            }
        }

        public virtual List<T> Listagem()
        {
            var registros = HelperFiware.Orion("GET", "/v2/entities?type=" + TipoEntidade + "&options=keyValues&limit=1000");
            return registros.Select(r => MontaModel((JObject)r)).OrderBy(m => m.Id).ToList();
        }

        public virtual int ProximoId()
        {
            var lista = Listagem();
            return lista.Count == 0 ? 1 : lista.Max(m => m.Id) + 1;
        }
    }
}
