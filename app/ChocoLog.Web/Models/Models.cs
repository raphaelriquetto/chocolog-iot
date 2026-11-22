using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.Text.Json.Nodes;

namespace ChocoLog.Web.Models;

/// <summary>Regras de texto do Orion: ele recusa &lt; &gt; " ' = ; ( ) nos valores.</summary>
public static class OrionText
{
    public const string Pattern = @"^[^<>""'=;()]*$";
    public const string Message = "Não use os caracteres < > \" ' = ; ( ) (o FIWARE não aceita).";
    public const string IdPattern = @"^[A-Za-z0-9_\-]{1,40}$";
    public const string IdMessage = "Use só letras, números, - e _ (até 40).";
}

/// <summary>Uma medida guardada no STH-Comet.</summary>
public record Reading(string Attribute, DateTime Time, string Value)
{
    public double? Number => double.TryParse(Value, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
}

/// <summary>Os quatro estados do comando "alert". Prioridade: temperatura, umidade, luminosidade.</summary>
public enum AlertKind { OK, T, U, L }

public static class AlertText
{
    public static string Label(AlertKind kind) => kind switch
    {
        AlertKind.T => "Temperatura fora do limite",
        AlertKind.U => "Umidade fora do limite",
        AlertKind.L => "Luz acima do limite",
        _ => "Carga OK",
    };
}

/// <summary>Limites de conservação do chocolate. Guardados no Orion, na entidade ChocoConfig.</summary>
public class Limits : IValidatableObject
{
    public const string EntityId = "urn:ngsi-ld:ChocoConfig:001";
    public const string EntityType = "ChocoConfig";

    // Valores iniciais iguais aos do chocolog.ino (trigger_t_min, trigger_t_max, ...)
    [Range(-10, 60)] [Display(Name = "Temperatura mínima (°C)")] public double TempMin { get; set; } = 15;
    [Range(-10, 60)] [Display(Name = "Temperatura máxima (°C)")] public double TempMax { get; set; } = 22;
    [Range(0, 100)] [Display(Name = "Umidade mínima (%)")] public double HumMin { get; set; } = 45;
    [Range(0, 100)] [Display(Name = "Umidade máxima (%)")] public double HumMax { get; set; } = 60;
    [Range(0, 100)] [Display(Name = "Luminosidade máxima (%)")] public double LumMax { get; set; } = 50;

    [Range(3, 100, ErrorMessage = "A janela deve ter entre 3 e 100 medidas.")]
    [Display(Name = "Janela da média (medidas)")]
    public int Window { get; set; } = 12; // 12 medidas x 5 s = 1 minuto

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (TempMin >= TempMax)
            yield return new("A temperatura mínima precisa ser menor que a máxima.", [nameof(TempMin)]);
        if (HumMin >= HumMax)
            yield return new("A umidade mínima precisa ser menor que a máxima.", [nameof(HumMin)]);
    }

    public Dictionary<string, object> ToAttributes() => new()
    {
        ["tempMin"] = new { type = "Number", value = TempMin },
        ["tempMax"] = new { type = "Number", value = TempMax },
        ["humMin"] = new { type = "Number", value = HumMin },
        ["humMax"] = new { type = "Number", value = HumMax },
        ["lumMax"] = new { type = "Number", value = LumMax },
        ["window"] = new { type = "Number", value = Window },
    };

    public static Limits From(JsonObject o) => new()
    {
        TempMin = o["tempMin"]?.GetValue<double>() ?? 15,
        TempMax = o["tempMax"]?.GetValue<double>() ?? 22,
        HumMin = o["humMin"]?.GetValue<double>() ?? 45,
        HumMax = o["humMax"]?.GetValue<double>() ?? 60,
        LumMax = o["lumMax"]?.GetValue<double>() ?? 50,
        Window = (int)(o["window"]?.GetValue<double>() ?? 12),
    };
}

/// <summary>Formulário de cadastro de device no IoT Agent.</summary>
public class DeviceInput
{
    [Required(ErrorMessage = "Informe o ID do device.")]
    [RegularExpression(OrionText.IdPattern, ErrorMessage = OrionText.IdMessage)]
    [Display(Name = "ID do device (o mesmo do firmware)")]
    public string DeviceId { get; set; } = "";

    [Required(ErrorMessage = "Informe o ID da entidade.")]
    [RegularExpression(@"^urn:ngsi-ld:[A-Za-z0-9_\-:]{1,80}$", ErrorMessage = "Formato: urn:ngsi-ld:ChocoLog:001")]
    [Display(Name = "ID da entidade no Orion")]
    public string EntityId { get; set; } = "";

    [Required(ErrorMessage = "Descreva o device.")]
    [StringLength(80), RegularExpression(OrionText.Pattern, ErrorMessage = OrionText.Message)]
    [Display(Name = "Descrição")]
    public string Description { get; set; } = "";

    [Required(ErrorMessage = "Informe onde o device está.")]
    [StringLength(80), RegularExpression(OrionText.Pattern, ErrorMessage = OrionText.Message)]
    [Display(Name = "Local (ex.: Caminhão 3, Galpão B)")]
    public string Location { get; set; } = "";
}

/// <summary>Device como o IoT Agent devolve.</summary>
public class DeviceInfo
{
    public string DeviceId { get; set; } = "";
    public string EntityId { get; set; } = "";
    public string EntityType { get; set; } = "";
    public string Transport { get; set; } = "";
    public string Description { get; set; } = "";
    public string Location { get; set; } = "";
    public List<string> Attributes { get; set; } = [];
    public List<string> Commands { get; set; } = [];

