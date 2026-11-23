using System;
using System.Collections.Generic;
using System.Linq;
using ChocoLog.Web.Models;

namespace ChocoLog.Web.Services
{
    // regra dos alertas, sem acesso a rede: recebe as médias e os limites e devolve qual alerta o ESP32 deve tocar.
    // Com mais de uma grandeza fora do limite vale a prioridade: temperatura, umidade, luminosidade
    // (o chocolate derrete com o calor; umidade e luz estragam mais devagar).
    public static class RegraAlertas
    {
        // valores do comando "alert" que o firmware entende
        public const string Normal = "OK";
        public const string Temperatura = "T";
        public const string Umidade = "U";
        public const string Luminosidade = "L";

        public static string Descricao(string alerta)
        {
            switch (alerta)
            {
                case Temperatura: return "Temperatura fora do limite";
                case Umidade: return "Umidade fora do limite";
                case Luminosidade: return "Luz acima do limite";
                default: return "Carga OK";
            }
        }

        // média com arredondamento comum (21,25 -> 21,3); o padrão do .NET arredondaria para o par (21,2)
        public static double? Media(IEnumerable<LeituraViewModel> leituras)
        {
            var valores = leituras.Where(l => l.ValorNumerico.HasValue).Select(l => l.ValorNumerico.Value).ToList();
            if (valores.Count == 0)
                return null;
            return Math.Round(valores.Average(), 1, MidpointRounding.AwayFromZero);
        }

        public static string Decide(MediasViewModel medias, LimiteViewModel limites, List<string> motivos)
        {
            bool temperaturaFora = medias.Temperatura.HasValue &&
                (medias.Temperatura < limites.TempMin || medias.Temperatura > limites.TempMax);
            bool umidadeFora = medias.Umidade.HasValue &&
                (medias.Umidade < limites.UmidMin || medias.Umidade > limites.UmidMax);
            bool luzFora = medias.Luminosidade.HasValue && medias.Luminosidade > limites.LuzMax;

            if (temperaturaFora)
                motivos.Add($"temperatura média {medias.Temperatura:0.0} °C fora de {limites.TempMin:0.#} a {limites.TempMax:0.#} °C");
            if (umidadeFora)
                motivos.Add($"umidade média {medias.Umidade:0} % fora de {limites.UmidMin:0} a {limites.UmidMax:0} %");
            if (luzFora)
                motivos.Add($"luminosidade média {medias.Luminosidade:0} % acima de {limites.LuzMax:0} %");

            // a primeira que bater é a de maior prioridade
            if (temperaturaFora) return Temperatura;
            if (umidadeFora) return Umidade;
            if (luzFora) return Luminosidade;
            return Normal;
        }
    }
}
