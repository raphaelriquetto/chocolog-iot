using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

/// <summary>CRUD das cargas de chocolate. Cada carga é uma entidade do tipo "Carga" no Orion.</summary>
public class CargasController(FiwareClient fiware) : FiwareController
{
    public async Task<IActionResult> Index(string? busca, CancellationToken ct)
    {
        var cargas = await Try(async () => (await fiware.GetEntitiesAsync(Carga.EntityType, ct)).Select(Carga.From).ToList()) ?? [];
        if (!string.IsNullOrWhiteSpace(busca))
        {
            cargas = cargas.Where(c => $"{c.Code} {c.Product} {c.Origin} {c.Destination} {c.Status}"
                .Contains(busca.Trim(), StringComparison.OrdinalIgnoreCase)).ToList();
        }
        ViewBag.Busca = busca;
        return View(cargas.OrderByDescending(c => c.Departure).ToList());
    }

    public async Task<IActionResult> Create(CancellationToken ct)
    {
        await LoadDevicesAsync(ct);
        return View("Form", new Carga());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(Carga carga, CancellationToken ct)
    {
        if (ModelState.IsValid && await Try(() => fiware.CreateEntityAsync(carga.EntityId, Carga.EntityType, carga.ToAttributes(), ct)))
        {
            Success($"Carga {carga.Code} cadastrada.");
            return RedirectToAction(nameof(Index));
        }
        await LoadDevicesAsync(ct);
        return View("Form", carga);
    }

    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var carga = await Try(async () => Carga.From(await fiware.GetEntityAsync($"urn:ngsi-ld:Carga:{id}", ct: ct)));
        if (carga is null) return RedirectToAction(nameof(Index));
        await LoadDevicesAsync(ct);
        ViewBag.Editing = true;
        return View("Form", carga);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, Carga carga, CancellationToken ct)
    {
        carga.Code = id; // o código é o ID da entidade e não muda
        ModelState.Remove(nameof(Carga.Code));
        if (ModelState.IsValid && await Try(() => fiware.UpdateAttrsAsync(carga.EntityId, carga.ToAttributes(), ct)))
        {
            Success($"Carga {carga.Code} atualizada.");
            return RedirectToAction(nameof(Index));
        }
        await LoadDevicesAsync(ct);
        ViewBag.Editing = true;
        return View("Form", carga);
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, CancellationToken ct)
    {
        if (await Try(() => fiware.DeleteEntityAsync($"urn:ngsi-ld:Carga:{id}", ct)))
            Success($"Carga {id} excluída.");
        return RedirectToAction(nameof(Index));
    }

    /// <summary>Lista de devices para o campo "Monitorada pelo device".</summary>
    private async Task LoadDevicesAsync(CancellationToken ct)
    {
        ViewBag.Devices = await Try(() => fiware.GetDevicesAsync(ct)) ?? [];
    }
}
