using System.IO;
using ChocoLog.Web.Models;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;

namespace ChocoLog.Web.DAO
{
    // equivalente ao ConexaoBD: guarda onde o FIWARE está.
    // O IP do Learner Lab muda a cada início, então a tela Configuração grava as mudanças em App_Data/fiware.json
    public static class ConexaoFiware
    {
        private static readonly object trava = new object();
        private static string arquivo;
        private static ConfiguracaoViewModel atual = new ConfiguracaoViewModel();

        public static void Carrega(IConfiguration config, string pastaProjeto)
        {
            arquivo = Path.Combine(pastaProjeto, "App_Data", "fiware.json");
            var doAppSettings = config.GetSection("Fiware").Get<ConfiguracaoViewModel>();
            if (doAppSettings != null)
                atual = doAppSettings;

            // o que foi salvo pela tela Configuração tem prioridade
            if (File.Exists(arquivo))
            {
                try
                {
                    var salvo = JsonConvert.DeserializeObject<ConfiguracaoViewModel>(File.ReadAllText(arquivo));
                    if (salvo != null)
                        atual = salvo;
                }
                catch (JsonException)
                {
                    // arquivo corrompido: segue com o appsettings.json
                }
            }
        }

        public static ConfiguracaoViewModel Atual
        {
            get { lock (trava) return atual; }
        }

        public static void Salva(ConfiguracaoViewModel config)
        {
            lock (trava)
            {
                atual = config;
                Directory.CreateDirectory(Path.GetDirectoryName(arquivo));
                File.WriteAllText(arquivo, JsonConvert.SerializeObject(config, Formatting.Indented));
            }
        }

        public static string UrlOrion => $"http://{Atual.Host}:{Atual.OrionPort}";
        public static string UrlIotAgent => $"http://{Atual.Host}:{Atual.IotAgentPort}";
        public static string UrlSth => $"http://{Atual.Host}:{Atual.SthPort}";

        // device e entidade acompanhados pelo dashboard e pelo monitor de alertas
        public static string EntidadeMonitorada => "urn:ngsi-ld:ChocoLog:" + Atual.DispositivoMonitorado.ToString("000");
        public const string TipoDispositivo = "ChocoLog";
    }
}
