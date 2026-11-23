using System.Collections.Generic;

namespace ChocoLog.Web.Models
{
    // assinatura do Orion (é ela que manda cada medida para o STH-Comet)
    public class AssinaturaViewModel
    {
        public string Id { get; set; }
        public string Descricao { get; set; }
        public string Situacao { get; set; }
        public string Url { get; set; }
        public List<string> Atributos { get; set; } = new List<string>();
        public string UltimaNotificacao { get; set; }
    }
}
