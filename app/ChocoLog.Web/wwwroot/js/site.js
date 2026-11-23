// mensagem rápida no canto da tela
function mostraMensagem(msg, erro) {
    var toast = $('#toast');
    toast.text(msg).toggleClass('erro', erro === true).addClass('visivel');
    clearTimeout(window.timerToast);
    window.timerToast = setTimeout(function () { toast.removeClass('visivel'); }, 4000);
}

// confirmação antes de excluir um registro dos CRUDs
function apagarRegistro(controller, id) {
    if (confirm('Confirma a exclusão do registro?'))
        location.href = '/' + controller + '/Delete?id=' + id;
}

function apagarAssinatura(id) {
    if (confirm('Excluir esta assinatura? Sem ela o histórico para de receber medidas.'))
        location.href = '/configuracao/DeleteAssinatura?id=' + id;
}

function restaurarLimites() {
    if (confirm('Apagar os limites do Orion e voltar aos valores do chocolog.ino?'))
        location.href = '/limite/Restaurar';
}

// formata número no padrão brasileiro (vírgula)
function formataNumero(valor, casas) {
    if (valor === null || valor === undefined) return '--';
    return Number(valor).toLocaleString('pt-BR', { minimumFractionDigits: casas, maximumFractionDigits: casas });
}

// limites aparecem como foram cadastrados: 15 ou 15,5 (nunca arredondados)
function formataLimite(valor) {
    return Number(valor).toLocaleString('pt-BR', { maximumFractionDigits: 1 });
}

// AJAX 1: dashboard em tempo real (valores atuais, médias e alerta)
var graficos = {};

// uma cor por grandeza; vermelho e verde ficam reservados para os alertas
var GRANDEZAS = {
    temperature: { campo: 'temperatura', canvas: 'graficoTemperatura', cor: '#C2552B', unidade: ' °C', casas: 1, min: 'tempMin', max: 'tempMax', card: 'Temperatura' },
    humidity: { campo: 'umidade', canvas: 'graficoUmidade', cor: '#2F6FD1', unidade: ' %', casas: 0, min: 'umidMin', max: 'umidMax', card: 'Umidade' },
    luminosity: { campo: 'luminosidade', canvas: 'graficoLuminosidade', cor: '#8E6A00', unidade: ' %', casas: 0, min: null, max: 'luzMax', card: 'Luminosidade' }
};

function carregaDashboard() {
    $.ajax({
        url: '/api/dashboard/atual',
        cache: false,
        success: function (r) {
            $('#atualizadoEm').text('Atualizado às ' + new Date().toLocaleTimeString('pt-BR'));

            Object.keys(GRANDEZAS).forEach(function (chave) {
                var g = GRANDEZAS[chave];
                var media = r.monitor.medias ? r.monitor.medias[g.campo] : null;
                $('#val' + g.card).text(formataNumero(r[g.campo], g.casas) + g.unidade);
                var limite = g.min ? formataLimite(r.limites[g.min]) + ' a ' + formataLimite(r.limites[g.max]) : 'até ' + formataLimite(r.limites[g.max]);
                $('#med' + g.card).text('Média da janela: ' + formataNumero(media, g.casas) + g.unidade + ' · limite: ' + limite + g.unidade);

                // card vermelho quando a média está fora do limite
                var fora = media !== null && media !== undefined && (media > r.limites[g.max] || (g.min && media < r.limites[g.min]));
                $('#card' + g.card).toggleClass('fora', fora === true);
            });

            if (r.monitor.erro) {
                mudaFaixa('aguardando', 'Monitor aguardando', r.monitor.erro);
            } else if (r.monitor.alerta === 'OK') {
                mudaFaixa('normal', 'Carga OK', 'Médias dentro dos limites. LED e buzzer desligados. ESP32 confirmou: ' + (r.alertaConfirmado || '--'));
            } else {
                mudaFaixa('alerta', r.monitor.descricao, r.monitor.motivos.join('; ') + '. Comando enviado: alert=' + r.monitor.alerta +
                    ' · ESP32 confirmou: ' + (r.alertaConfirmado || 'aguardando'));
            }

            Object.keys(GRANDEZAS).forEach(function (chave) { carregaGrafico(chave, r.limites); });
        },
        error: function (xhr) {
            var msg = xhr.responseJSON && xhr.responseJSON.erro ? xhr.responseJSON.erro : 'Aplicação sem resposta.';
            mudaFaixa('aguardando', 'Sem conexão com o FIWARE', msg);
            $('#atualizadoEm').text('Sem conexão');
        }
    });
}

