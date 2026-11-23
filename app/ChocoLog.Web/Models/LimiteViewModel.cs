namespace ChocoLog.Web.Models
{
    // limites de conservação do chocolate (entidade ChocoConfig no Orion). Valores iniciais iguais aos do chocolog.ino
    public class LimiteViewModel
    {
        public double TempMin { get; set; } = 15;
        public double TempMax { get; set; } = 22;
        public double UmidMin { get; set; } = 45;
        public double UmidMax { get; set; } = 60;
        public double LuzMax { get; set; } = 50;
        public int Janela { get; set; } = 12; // 12 medidas x 5 s = média do último minuto

        public bool SalvoNoOrion { get; set; }
    }
}
