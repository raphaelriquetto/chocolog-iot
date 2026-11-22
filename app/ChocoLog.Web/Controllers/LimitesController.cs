using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

/// <summary>
/// Limites de conservação, guardados no Orion (entidade ChocoConfig). O monitor relê a cada ciclo,
/// então uma mudança aqui vale na hora, sem reiniciar nada.
/// </summary>
public class LimitesController(FiwareClient fiware) : FiwareController
{
    public async Task<IActionResult> Index(CancellationToken ct)
    {
        Limits? limits = null;
        try
        {
            limits = Limits.From(await fiware.GetEntityAsync(Limits.EntityId, ct: ct));
            ViewBag.Exists = true;
        }
        catch (FiwareException e) when (e.NotFound)
        {
            ViewBag.Exists = false; // primeira vez: mostra os valores do chocolog.ino para salvar
        }
        catch (FiwareException e)
        {
            TempData["Error"] = e.Message;
        }
        return View(limits ?? new Limits());
    }

    /// <summary>Cria ou atualiza (upsert) a entidade de limites.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Index(Limits limits, CancellationToken ct)
    {
        if (ModelState.IsValid && await Try(() => fiware.UpsertEntityAsync(Limits.EntityId, Limits.EntityType, limits.ToAttributes(), ct)))
        {
            Success("Limites salvos. O monitor usa os novos valores no próximo ciclo (até 10 s).");
            return RedirectToAction(nameof(Index));
        }
        ViewBag.Exists = true;
        return View(limits);
    }

    /// <summary>Volta aos valores do chocolog.ino apagando a entidade.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(CancellationToken ct)
    {
        if (await Try(() => fiware.DeleteEntityAsync(Limits.EntityId, ct)))
            Success("Limites apagados do Orion. Valem de novo os valores padrão do chocolog.ino.");
        return RedirectToAction(nameof(Index));
    }
}