    public static DeviceInfo From(JsonObject o)
    {
        string Static(string name) => o["static_attributes"]?.AsArray()
            .FirstOrDefault(a => a?["name"]?.GetValue<string>() == name)?["value"]?.ToString() ?? "";
        return new DeviceInfo
        {
            DeviceId = o["device_id"]?.GetValue<string>() ?? "",
            EntityId = o["entity_name"]?.GetValue<string>() ?? "",
            EntityType = o["entity_type"]?.GetValue<string>() ?? "",
            Transport = o["transport"]?.GetValue<string>() ?? "",
            Description = Static("descricao"),
            Location = Static("local"),
            Attributes = o["attributes"]?.AsArray().Select(a => $"{a?["object_id"]} → {a?["name"]}").ToList() ?? [],
            Commands = o["commands"]?.AsArray().Select(a => a?["name"]?.GetValue<string>() ?? "").ToList() ?? [],
        };
    }

    public DeviceInput ToInput() => new() { DeviceId = DeviceId, EntityId = EntityId, Description = Description, Location = Location };
}

public class SubscriptionInfo
{
    public string Id { get; set; } = "";
    public string Description { get; set; } = "";
    public string Status { get; set; } = "";
    public string Url { get; set; } = "";
    public List<string> Attributes { get; set; } = [];
    public string? LastNotification { get; set; }
}

/// <summary>Carga de chocolate em transporte, monitorada por um ChocoLog. Guardada no Orion (tipo Carga).</summary>
public class Carga : IValidatableObject
{
    public const string EntityType = "Carga";
    public static readonly string[] StatusList = ["Aguardando coleta", "Em trânsito", "Entregue", "Avariada"];

    [Required(ErrorMessage = "Informe o código da carga.")]
    [RegularExpression(OrionText.IdPattern, ErrorMessage = OrionText.IdMessage)]
    [Display(Name = "Código")]
    public string Code { get; set; } = "";

    [Required(ErrorMessage = "Informe o produto.")]
    [StringLength(60), RegularExpression(OrionText.Pattern, ErrorMessage = OrionText.Message)]
    [Display(Name = "Produto")]
    public string Product { get; set; } = "";

    [Range(1, 100000, ErrorMessage = "Quantidade entre 1 e 100.000 caixas.")]
    [Display(Name = "Quantidade (caixas)")]
    public int Boxes { get; set; } = 1;

    [Required(ErrorMessage = "Informe a origem.")]
    [StringLength(60), RegularExpression(OrionText.Pattern, ErrorMessage = OrionText.Message)]
    [Display(Name = "Origem")]
    public string Origin { get; set; } = "";

    [Required(ErrorMessage = "Informe o destino.")]
    [StringLength(60), RegularExpression(OrionText.Pattern, ErrorMessage = OrionText.Message)]
    [Display(Name = "Destino")]
    public string Destination { get; set; } = "";

    [DataType(DataType.Date)]
    [Display(Name = "Data de saída")]
    public DateTime Departure { get; set; } = DateTime.Today;

    [Required] [Display(Name = "Situação")] public string Status { get; set; } = StatusList[0];

    [Required(ErrorMessage = "Escolha o device que acompanha a carga.")]
    [Display(Name = "Monitorada pelo device")]
    public string DeviceEntityId { get; set; } = "";

    public string EntityId => $"urn:ngsi-ld:Carga:{Code}";

    public IEnumerable<ValidationResult> Validate(ValidationContext context)
    {
        if (string.Equals(Origin?.Trim(), Destination?.Trim(), StringComparison.OrdinalIgnoreCase))
            yield return new("A origem e o destino não podem ser iguais.", [nameof(Destination)]);
        if (!StatusList.Contains(Status))
            yield return new("Situação inválida.", [nameof(Status)]);
        if (Departure.Year < 2020 || Departure > DateTime.Today.AddYears(1))
            yield return new("Data de saída fora do intervalo aceito.", [nameof(Departure)]);
    }

    public Dictionary<string, object> ToAttributes() => new()
    {
        ["produto"] = new { type = "Text", value = Product.Trim() },
        ["caixas"] = new { type = "Number", value = Boxes },
        ["origem"] = new { type = "Text", value = Origin.Trim() },
        ["destino"] = new { type = "Text", value = Destination.Trim() },
        ["dataSaida"] = new { type = "DateTime", value = Departure.ToString("yyyy-MM-dd") + "T00:00:00.000Z" },
        ["situacao"] = new { type = "Text", value = Status },
        // Relationship: liga a carga à entidade do ChocoLog que mede a temperatura dela
        ["refChocoLog"] = new { type = "Relationship", value = DeviceEntityId },
    };

    public static Carga From(JsonObject o) => new()
    {
        Code = (o["id"]?.GetValue<string>() ?? "").Replace("urn:ngsi-ld:Carga:", ""),
        Product = o["produto"]?.ToString() ?? "",
        Boxes = (int)(o["caixas"]?.GetValue<double>() ?? 0),
        Origin = o["origem"]?.ToString() ?? "",
        Destination = o["destino"]?.ToString() ?? "",
        Departure = DateTime.TryParse(o["dataSaida"]?.ToString(), CultureInfo.InvariantCulture,
            DateTimeStyles.AdjustToUniversal, out var d) ? d.Date : DateTime.Today,
        Status = o["situacao"]?.ToString() ?? StatusList[0],
        DeviceEntityId = o["refChocoLog"]?.ToString() ?? "",
    };
}
