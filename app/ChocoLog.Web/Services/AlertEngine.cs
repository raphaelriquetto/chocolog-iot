using ChocoLog.Web.Models;

namespace ChocoLog.Web.Services;

/// <summary>Médias da janela. Nulo = ainda sem medidas daquela grandeza.</summary>
public record Averages(double? Temperature, double? Humidity, double? Luminosity, int Samples);

/// <summary>
/// Regra dos alertas, sem acesso a rede: recebe as médias e os limites e devolve qual alerta o ESP32 deve tocar.
/// Com mais de uma grandeza fora do limite, vale a prioridade: temperatura, depois umidade, depois luminosidade
/// (o chocolate derrete com calor; umidade e luz estragam mais devagar).
/// </summary>
public static class AlertEngine
{
    public static double? Average(IEnumerable<Reading> readings)
    {
        var values = readings.Select(r => r.Number).OfType<double>().ToList();
        // Arredondamento comum (21,25 -> 21,3); o padrão do .NET arredondaria para o par (21,2)
        return values.Count == 0 ? null : Math.Round(values.Average(), 1, MidpointRounding.AwayFromZero);
    }

    public static (AlertKind Alert, List<string> Reasons) Decide(Averages avg, Limits limits)
    {
        var reasons = new List<string>();
        if (avg.Temperature is double t && (t < limits.TempMin || t > limits.TempMax))
            reasons.Add($"temperatura média {t:0.0} °C fora de {limits.TempMin:0.#} a {limits.TempMax:0.#} °C");
        if (avg.Humidity is double h && (h < limits.HumMin || h > limits.HumMax))
            reasons.Add($"umidade média {h:0} % fora de {limits.HumMin:0} a {limits.HumMax:0} %");
        if (avg.Luminosity is double l && l > limits.LumMax)
            reasons.Add($"luminosidade média {l:0} % acima de {limits.LumMax:0} %");

        // A primeira regra que bater é a de maior prioridade
        var alert =
            avg.Temperature is double t2 && (t2 < limits.TempMin || t2 > limits.TempMax) ? AlertKind.T :
            avg.Humidity is double h2 && (h2 < limits.HumMin || h2 > limits.HumMax) ? AlertKind.U :
            avg.Luminosity is double l2 && l2 > limits.LumMax ? AlertKind.L :
            AlertKind.OK;
        return (alert, reasons);
    }
}
