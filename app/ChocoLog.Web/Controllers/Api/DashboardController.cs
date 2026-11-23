using System;
using System.Linq;
using ChocoLog.Web.DAO;
using ChocoLog.Web.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ChocoLog.Web.Controllers.Api
{
    /// <summary>
    /// APIs do dashboard: estado atual (Orion), histórico (STH-Comet), limites e decisão do monitor de alertas
    /// </summary>
    [ApiController]
    [Route("api/[controller]")]
    [Produces("application/json")]
    public class DashboardController : ControllerBase
    {
        /// <summary>
        /// Valores atuais do ChocoLog, médias da janela e alerta decidido pelo monitor.
        /// </summary>
        /// <response code="200">Estado atual e decisão do monitor</response>
        /// <response code="503">FIWARE sem resposta ou entidade ainda não criada</response>
        [HttpGet("atual")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
        public IActionResult Atual()
        {
            try
            {
                var estado = new LeituraDAO().EstadoAtual();
                var limites = new LimiteDAO().Consulta();
                return Ok(new
                {
                    temperatura = (double?)estado["temperature"],
                    umidade = (double?)estado["humidity"],
                    luminosidade = (double?)estado["luminosity"],
                    alertaConfirmado = (string)estado["alertState"],
                    statusComando = (string)estado["alert_status"],
                    atualizadoEm = (string)estado["TimeInstant"],
                    limites,
                    monitor = new
                    {
                        medias = EstadoMonitor.Medias,
                        alerta = EstadoMonitor.Alerta,
                        descricao = RegraAlertas.Descricao(EstadoMonitor.Alerta),
                        motivos = EstadoMonitor.Motivos,
                        verificadoEm = EstadoMonitor.VerificadoEm,
                        comandoEnviadoEm = EstadoMonitor.ComandoEnviadoEm,
                        erro = EstadoMonitor.Erro
                    }
                });
            }
            catch (FiwareException erro) when (erro.NaoEncontrado)
            {
                return StatusCode(503, new { erro = "A entidade do ChocoLog ainda não existe no Orion. Provisione em Configuração e ligue o ESP32: ela aparece com a primeira medida." });
            }
            catch (FiwareException erro)
            {
                return StatusCode(503, new { erro = erro.Message });
            }
            catch (Exception erro)
            {
                return StatusCode(500, new { erro = erro.Message });
            }
        }

        /// <summary>
        /// Histórico de uma grandeza para os gráficos.
        /// </summary>
        /// <param name="grandeza">temperature, humidity ou luminosity</param>
        /// <param name="quantidade">Quantidade de medidas (1 a 100, padrão 60)</param>
        /// <response code="200">Lista de pontos {data, valor}</response>
        /// <response code="400">Parâmetro inválido</response>
        [HttpGet("historico")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        public IActionResult Historico(string grandeza, int quantidade = 60)
        {
            try
            {
                if (!LeituraDAO.Grandezas.Contains(grandeza))
                    return BadRequest(new { erro = "Grandeza inválida. Use temperature, humidity ou luminosity." });
                if (quantidade < 1 || quantidade > 100)
                    return BadRequest(new { erro = "A quantidade deve estar entre 1 e 100." });

                var pontos = new LeituraDAO().Historico(grandeza, quantidade)
                    .Select(l => new { data = l.Data, valor = l.ValorNumerico });
                return Ok(pontos);
            }
            catch (FiwareException erro)
            {
                return StatusCode(503, new { erro = erro.Message });
            }
            catch (Exception erro)
            {
                return StatusCode(500, new { erro = erro.Message });
            }
        }
    }
}
