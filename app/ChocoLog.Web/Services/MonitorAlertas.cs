using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Models;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ChocoLog.Web.Services
{
    // último resultado do monitor, mostrado no dashboard
    public static class EstadoMonitor
    {
        private static readonly object trava = new object();

        public static MediasViewModel Medias { get; private set; }
        public static string Alerta { get; private set; } = RegraAlertas.Normal;
        public static List<string> Motivos { get; private set; } = new List<string>();
        public static DateTime? VerificadoEm { get; private set; }
        public static DateTime? ComandoEnviadoEm { get; private set; }
        public static string UltimoEnviado { get; private set; }
        public static string Erro { get; private set; }

        public static void Atualiza(MediasViewModel medias, string alerta, List<string> motivos)
        {
            lock (trava) { Medias = medias; Alerta = alerta; Motivos = motivos; VerificadoEm = DateTime.Now; Erro = null; }
        }

        public static void Enviado(string alerta)
        {
            lock (trava) { UltimoEnviado = alerta; ComandoEnviadoEm = DateTime.Now; }
        }

        public static void Falha(string erro)
        {
            lock (trava) { Erro = erro; VerificadoEm = DateTime.Now; }
        }
    }

    // supervisão: a cada Monitor:IntervaloSegundos busca as últimas N medidas no STH-Comet, calcula as médias,
    // compara com os limites e, se o alerta mudou, envia o comando "alert" ao ESP32 pelo Orion
    public class MonitorAlertas : BackgroundService
    {
        private static readonly TimeSpan ReenviaApos = TimeSpan.FromSeconds(30);
        private readonly int intervaloSegundos;
        private readonly ILogger<MonitorAlertas> log;

        public MonitorAlertas(IConfiguration config, ILogger<MonitorAlertas> log)
        {
            intervaloSegundos = config.GetValue("Monitor:IntervaloSegundos", 10);
            this.log = log;
        }

        protected override async Task ExecuteAsync(CancellationToken parar)
        {
            while (!parar.IsCancellationRequested)
            {
                try
                {
                    // as chamadas ao FIWARE são síncronas (como os DAOs), então rodam fora da thread do servidor
                    await Task.Run(Verifica, parar);
                }
                catch (FiwareException erro)
                {
                    EstadoMonitor.Falha(erro.Message);
                    log.LogWarning("Monitor: {Mensagem}", erro.Message);
                }
                catch (Exception erro) when (!(erro is OperationCanceledException))
                {
                    EstadoMonitor.Falha("Erro inesperado no monitor: " + erro.Message);
                    log.LogError(erro, "Monitor falhou");
                }

                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(intervaloSegundos), parar);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
            }
        }

        private void Verifica()
        {
            var limites = new LimiteDAO().Consulta();

            // janela da média: as últimas N medidas de cada grandeza no STH-Comet
            var leituraDAO = new LeituraDAO();
            var temperaturas = leituraDAO.Historico("temperature", limites.Janela);
            var umidades = leituraDAO.Historico("humidity", limites.Janela);
            var luzes = leituraDAO.Historico("luminosity", limites.Janela);

            var medias = new MediasViewModel
            {
                Temperatura = RegraAlertas.Media(temperaturas),
                Umidade = RegraAlertas.Media(umidades),
                Luminosidade = RegraAlertas.Media(luzes),
                Amostras = Math.Max(temperaturas.Count, Math.Max(umidades.Count, luzes.Count))
            };
            if (medias.Amostras == 0)
            {
                EstadoMonitor.Falha("Ainda não há medidas no histórico. O ESP32 está publicando e a assinatura do STH-Comet existe?");
                return;
            }

            var motivos = new List<string>();
            string alerta = RegraAlertas.Decide(medias, limites, motivos);
            EstadoMonitor.Atualiza(medias, alerta, motivos);

            // o que o ESP32 informou estar tocando (atributo alertState, vindo do "a" no MQTT)
            string confirmado = (string)leituraDAO.EstadoAtual()["alertState"];

            // envia quando a decisão muda, ou reenvia se o device não confirmou (por exemplo, se reiniciou)
            bool mudou = EstadoMonitor.UltimoEnviado != alerta;
            bool foraDeSincronia = confirmado != alerta &&
                (EstadoMonitor.ComandoEnviadoEm == null || DateTime.Now - EstadoMonitor.ComandoEnviadoEm > ReenviaApos);
            if (mudou || foraDeSincronia)
            {
                new DispositivoDAO().EnviaComando(ConexaoFiware.EntidadeMonitorada, "alert", alerta);
                EstadoMonitor.Enviado(alerta);
                log.LogInformation("Comando alert={Alerta} enviado ({Motivos})", alerta, string.Join("; ", motivos));
            }
        }
    }
}
