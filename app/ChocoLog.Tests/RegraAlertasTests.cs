using System;
using System.Collections.Generic;
using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Xunit;

namespace ChocoLog.Tests
{
    // regras dos alertas com os limites do chocolog.ino: 15 a 22 °C, 45 a 60 % de umidade, luz até 50 %
    public class RegraAlertasTests
    {
        private static readonly LimiteViewModel Limites = new LimiteViewModel();

        private static string Decide(double? t, double? u, double? l, List<string> motivos = null) =>
            RegraAlertas.Decide(new MediasViewModel { Temperatura = t, Umidade = u, Luminosidade = l, Amostras = 12 },
                Limites, motivos ?? new List<string>());

        [Fact]
        public void TudoDentroDosLimites_ficaNormal()
        {
            var motivos = new List<string>();
            Assert.Equal("OK", Decide(18, 50, 20, motivos));
            Assert.Empty(motivos);
        }

        [Theory]
        [InlineData(14.9, 50, 20, "T")] // frio demais
        [InlineData(22.1, 50, 20, "T")] // quente demais
        [InlineData(18, 44, 20, "U")]   // seco demais
        [InlineData(18, 61, 20, "U")]   // úmido demais
        [InlineData(18, 50, 51, "L")]   // luz demais
        public void CadaGrandezaGeraSeuAlerta(double t, double u, double l, string esperado) =>
            Assert.Equal(esperado, Decide(t, u, l));

        [Fact]
        public void LimiteExato_naoDispara()
        {
            Assert.Equal("OK", Decide(15, 45, 50));
            Assert.Equal("OK", Decide(22, 60, 50));
        }

        [Fact]
        public void VariasAnomalias_valeAPrioridade_temperaturaUmidadeLuz()
        {
            var motivos = new List<string>();
            Assert.Equal("T", Decide(30, 80, 90, motivos));
            Assert.Equal(3, motivos.Count);
            Assert.Equal("U", Decide(18, 80, 90));
        }

        [Fact]
        public void SemMedidasDeUmaGrandeza_ignoraEla()
        {
            Assert.Equal("L", Decide(null, null, 70));
        }

        [Fact]
        public void Media_ignoraValoresNaoNumericos_eArredondaComoNaEscola()
        {
            var leituras = new[]
            {
                new LeituraViewModel { Grandeza = "temperature", Data = DateTime.Now, Valor = "20" },
                new LeituraViewModel { Grandeza = "temperature", Data = DateTime.Now, Valor = "22.5" },
                new LeituraViewModel { Grandeza = "temperature", Data = DateTime.Now, Valor = "" } // notificação sem valor
            };
            Assert.Equal(21.3, RegraAlertas.Media(leituras)); // (20 + 22,5) / 2 = 21,25 -> 21,3
            Assert.Null(RegraAlertas.Media(new LeituraViewModel[0]));
        }
    }
}
