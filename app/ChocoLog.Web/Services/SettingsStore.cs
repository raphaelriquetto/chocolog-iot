using System.Text.Json;
using ChocoLog.Web.Models;

namespace ChocoLog.Web.Services;

/// <summary>
/// Guarda a configuração atual do FIWARE e permite trocá-la sem reiniciar a aplicação.
/// </summary>
public class SettingsStore
{
    private readonly string _file;
    private readonly object _lock = new();
    private FiwareSettings _current;

    public SettingsStore(IConfiguration config, IWebHostEnvironment env)
    {
        _file = Path.Combine(env.ContentRootPath, "App_Data", "fiware.json");
        _current = config.GetSection("Fiware").Get<FiwareSettings>() ?? new FiwareSettings();

        // O que foi salvo pela tela Configuração tem prioridade sobre o appsettings.json
        if (File.Exists(_file))
        {
            try
            {
                var saved = JsonSerializer.Deserialize<FiwareSettings>(File.ReadAllText(_file));
                if (saved is not null) _current = saved;
            }
            catch (JsonException)
            {
                // Arquivo corrompido: segue com o appsettings.json
            }
        }
    }

    public FiwareSettings Current
    {
        get { lock (_lock) return _current.Clone(); }
    }

    public void Save(FiwareSettings settings)
    {
        lock (_lock)
        {
            _current = settings.Clone();
            Directory.CreateDirectory(Path.GetDirectoryName(_file)!);
            File.WriteAllText(_file, JsonSerializer.Serialize(_current, new JsonSerializerOptions { WriteIndented = true }));
        }
    }
}
