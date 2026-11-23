namespace ChocoLog.Web.Models
{
    // endereço do FIWARE. O IP público do Learner Lab muda a cada início, por isso dá para trocar pela tela
    public class ConfiguracaoViewModel
    {
        public string Host { get; set; } = "localhost";
        public int OrionPort { get; set; } = 1026;
        public int IotAgentPort { get; set; } = 4041;
        public int SthPort { get; set; } = 8666;
        public string Service { get; set; } = "chocolog";
        public string ServicePath { get; set; } = "/";
        public string ApiKey { get; set; } = "chocolog";
        public int DispositivoMonitorado { get; set; } = 1;
    }
}
