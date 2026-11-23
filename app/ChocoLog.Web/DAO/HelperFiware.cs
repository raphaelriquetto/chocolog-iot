using System;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace ChocoLog.Web.DAO
{
    // erro do FIWARE já com uma mensagem que dá para mostrar na tela
    public class FiwareException : Exception
    {
        public FiwareException(string mensagem, HttpStatusCode? status = null) : base(mensagem)
        {
            Status = status;
        }

        public HttpStatusCode? Status { get; }
        public bool NaoEncontrado => Status == HttpStatusCode.NotFound;
    }

    // equivalente ao HelperDAO: toda chamada de API ao FIWARE passa por aqui,
    // com os cabeçalhos fiware-service e fiware-servicepath
    public static class HelperFiware
    {
        // um HttpClient só para a aplicação inteira; 8 s de limite para não travar a tela com a EC2 desligada
        private static readonly HttpClient cliente = new HttpClient { Timeout = TimeSpan.FromSeconds(8) };

        public static JToken Executa(string metodo, string url, JToken corpo = null, string componente = "FIWARE")
        {
            var requisicao = new HttpRequestMessage(new HttpMethod(metodo), url);
            requisicao.Headers.Add("fiware-service", ConexaoFiware.Atual.Service);
            requisicao.Headers.Add("fiware-servicepath", ConexaoFiware.Atual.ServicePath);
            if (corpo != null)
                requisicao.Content = new StringContent(corpo.ToString(), Encoding.UTF8, "application/json");

            HttpResponseMessage resposta;
            try
            {
                resposta = cliente.SendAsync(requisicao).GetAwaiter().GetResult();
            }
            catch (Exception erro) when (erro is HttpRequestException || erro is TaskCanceledException)
            {
                throw new FiwareException($"Sem resposta do {componente} em {new Uri(url).GetLeftPart(UriPartial.Authority)}. " +
                    "Confira o IP na tela Configuração, se a EC2 está ligada e se a porta está liberada no Security Group.");
            }

            using (resposta)
            {
                string texto = resposta.Content.ReadAsStringAsync().GetAwaiter().GetResult();
                if (!resposta.IsSuccessStatusCode)
                    throw new FiwareException($"{componente} respondeu {(int)resposta.StatusCode}: {DescreveErro(texto)}", resposta.StatusCode);
                return string.IsNullOrWhiteSpace(texto) ? null : JToken.Parse(texto);
            }
        }

        public static JToken Orion(string metodo, string caminho, JToken corpo = null) =>
            Executa(metodo, ConexaoFiware.UrlOrion + caminho, corpo, "Orion");

        public static JToken IotAgent(string metodo, string caminho, JToken corpo = null) =>
            Executa(metodo, ConexaoFiware.UrlIotAgent + caminho, corpo, "IoT Agent");

        public static JToken Sth(string metodo, string caminho) =>
            Executa(metodo, ConexaoFiware.UrlSth + caminho, null, "STH-Comet");

        // monta um atributo NGSI-v2: { "type": "...", "value": ... }
        public static JObject Atributo(string tipo, object valor) =>
            new JObject { ["type"] = tipo, ["value"] = JToken.FromObject(valor) };

        // pega "description" (Orion) ou "message" (IoT Agent) do corpo do erro
        private static string DescreveErro(string corpo)
        {
            try
            {
                var json = JObject.Parse(corpo);
                return (string)json["description"] ?? (string)json["message"] ?? corpo;
            }
            catch (Exception)
            {
                return string.IsNullOrWhiteSpace(corpo) ? "sem detalhes" : corpo;
            }
        }
    }

}
