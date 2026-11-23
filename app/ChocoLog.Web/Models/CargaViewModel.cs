using System;

namespace ChocoLog.Web.Models
{
    // carga de chocolate em transporte (entidade do tipo Carga no Orion)
    public class CargaViewModel : PadraoViewModel
    {
        public string Produto { get; set; }
        public int Caixas { get; set; }
        public string Origem { get; set; }
        public string Destino { get; set; }
        public DateTime DataSaida { get; set; } = DateTime.Today;
        public string Situacao { get; set; }
        public int DispositivoId { get; set; }

        // usado só na listagem
        public string DescricaoDispositivo { get; set; }

        public static readonly string[] ListaSituacoes = { "Aguardando coleta", "Em trânsito", "Entregue", "Avariada" };
    }
}