function mudaFaixa(tipo, titulo, detalhe) {
    $('#faixaAlerta').removeClass('normal alerta aguardando').addClass(tipo);
    $('#alertaTitulo').text(titulo);
    $('#alertaDetalhe').text(detalhe);
}

// AJAX 2: histórico de cada grandeza (STH-Comet) para os gráficos
function carregaGrafico(chave, limites) {
    var g = GRANDEZAS[chave];
    $.ajax({
        url: '/api/dashboard/historico',
        data: { grandeza: chave, quantidade: 60 },
        cache: false,
        success: function (pontos) {
            var rotulos = pontos.map(function (p) { return new Date(p.data).toLocaleTimeString('pt-BR'); });
            var valores = pontos.map(function (p) { return p.valor; });
            var linhaMax = pontos.map(function () { return limites[g.max]; });
            var linhaMin = pontos.map(function () { return g.min ? limites[g.min] : null; });

            if (!graficos[chave]) {
                graficos[chave] = new Chart(document.getElementById(g.canvas), {
                    type: 'line',
                    data: {
                        labels: rotulos,
                        datasets: [
                            { label: 'Medida', data: valores, borderColor: g.cor, backgroundColor: g.cor + '1A', borderWidth: 2, fill: true, tension: 0.25, pointRadius: 0 },
                            { label: 'Limite máximo', data: linhaMax, borderColor: '#999', borderWidth: 1, borderDash: [4, 4], pointRadius: 0, fill: false },
                            { label: 'Limite mínimo', data: linhaMin, borderColor: '#999', borderWidth: 1, borderDash: [4, 4], pointRadius: 0, fill: false }
                        ]
                    },
                    options: {
                        animation: false,
                        maintainAspectRatio: false,
                        interaction: { mode: 'index', intersect: false },
                        plugins: { legend: { display: false } },
                        scales: { x: { ticks: { maxTicksLimit: 6 }, grid: { display: false } } }
                    }
                });
            } else {
                var grafico = graficos[chave];
                grafico.data.labels = rotulos;
                grafico.data.datasets[0].data = valores;
                grafico.data.datasets[1].data = linhaMax;
                grafico.data.datasets[2].data = linhaMin;
                grafico.update();
            }
        }
    });
}

// AJAX 3: consulta de mensagens IoT (o servidor devolve a tabela pronta)
function aplicaFiltroConsultaMensagens() {
    $.ajax({
        url: '/mensagem/ObtemDadosConsultaMensagens',
        data: {
            grandeza: $('#grandeza').val(),
            quantidade: $('#quantidade').val(),
            dataInicial: $('#dataInicial').val(),
            dataFinal: $('#dataFinal').val()
        },
        success: function (dados) {
            if (dados.erro != undefined)
                alert(dados.msg);
            else
                $('#resultadoConsulta').html(dados);
        }
    });
}

// AJAX 4: testa a conexão com o Orion, o IoT Agent e o STH-Comet
function testaConexao() {
    mostraMensagem('Testando conexão...', false);
    $.ajax({
        url: '/configuracao/TestaConexao',
        cache: false,
        success: function (dados) {
            mostraMensagem(dados.msg, dados.erro);
        }
    });
}
