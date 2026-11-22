using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

/// <summary>
/// Endpoints JSON chamados por Ajax (fetch) pelo dashboard e pela consulta de mensagens.
/// A página não recarrega: o JavaScript chama estes endpoints e redesenha só os números e os gráficos.
/// </summary>
[Route("api")]
public class ApiController(FiwareClient fiware, MonitorState monitor) : Controller
{
    private static readonly string[] Measures = ["temperature", "humidity", "luminosity"];

    /// <summary>GET /api/dashboard : valores atuais (Orion), histórico (STH-Comet), limites e estado do monitor.</summary>
    [HttpGet("dashboard")]
    public async Task<IActionResult> Dashboard(int points = 60, CancellationToken ct = default)
    {
        try
        {
            var s = fiware.Settings;
            var current = await fiware.GetEntityAsync(s.EntityId, "temperature,humidity,luminosity,alertState,alert_status,TimeInstant", ct);
            var limits = await AlertMonitor.LoadLimitsAsync(fiware, ct);

            var history = await Task.WhenAll(Measures.Select(m => fiware.GetHistoryAsync(m, Math.Clamp(points, 5, 100), ct: ct)));

            return Json(new
            {
                ok = true,
                current = new
                {
                    temperature = current["temperature"]?.ToString(),
                    humidity = current["humidity"]?.ToString(),
                    luminosity = current["luminosity"]?.ToString(),
                    alertState = current["alertState"]?.ToString(),
                    commandStatus = current["alert_status"]?.ToString(),
                    updatedAt = current["TimeInstant"]?.ToString(),
                },
                limits,
                monitor = new
                {
                    averages = monitor.Averages,
                    desired = monitor.Desired.ToString(),
                    desiredLabel = AlertText.Label(monitor.Desired),
                    reasons = monitor.Reasons,
                    checkedAt = monitor.CheckedAt,
                    commandSentAt = monitor.CommandSentAt,
                    error = monitor.Error,
                },
                history = Measures.Zip(history).ToDictionary(
                    p => p.First,
                    p => p.Second.Select(r => new { t = r.Time, v = r.Number })),
            });
        }
        catch (FiwareException e) when (e.NotFound)
        {
            return Json(new
            {
                ok = false,
                error = "A entidade do ChocoLog ainda não existe no Orion. Provisione em Configuração e ligue o ESP32: " +
                        "ela aparece com a primeira medida.",
            });
        }
        catch (FiwareException e)
        {
            return Json(new { ok = false, error = e.Message });
        }
    }

    /// <summary>
    /// GET /api/mensagens : mensagens IoT guardadas no STH-Comet.
    /// attr = temperature, humidity, luminosity, alertState ou "todas"; quantidade = últimas N (até 100)
    /// ou, com de/ate, o período informado.
    /// </summary>
    [HttpGet("mensagens")]
    public async Task<IActionResult> Mensagens(string attr = "todas", int quantidade = 50, DateTime? de = null, DateTime? ate = null,
        CancellationToken ct = default)
    {
        string[] allowed = [.. Measures, "alertState"];
        var attrs = attr == "todas" ? allowed : allowed.Contains(attr) ? [attr] : [];
        if (attrs.Length == 0) return BadRequest(new { ok = false, error = "Atributo inválido." });
        if (de is not null && ate is not null && de > ate) return BadRequest(new { ok = false, error = "A data inicial é depois da final." });

        try
        {
            var lastN = de is null && ate is null ? Math.Clamp(quantidade, 1, 100) : 0;
            var results = await Task.WhenAll(attrs.Select(a => fiware.GetHistoryAsync(a, lastN, de, ate, ct: ct)));
            var items = results.SelectMany(r => r)
                .OrderByDescending(r => r.Time)
                .Take(Math.Clamp(quantidade, 1, 400))
                .Select(r => new { time = r.Time, attribute = r.Attribute, value = r.Value });
            return Json(new { ok = true, items });
        }
        catch (FiwareException e)
        {
            return Json(new { ok = false, error = e.Message });
        }
    }
}
