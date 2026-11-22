using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using ChocoLog.Web.Models;

namespace ChocoLog.Web.Services;

/// <summary>Erro ao falar com o FIWARE, já com uma mensagem que dá para mostrar na tela.</summary>
public class FiwareException(string message, HttpStatusCode? status = null) : Exception(message)
{
    public HttpStatusCode? Status { get; } = status;
    public bool NotFound => Status == HttpStatusCode.NotFound;
}

/// <summary>
/// Todas as chamadas de API ao FIWARE passam por aqui:
/// Orion (estado atual, porta 1026), IoT Agent (devices, porta 4041) e STH-Comet (histórico, porta 8666).
/// Toda requisição leva os cabeçalhos fiware-service e fiware-servicepath.
/// </summary>
public class FiwareClient(IHttpClientFactory httpFactory, SettingsStore store)
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    public FiwareSettings Settings => store.Current;

    // Orion: entidades

    /// <summary>GET /v2/entities/{id}?options=keyValues : atributos da entidade como chave/valor.</summary>
    public async Task<JsonObject> GetEntityAsync(string id, string? attrs = null, CancellationToken ct = default)
    {
        var query = "options=keyValues" + (attrs is null ? "" : $"&attrs={attrs}");
        var node = await SendAsync(HttpMethod.Get, Orion($"/v2/entities/{Uri.EscapeDataString(id)}?{query}"), null, "Orion", ct);
        return node?.AsObject() ?? new JsonObject();
    }

    /// <summary>GET /v2/entities?type={type}&options=keyValues : lista todas as entidades de um tipo.</summary>
    public async Task<List<JsonObject>> GetEntitiesAsync(string type, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, Orion($"/v2/entities?type={type}&options=keyValues&limit=1000"), null, "Orion", ct);
        return node?.AsArray().Select(n => n!.AsObject()).ToList() ?? [];
    }

    /// <summary>POST /v2/entities?options=upsert : cria a entidade ou atualiza a que já existe.</summary>
    public Task UpsertEntityAsync(string id, string type, Dictionary<string, object> attrs, CancellationToken ct = default)
    {
        var body = new JsonObject { ["id"] = id, ["type"] = type };
        foreach (var (name, attr) in attrs) body[name] = JsonSerializer.SerializeToNode(attr, Json);
        return SendAsync(HttpMethod.Post, Orion("/v2/entities?options=upsert"), body, "Orion", ct);
    }

    /// <summary>POST /v2/entities : cria a entidade e falha se ela já existir.</summary>
    public async Task CreateEntityAsync(string id, string type, Dictionary<string, object> attrs, CancellationToken ct = default)
    {
        var body = new JsonObject { ["id"] = id, ["type"] = type };
        foreach (var (name, attr) in attrs) body[name] = JsonSerializer.SerializeToNode(attr, Json);
        try
        {
            await SendAsync(HttpMethod.Post, Orion("/v2/entities"), body, "Orion", ct);
        }
        catch (FiwareException e) when (e.Status == HttpStatusCode.UnprocessableEntity)
        {
            throw new FiwareException($"Já existe um cadastro com o ID {id}.", e.Status);
        }
    }

    /// <summary>PATCH /v2/entities/{id}/attrs : atualiza atributos existentes.</summary>
    public Task UpdateAttrsAsync(string id, Dictionary<string, object> attrs, CancellationToken ct = default) =>
        SendAsync(new HttpMethod("PATCH"), Orion($"/v2/entities/{Uri.EscapeDataString(id)}/attrs"),
            JsonSerializer.SerializeToNode(attrs, Json), "Orion", ct);

    /// <summary>DELETE /v2/entities/{id}</summary>
    public Task DeleteEntityAsync(string id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, Orion($"/v2/entities/{Uri.EscapeDataString(id)}"), null, "Orion", ct);

    /// <summary>
    /// Envia um comando ao device: o Orion repassa ao IoT Agent, que publica no tópico MQTT /cmd do ESP32.
    /// PATCH /v2/entities/{id}/attrs  { "alert": { "type": "command", "value": "T" } }
    /// </summary>
    public Task SendCommandAsync(string command, string value, CancellationToken ct = default) =>
        UpdateAttrsAsync(Settings.EntityId, new() { [command] = new { type = "command", value } }, ct);

    // Orion: assinaturas (é a assinatura que manda cada medida para o STH-Comet guardar)

    /// <summary>GET /v2/subscriptions</summary>
    public async Task<List<SubscriptionInfo>> GetSubscriptionsAsync(CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, Orion("/v2/subscriptions?limit=1000"), null, "Orion", ct);
        return node?.AsArray().Select(n => new SubscriptionInfo
        {
            Id = n?["id"]?.GetValue<string>() ?? "",
            Description = n?["description"]?.GetValue<string>() ?? "",
            Status = n?["status"]?.GetValue<string>() ?? "",
            Url = n?["notification"]?["httpCustom"]?["url"]?.GetValue<string>()
                  ?? n?["notification"]?["http"]?["url"]?.GetValue<string>() ?? "",
            Attributes = n?["subject"]?["condition"]?["attrs"]?.AsArray().Select(a => a!.GetValue<string>()).ToList() ?? [],
            LastNotification = n?["notification"]?["lastNotification"]?.GetValue<string>(),
        }).ToList() ?? [];
    }

    /// <summary>
    /// POST /v2/subscriptions : avisa o STH-Comet a cada nova medida.
    /// O STH-Comet só entende o formato antigo (NGSIv1), então o corpo é montado com httpCustom,
    /// igual à requisição 2.6 do FIWARE Made Simple. A URL é interna da rede Docker.
    /// </summary>
    public Task CreateSthSubscriptionAsync(IEnumerable<string> attrs, CancellationToken ct = default)
    {
        var s = Settings;
        var list = attrs.ToList();
        var body = new JsonObject
        {
            ["description"] = "ChocoLog: historico no STH-Comet",
            ["subject"] = new JsonObject
            {
                ["entities"] = new JsonArray(new JsonObject { ["id"] = s.EntityId, ["type"] = s.EntityType }),
                ["condition"] = new JsonObject { ["attrs"] = new JsonArray(list.Select(a => (JsonNode)a).ToArray()) },
            },
            ["notification"] = new JsonObject
            {
                ["httpCustom"] = new JsonObject
                {
                    ["url"] = "http://sth-comet:8666/notify",
                    ["json"] = new JsonObject
                    {
                        ["contextResponses"] = new JsonArray(new JsonObject
                        {
                            ["contextElement"] = new JsonObject
                            {
                                ["id"] = "${id}",
                                ["type"] = "${type}",
                                ["isPattern"] = "false",
                                ["attributes"] = new JsonArray(list.Select(a => (JsonNode)new JsonObject
                                {
                                    ["name"] = a,
                                    ["type"] = a == "alertState" ? "Text" : "Number",
                                    ["value"] = "${" + a + "}",
                                }).ToArray()),
                            },
                            ["statusCode"] = new JsonObject { ["code"] = "200", ["reasonPhrase"] = "OK" },
                        }),
                    },
                },
                ["attrs"] = new JsonArray(list.Select(a => (JsonNode)a).ToArray()),
            },
            ["throttling"] = 1,
        };
        return SendAsync(HttpMethod.Post, Orion("/v2/subscriptions"), body, "Orion", ct);
    }

    /// <summary>DELETE /v2/subscriptions/{id}</summary>
    public Task DeleteSubscriptionAsync(string id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, Orion($"/v2/subscriptions/{Uri.EscapeDataString(id)}"), null, "Orion", ct);

    // IoT Agent: service group e devices

    /// <summary>GET /iot/services : service groups (apikey de cada grupo de devices).</summary>
    public async Task<List<JsonObject>> GetServicesAsync(CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, IotAgent("/iot/services"), null, "IoT Agent", ct);
        return node?["services"]?.AsArray().Select(n => n!.AsObject()).ToList() ?? [];
    }

    /// <summary>POST /iot/services : cria o service group com a apikey usada nos tópicos MQTT.</summary>
    public Task CreateServiceAsync(CancellationToken ct = default)
    {
        var s = Settings;
        var body = new JsonObject
        {
            ["services"] = new JsonArray(new JsonObject
            {
                ["apikey"] = s.ApiKey,
                ["resource"] = "/iot/d",
                ["entity_type"] = s.EntityType,
                ["autoprovision"] = false,
            }),
        };
        return SendAsync(HttpMethod.Post, IotAgent("/iot/services"), body, "IoT Agent", ct);
    }

    /// <summary>GET /iot/devices</summary>
    public async Task<List<DeviceInfo>> GetDevicesAsync(CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, IotAgent("/iot/devices?limit=1000"), null, "IoT Agent", ct);
        return node?["devices"]?.AsArray().Select(n => DeviceInfo.From(n!.AsObject())).ToList() ?? [];
    }

    /// <summary>GET /iot/devices/{id}</summary>
    public async Task<DeviceInfo> GetDeviceAsync(string id, CancellationToken ct = default)
    {
        var node = await SendAsync(HttpMethod.Get, IotAgent($"/iot/devices/{Uri.EscapeDataString(id)}"), null, "IoT Agent", ct);
        return DeviceInfo.From(node!.AsObject());
    }

    /// <summary>
    /// POST /iot/devices : provisiona um ChocoLog.
    /// Os nomes curtos (t, h, l, a) economizam bytes no MQTT; o IoT Agent traduz para os nomes completos no Orion.
    /// </summary>
    public async Task CreateDeviceAsync(DeviceInput input, CancellationToken ct = default)
    {
        var s = Settings;
        var body = new JsonObject
        {
            ["devices"] = new JsonArray(new JsonObject
            {
                ["device_id"] = input.DeviceId,
                ["apikey"] = s.ApiKey,
                ["entity_name"] = input.EntityId,
                ["entity_type"] = s.EntityType,
                ["protocol"] = "PDI-IoTA-UltraLight",
                ["transport"] = "MQTT",
                ["commands"] = new JsonArray(new JsonObject { ["name"] = "alert", ["type"] = "command" }),
                ["attributes"] = new JsonArray(
                    Attribute("t", "temperature", "Number"),
                    Attribute("h", "humidity", "Number"),
                    Attribute("l", "luminosity", "Number"),
                    Attribute("a", "alertState", "Text")),
                ["static_attributes"] = StaticAttributes(input),
            }),
        };
        try
        {
            await SendAsync(HttpMethod.Post, IotAgent("/iot/devices"), body, "IoT Agent", ct);
        }
        catch (FiwareException e) when (e.Status == HttpStatusCode.Conflict)
        {
            throw new FiwareException($"Já existe um device com o ID {input.DeviceId}.", e.Status);
        }
    }

    /// <summary>PUT /iot/devices/{id} : atualiza descrição e local (atributos estáticos).</summary>
    public Task UpdateDeviceAsync(DeviceInput input, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Put, IotAgent($"/iot/devices/{Uri.EscapeDataString(input.DeviceId)}"),
            new JsonObject { ["static_attributes"] = StaticAttributes(input) }, "IoT Agent", ct);

    /// <summary>DELETE /iot/devices/{id}</summary>
    public Task DeleteDeviceAsync(string id, CancellationToken ct = default) =>
        SendAsync(HttpMethod.Delete, IotAgent($"/iot/devices/{Uri.EscapeDataString(id)}"), null, "IoT Agent", ct);

    // STH-Comet: histórico

    /// <summary>
    /// GET /STH/v1/contextEntities/type/{type}/id/{id}/attributes/{attr}
    /// Com lastN traz as N medidas mais recentes; com dateFrom/dateTo, as do período (até 100 por página).
    /// </summary>
    public async Task<List<Reading>> GetHistoryAsync(string attr, int lastN = 0, DateTime? from = null, DateTime? to = null,
        int offset = 0, CancellationToken ct = default)
    {
        var s = Settings;
        var query = lastN > 0
            ? $"lastN={Math.Min(lastN, 100)}"
            : $"hLimit=100&hOffset={offset}";
        if (from is not null) query += "&dateFrom=" + from.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");
        if (to is not null) query += "&dateTo=" + to.Value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ss.fffZ");

        var url = Sth($"/STH/v1/contextEntities/type/{s.EntityType}/id/{Uri.EscapeDataString(s.EntityId)}/attributes/{attr}?{query}");
        JsonNode? node;
        try
        {
            node = await SendAsync(HttpMethod.Get, url, null, "STH-Comet", ct);
        }
        catch (FiwareException e) when (e.NotFound)
        {
            return []; // ainda sem histórico
        }

        var values = node?["contextResponses"]?[0]?["contextElement"]?["attributes"]?[0]?["values"]?.AsArray();
        if (values is null) return [];
        var list = new List<Reading>();
        foreach (var v in values)
        {
            if (v is null) continue;
            var time = DateTime.Parse(v["recvTime"]!.GetValue<string>(), CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal);
            list.Add(new Reading(attr, time, ReadValue(v["attrValue"])));
        }
        return list;
    }

    // Saúde dos serviços

    /// <summary>GET /version (Orion e STH-Comet) e /iot/about (IoT Agent)</summary>
    public async Task<Dictionary<string, string>> CheckServicesAsync(CancellationToken ct = default)
    {
        var result = new Dictionary<string, string>();
        async Task Check(string name, string url, Func<JsonNode?, string> version)
        {
            try { result[name] = "OK, versão " + version(await SendAsync(HttpMethod.Get, url, null, name, ct)); }
            catch (FiwareException e) { result[name] = "Falhou: " + e.Message; }
        }
        await Task.WhenAll(
            Check("Orion", Orion("/version"), n => n?["orion"]?["version"]?.GetValue<string>() ?? "?"),
            Check("IoT Agent", IotAgent("/iot/about"), n => n?["version"]?.GetValue<string>() ?? "?"),
            Check("STH-Comet", Sth("/version"), n => n?["version"]?.GetValue<string>() ?? "?"));
        return result;
    }

    // Infraestrutura

    private string Orion(string path) => Settings.OrionUrl + path;
    private string IotAgent(string path) => Settings.IotAgentUrl + path;
    private string Sth(string path) => Settings.SthUrl + path;

    private static JsonObject Attribute(string objectId, string name, string type) =>
        new() { ["object_id"] = objectId, ["name"] = name, ["type"] = type };

    private static JsonArray StaticAttributes(DeviceInput input) => new(
        new JsonObject { ["name"] = "descricao", ["type"] = "Text", ["value"] = input.Description },
        new JsonObject { ["name"] = "local", ["type"] = "Text", ["value"] = input.Location });

    /// <summary>O STH devolve o valor como texto ou número, conforme a versão: aceita os dois.</summary>
    private static string ReadValue(JsonNode? node) => node switch
    {
        null => "",
        JsonValue v when v.TryGetValue<double>(out var d) => d.ToString(CultureInfo.InvariantCulture),
        _ => node.ToString(),
    };

    private async Task<JsonNode?> SendAsync(HttpMethod method, string url, JsonNode? body, string component, CancellationToken ct)
    {
        var s = Settings;
        using var request = new HttpRequestMessage(method, url);
        request.Headers.Add("fiware-service", s.Service);
        request.Headers.Add("fiware-servicepath", s.ServicePath);
        if (body is not null)
            request.Content = new StringContent(body.ToJsonString(), Encoding.UTF8, "application/json");

        HttpResponseMessage response;
        try
        {
            response = await httpFactory.CreateClient("fiware").SendAsync(request, ct);
        }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException && !ct.IsCancellationRequested)
        {
            throw new FiwareException($"Sem resposta do {component} em {new Uri(url).GetLeftPart(UriPartial.Authority)}. " +
                                      "Confira o IP na tela Configuração, se a EC2 está ligada e se a porta está liberada no Security Group.");
        }

        using (response)
        {
            var text = await response.Content.ReadAsStringAsync(ct);
            if (!response.IsSuccessStatusCode)
                throw new FiwareException($"{component} respondeu {(int)response.StatusCode}: {DescribeError(text)}", response.StatusCode);
            return string.IsNullOrWhiteSpace(text) ? null : JsonNode.Parse(text);
        }
    }

    /// <summary>Extrai "description" (Orion) ou "message" (IoT Agent) do corpo do erro.</summary>
    private static string DescribeError(string body)
    {
        try
        {
            var n = JsonNode.Parse(body);
            return n?["description"]?.GetValue<string>() ?? n?["message"]?.GetValue<string>() ?? body;
        }
        catch (JsonException)
        {
            return string.IsNullOrWhiteSpace(body) ? "sem detalhes" : body;
        }
    }
}
