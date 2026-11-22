using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers;

/// <summary>
/// Base dos controllers que chamam o FIWARE: transforma FiwareException em mensagem na tela,
/// para um erro de rede nunca virar uma página de erro genérica.
/// </summary>
public abstract class FiwareController : Controller
{
    protected void Success(string message) => TempData["Success"] = message;

    /// <summary>Executa uma chamada; se falhar, mostra o erro e devolve false.</summary>
    protected async Task<bool> Try(Func<Task> action)
    {
        try
        {
            await action();
            return true;
        }
        catch (FiwareException e)
        {
            ShowError(e.Message);
            return false;
        }
    }

    /// <summary>Executa uma consulta; se falhar, mostra o erro e devolve null.</summary>
    protected async Task<T?> Try<T>(Func<Task<T>> action) where T : class
    {
        try
        {
            return await action();
        }
        catch (FiwareException e)
        {
            ShowError(e.Message);
            return null;
        }
    }

    // Em POST com formulário, o erro aparece no topo do formulário; nos outros casos, no aviso da página
    private void ShowError(string message)
    {
        if (HttpContext.Request.Method == "POST") ModelState.AddModelError(string.Empty, message);
        TempData["Error"] = message;
    }
}
