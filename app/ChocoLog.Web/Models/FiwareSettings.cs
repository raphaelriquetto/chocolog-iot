using System.ComponentModel.DataAnnotations;

namespace ChocoLog.Web.Models;

/// <summary>
/// Endereço e identificadores do FIWARE. Os valores iniciais vêm do appsettings.json;
/// a tela Configuração grava as mudanças em App_Data/fiware.json, porque o IP do Learner Lab muda a cada início.
/// </summary>
public class FiwareSettings
{
    [Required(ErrorMessage = "Informe o IP ou nome do servidor.")]
    [RegularExpression(@"^[A-Za-z0-9.\-]+$", ErrorMessage = "Use só o IP ou o nome do servidor, sem http:// e sem porta.")]
    [Display(Name = "Servidor (IP público da EC2)")]
    public string Host { get; set; } = "localhost";

    [Range(1, 65535)] [Display(Name = "Porta do Orion")] public int OrionPort { get; set; } = 1026;
    [Range(1, 65535)] [Display(Name = "Porta do IoT Agent")] public int IotAgentPort { get; set; } = 4041;
    [Range(1, 65535)] [Display(Name = "Porta do STH-Comet")] public int SthPort { get; set; } = 8666;

    [Required, RegularExpression(@"^[a-z0-9_]{1,50}$", ErrorMessage = "Só letras minúsculas, números e _.")]
    [Display(Name = "fiware-service")]
    public string Service { get; set; } = "chocolog";

    [Required, RegularExpression(@"^/[A-Za-z0-9_/]*$", ErrorMessage = "Deve começar com /.")]
    [Display(Name = "fiware-servicepath")]
    public string ServicePath { get; set; } = "/";

    [Required] [Display(Name = "API key do service group")] public string ApiKey { get; set; } = "chocolog";
    [Required] [Display(Name = "ID do device")] public string DeviceId { get; set; } = "choco001";
    [Required] [Display(Name = "ID da entidade")] public string EntityId { get; set; } = "urn:ngsi-ld:ChocoLog:001";
    [Required] [Display(Name = "Tipo da entidade")] public string EntityType { get; set; } = "ChocoLog";

    public string OrionUrl => $"http://{Host}:{OrionPort}";
    public string IotAgentUrl => $"http://{Host}:{IotAgentPort}";
    public string SthUrl => $"http://{Host}:{SthPort}";

    public FiwareSettings Clone() => (FiwareSettings)MemberwiseClone();
}
