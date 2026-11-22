using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Xunit;

namespace ChocoLog.Tests;

// Regras dos alertas com os limites do chocolog.ino: 15 a 22 °C, 45 a 60 % de umidade, luz até 50 %
public class AlertEngineTests
{
    private static readonly Limits L = new();

    [Fact]
    public void TudoDentroDosLimites_ficaNormal()
    {
        var (alert, reasons) = AlertEngine.Decide(new Averages(18, 50, 20, 12), L);
        Assert.Equal(AlertKind.OK, alert);
        Assert.Empty(reasons);
    }

    [Theory]
    [InlineData(14.9, 50, 20, AlertKind.T)] // frio demais
    [InlineData(22.1, 50, 20, AlertKind.T)] // quente demais
    [InlineData(18, 44, 20, AlertKind.U)]   // seco demais
    [InlineData(18, 61, 20, AlertKind.U)]   // úmido demais
    [InlineData(18, 50, 51, AlertKind.L)]   // luz demais
    public void CadaGrandezaGeraSeuAlerta(double t, double h, double l, AlertKind esperado) =>
        Assert.Equal(esperado, AlertEngine.Decide(new Averages(t, h, l, 12), L).Alert);

    [Fact]
    public void LimiteExato_naoDispara()
    {
        Assert.Equal(AlertKind.OK, AlertEngine.Decide(new Averages(15, 45, 50, 12), L).Alert);
        Assert.Equal(AlertKind.OK, AlertEngine.Decide(new Averages(22, 60, 50, 12), L).Alert);
    }

    [Fact]
    public void VariasAnomalias_valeAPrioridade_temperaturaUmidadeLuz()
    {
        var (alert, reasons) = AlertEngine.Decide(new Averages(30, 80, 90, 12), L);
        Assert.Equal(AlertKind.T, alert);
        Assert.Equal(3, reasons.Count);

        Assert.Equal(AlertKind.U, AlertEngine.Decide(new Averages(18, 80, 90, 12), L).Alert);
    }

    [Fact]
    public void SemMedidasDeUmaGrandeza_ignoraEla()
    {
        Assert.Equal(AlertKind.L, AlertEngine.Decide(new Averages(null, null, 70, 12), L).Alert);
    }

    [Fact]
    public void Media_ignoraValoresNaoNumericos()
    {
        var readings = new[]
        {
            new Reading("temperature", DateTime.UtcNow, "20"),
            new Reading("temperature", DateTime.UtcNow, "22.5"),
            new Reading("temperature", DateTime.UtcNow, ""), // notificação sem valor
        };
        Assert.Equal(21.3, AlertEngine.Average(readings)); // (20 + 22,5) / 2 = 21,25 -> 21,3
        Assert.Null(AlertEngine.Average([]));
    }

    [Fact]
    public void Limites_validamMinimoMenorQueMaximo()
    {
        var invalid = new Limits { TempMin = 25, TempMax = 20 };
        var errors = invalid.Validate(new System.ComponentModel.DataAnnotations.ValidationContext(invalid)).ToList();
        Assert.Single(errors);
    }
}
