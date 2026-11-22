using ChocoLog.Web.Models;

namespace ChocoLog.Web.Services;

/// <summary>Último resultado do monitor, mostrado no dashboard.</summary>
public class MonitorState
{
    private readonly object _lock = new();
    public Averages? Averages { get; private set; }
    public AlertKind Desired { get; private set; } = AlertKind.OK;
    public List<string> Reasons { get; private set; } = [];
    public DateTime? CheckedAt { get; private set; }
    public DateTime? CommandSentAt { get; private set; }
    public AlertKind? LastSent { get; private set; }
    public string? Error { get; private set; }

    public void Update(Averages avg, AlertKind desired, List<string> reasons)
    {
        lock (_lock) { Averages = avg; Desired = desired; Reasons = reasons; CheckedAt = DateTime.UtcNow; Error = null; }
    }

    public void Sent(AlertKind alert)
    {
        lock (_lock) { LastSent = alert; CommandSentAt = DateTime.UtcNow; }
    }

    public void Fail(string error)
    {
        lock (_lock) { Error = error; CheckedAt = DateTime.UtcNow; }
    }
}

/// <summary>
/// Supervisão: a cada Monitor:IntervalSeconds busca as últimas N medidas no STH-Comet, calcula as médias,
/// compara com os limites e, se o alerta mudou, envia o comando "alert" ao ESP32 pelo Orion.
/// Roda em segundo plano, então funciona mesmo com nenhuma página aberta.
/// </summary>
public class AlertMonitor(IServiceScopeFactory scopes, MonitorState state, IConfiguration config, ILogger<AlertMonitor> log)
    : BackgroundService
{
    private static readonly TimeSpan ResendAfter = TimeSpan.FromSeconds(30);

    protected override async Task ExecuteAsync(CancellationToken stop)
    {
        var interval = TimeSpan.FromSeconds(config.GetValue("Monitor:IntervalSeconds", 10));
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await CheckOnceAsync(stop);
            }
            catch (FiwareException e)
            {
                state.Fail(e.Message);
                log.LogWarning("Monitor: {Message}", e.Message);
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                state.Fail("Erro inesperado no monitor: " + e.Message);
                log.LogError(e, "Monitor falhou");
            }
        } while (await timer.WaitForNextTickAsync(stop));
    }

    private async Task CheckOnceAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var fiware = scope.ServiceProvider.GetRequiredService<FiwareClient>();

        var limits = await LoadLimitsAsync(fiware, ct);

        // Janela da média: as últimas N medidas de cada grandeza no STH-Comet
        var t = fiware.GetHistoryAsync("temperature", limits.Window, ct: ct);
        var h = fiware.GetHistoryAsync("humidity", limits.Window, ct: ct);
        var l = fiware.GetHistoryAsync("luminosity", limits.Window, ct: ct);
        await Task.WhenAll(t, h, l);

        var avg = new Averages(AlertEngine.Average(t.Result), AlertEngine.Average(h.Result), AlertEngine.Average(l.Result),
            Math.Max(t.Result.Count, Math.Max(h.Result.Count, l.Result.Count)));
        if (avg.Samples == 0)
        {
            state.Fail("Ainda não há medidas no histórico. O ESP32 está publicando e a assinatura do STH-Comet existe?");
            return;
        }

        var (desired, reasons) = AlertEngine.Decide(avg, limits);
        state.Update(avg, desired, reasons);

        // O que o ESP32 informou estar tocando (atributo alertState, vindo do "a" no MQTT)
        var entity = await fiware.GetEntityAsync(fiware.Settings.EntityId, "alertState", ct);
        var reported = entity["alertState"]?.ToString();

        // Envia quando a decisão muda, ou reenvia se o device não confirmou (por exemplo, se reiniciou)
        var changed = state.LastSent != desired;
        var outOfSync = reported != desired.ToString() &&
                        (state.CommandSentAt is null || DateTime.UtcNow - state.CommandSentAt > ResendAfter);
        if (changed || outOfSync)
        {
            await fiware.SendCommandAsync("alert", desired.ToString(), ct);
            state.Sent(desired);
            log.LogInformation("Comando alert={Alert} enviado ({Reasons})", desired, string.Join("; ", reasons));
        }
    }

    /// <summary>Lê os limites do Orion; se a entidade ainda não existe, usa os valores do chocolog.ino.</summary>
    public static async Task<Limits> LoadLimitsAsync(FiwareClient fiware, CancellationToken ct)
    {
        try
        {
            return Limits.From(await fiware.GetEntityAsync(Limits.EntityId, ct: ct));
        }
        catch (FiwareException e) when (e.NotFound)
        {
            return new Limits();
        }
    }
}
