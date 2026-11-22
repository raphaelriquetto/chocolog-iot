using ChocoLog.Web.Models;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

/// <summary>CRUD dos devices ChocoLog no IoT Agent (provisionamento).</summary>
public class DispositivosController(FiwareClient fiware) : FiwareController
{
    public async Task<IActionResult> Index(CancellationToken ct) =>
        View(await Try(() => fiware.GetDevicesAsync(ct)) ?? []);

    public IActionResult Create()
    {
        var s = fiware.Settings;
        return View("Form", new DeviceInput { DeviceId = s.DeviceId, EntityId = s.EntityId, Description = "ChocoLog", Location = "" });
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Create(DeviceInput input, CancellationToken ct)
    {
        if (ModelState.IsValid && await Try(() => fiware.CreateDeviceAsync(input, ct)))
        {
            Success($"Device {input.DeviceId} provisionado. Grave o mesmo ID no firmware (DEVICE_ID).");
            return RedirectToAction(nameof(Index));
        }
        return View("Form", input);
    }

    public async Task<IActionResult> Edit(string id, CancellationToken ct)
    {
        var device = await Try(() => fiware.GetDeviceAsync(id, ct));
        if (device is null) return RedirectToAction(nameof(Index));
        ViewBag.Editing = true;
        return View("Form", device.ToInput());
    }

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Edit(string id, DeviceInput input, CancellationToken ct)
    {
        input.DeviceId = id; // ID e entidade não mudam depois do provisionamento (o campo vem desabilitado)
        ModelState.Remove(nameof(DeviceInput.DeviceId));
        if (ModelState.IsValid && await Try(() => fiware.UpdateDeviceAsync(input, ct)))
        {
            Success($"Device {id} atualizado.");
            return RedirectToAction(nameof(Index));
        }
        ViewBag.Editing = true;
        return View("Form", input);
    }

    /// <summary>Remove o device do IoT Agent e, se marcado, também a entidade dele no Orion.</summary>
    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Delete(string id, string entityId, bool apagarEntidade, CancellationToken ct)
    {
        if (await Try(() => fiware.DeleteDeviceAsync(id, ct)))
        {
            if (apagarEntidade && !string.IsNullOrEmpty(entityId)) await Try(() => fiware.DeleteEntityAsync(entityId, ct));
            Success($"Device {id} removido.");
        }
        return RedirectToAction(nameof(Index));
    }
}
