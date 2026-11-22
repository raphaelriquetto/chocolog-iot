using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

public class ProvisionStep
{
    public string Name { get; set; } = "";
    public bool Done { get; set; }
    public string Detail { get; set; } = "";
}

/// <summary>
/// Endereço do FIWARE (o IP do Learner Lab muda a cada início), teste dos serviços,
/// provisionamento em um clique e cadastro das assinaturas do histórico.
/// </summary>
public class ConfiguracaoController(FiwareClient fiware, SettingsStore store) : FiwareController
{
    private static readonly string[] HistoryAttrs = ["temperature", "humidity", "luminosity", "alertState"];

    public async Task<IActionResult> Index(CancellationToken ct)
    {
        ViewBag.Steps = await CheckStepsAsync(ct);
        ViewBag.Subscriptions = await Try(() => fiware.GetSubscriptionsAsync(ct)) ?? [];
        return View(store.Current);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public IActionResult Index(FiwareSettings settings)
    {
        if (!ModelState.IsValid)
        {
            ViewBag.Steps = new List<ProvisionStep>();
            ViewBag.Subscriptions = new List<SubscriptionInfo>();
            return View(settings);
        }
        store.Save(settings);
        Success($"Configuração salva. Servidor: {settings.Host}");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>GET /version de cada serviço, mostrado num quadro na própria tela.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Testar(CancellationToken ct)
    {
        var results = await fiware.CheckServicesAsync(ct);
        TempData[results.Values.All(v => v.StartsWith("OK")) ? "Success" : "Error"] =
            string.Join(" · ", results.Select(r => $"{r.Key}: {r.Value}"));
        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Cria o que falta, na ordem: service group (apikey), device, limites e assinatura do STH-Comet.
    /// Pode clicar mais de uma vez: o que já existe é pulado.
    /// </summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Provisionar(CancellationToken ct)
    {
        var s = fiware.Settings;
        var created = new List<string>();
        var ok = await Try(async () =>
        {
            var steps = await CheckStepsAsync(ct);
            if (!steps[0].Done) { await fiware.CreateServiceAsync(ct); created.Add("service group"); }
            if (!steps[1].Done)
            {
                await fiware.CreateDeviceAsync(new DeviceInput
                {
                    DeviceId = s.DeviceId, EntityId = s.EntityId, Description = "ChocoLog", Location = "Caminhão 1",
                }, ct);
                created.Add("device");
            }
            if (!steps[2].Done)
            {
                await fiware.UpsertEntityAsync(Limits.EntityId, Limits.EntityType, new Limits().ToAttributes(), ct);
                created.Add("limites");
            }
            if (!steps[3].Done) { await fiware.CreateSthSubscriptionAsync(HistoryAttrs, ct); created.Add("assinatura do histórico"); }
        });
        if (ok) Success(created.Count == 0 ? "Tudo já estava provisionado." : "Criado: " + string.Join(", ", created) + ".");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> CriarAssinatura(CancellationToken ct)
    {
        if (await Try(() => fiware.CreateSthSubscriptionAsync(HistoryAttrs, ct)))
            Success("Assinatura do STH-Comet criada.");
        return RedirectToAction(nameof(Index));
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> ExcluirAssinatura(string id, CancellationToken ct)
    {
        if (await Try(() => fiware.DeleteSubscriptionAsync(id, ct)))
            Success("Assinatura excluída. Sem ela, o histórico para de receber medidas.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Confere cada etapa do provisionamento; se o FIWARE não responder, todas ficam pendentes.</summary>
    private async Task<List<ProvisionStep>> CheckStepsAsync(CancellationToken ct)
    {
        var s = fiware.Settings;
        var steps = new List<ProvisionStep>
        {
            new() { Name = "Service group (apikey " + s.ApiKey + ")" },
            new() { Name = "Device " + s.DeviceId },
            new() { Name = "Limites de conservação" },
            new() { Name = "Assinatura do histórico (STH-Comet)" },
        };
        try
        {
            var services = await fiware.GetServicesAsync(ct);
            steps[0].Done = services.Any(x => x["apikey"]?.ToString() == s.ApiKey);
            var devices = await fiware.GetDevicesAsync(ct);
            steps[1].Done = devices.Any(d => d.DeviceId == s.DeviceId);
            try { await fiware.GetEntityAsync(Limits.EntityId, ct: ct); steps[2].Done = true; }
            catch (FiwareException e) when (e.NotFound) { }
            var subs = await fiware.GetSubscriptionsAsync(ct);
            steps[3].Done = subs.Any(x => x.Url.Contains("sth-comet") && x.Attributes.Contains("temperature"));
        }
        catch (FiwareException e)
        {
            foreach (var step in steps) step.Detail = "sem resposta";
            TempData["Error"] ??= e.Message;
        }
        return steps;
    }
}
