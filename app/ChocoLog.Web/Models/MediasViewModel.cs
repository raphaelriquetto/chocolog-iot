namespace ChocoLog.Web.Models
{
    // médias da janela usadas pelo monitor. Nulo = ainda sem medidas daquela grandeza
    public class MediasViewModel
    {
        public double? Temperatura { get; set; }
        public double? Umidade { get; set; }
        public double? Luminosidade { get; set; }
        public int Amostras { get; set; }
    }
}
