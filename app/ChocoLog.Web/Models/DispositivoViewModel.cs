using System.Collections.Generic;

namespace ChocoLog.Web.Models
{
    // device ChocoLog provisionado no IoT Agent. O Id 1 vira o device "choco001" e a entidade urn:ngsi-ld:ChocoLog:001
    public class DispositivoViewModel : PadraoViewModel
    {
        public string Descricao { get; set; }
        public string Local { get; set; }

        // preenchidos pelo IoT Agent, só para exibição
        public string Transporte { get; set; }
        public List<string> Atributos { get; set; } = new List<string>();
        public List<string> Comandos { get; set; } = new List<string>();

        public string DeviceId => "choco" + Id.ToString("000");
        public string EntityId => "urn:ngsi-ld:ChocoLog:" + Id.ToString("000");
    }
}
