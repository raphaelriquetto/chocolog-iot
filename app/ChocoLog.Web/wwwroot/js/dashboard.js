// Dashboard em tempo real: a cada 5 s busca /api/dashboard (Ajax) e atualiza números e gráficos sem recarregar a página.

const REFRESH_MS = 5000;

// Uma cor por grandeza, validadas para contraste e daltonismo; vermelho/âmbar/verde ficam reservados para status
const SERIES = {
  temperature: { color: '#C2552B', unit: '°C', min: 'tempMin', max: 'tempMax', digits: 1 },
  humidity: { color: '#2F6FD1', unit: '%', min: 'humMin', max: 'humMax', digits: 0 },
  luminosity: { color: '#8E6A00', unit: '%', min: null, max: 'lumMax', digits: 0 },
};

const charts = {};
const hhmmss = (iso) => new Date(iso).toLocaleTimeString('pt-BR');
const fmt = (v, digits) => (v === null || v === undefined || v === '' || isNaN(Number(v)) ? '--' : Number(v).toFixed(digits).replace('.', ','));

function createChart(key) {
  const s = SERIES[key];
  const limitLine = (label) => ({
    label, data: [], borderColor: '#8a7d74', borderWidth: 1, borderDash: [4, 4], pointRadius: 0, fill: false,
  });
  charts[key] = new Chart(document.getElementById('chart-' + key), {
    type: 'line',
    data: {
      labels: [],
      datasets: [
        { label: 'Medida', data: [], borderColor: s.color, backgroundColor: s.color + '1A', borderWidth: 2, fill: true,
          tension: 0.25, pointRadius: 0, pointHoverRadius: 4 },
        limitLine('Limite máximo'),
        ...(s.min ? [limitLine('Limite mínimo')] : []),
      ],
    },
    options: {
      animation: false,
      maintainAspectRatio: false,
      interaction: { mode: 'index', intersect: false }, // a dica mostra o ponto mais próximo do mouse no eixo X
      plugins: {
        legend: { display: false },
        tooltip: { callbacks: { label: (c) => `${c.dataset.label}: ${fmt(c.parsed.y, s.digits)} ${s.unit}` } },
      },
      scales: {
        x: { ticks: { maxTicksLimit: 6, color: '#6f6259' }, grid: { display: false } },
        y: { ticks: { color: '#6f6259' }, grid: { color: '#efe7df' } },
      },
    },
  });
}

function updateChart(key, points, limits) {
  const chart = charts[key];
  const s = SERIES[key];
  chart.data.labels = points.map((p) => hhmmss(p.t));
  chart.data.datasets[0].data = points.map((p) => p.v);
  chart.data.datasets[1].data = points.map(() => limits[s.max]);
  if (s.min) chart.data.datasets[2].data = points.map(() => limits[s.min]);
  chart.update();
}

function setBanner(kind, title, detail) {
  const icons = { ok: 'bi-check-circle-fill', alert: 'bi-exclamation-octagon-fill', unknown: 'bi-hourglass-split' };
  document.getElementById('banner').className = 'status-banner mb-4 ' + kind;
  document.getElementById('banner-icon').className = 'bi icon ' + icons[kind];
  document.getElementById('banner-title').textContent = title;
  document.getElementById('banner-detail').textContent = detail;
}

function setLive(ok, text) {
  document.getElementById('live-dot').classList.toggle('off', !ok);
  document.getElementById('live-text').textContent = text;
}

async function refresh() {
  let data;
  try {
    const res = await fetch('/api/dashboard?points=60', { cache: 'no-store' });
    data = await res.json();
  } catch {
    setLive(false, 'Aplicação sem resposta');
    return;
  }
  if (!data.ok) {
    setLive(false, data.error);
    setBanner('unknown', 'Sem conexão com o FIWARE', data.error);
    return;
  }

  const { current, limits, monitor, history } = data;
  setLive(true, 'Ao vivo · atualizado às ' + new Date().toLocaleTimeString('pt-BR'));

  for (const key of Object.keys(SERIES)) {
    const s = SERIES[key];
    const avg = monitor.averages ? monitor.averages[key] : null;
    document.getElementById('val-' + key).textContent = fmt(current[key], s.digits);
    document.getElementById('avg-' + key).textContent = avg === null || avg === undefined ? '--' : fmt(avg, s.digits) + ' ' + s.unit;
    document.getElementById('lim-' + key).textContent = s.min
      ? `${fmt(limits[s.min], 0)} a ${fmt(limits[s.max], 0)} ${s.unit}`
      : `até ${fmt(limits[s.max], 0)} ${s.unit}`;
    // Cartão vermelho quando a média está fora do limite
    const out = avg !== null && avg !== undefined && (avg > limits[s.max] || (s.min && avg < limits[s.min]));
    document.getElementById('card-' + key).classList.toggle('out', Boolean(out));
    updateChart(key, history[key] || [], limits);
  }

  // Faixa de status: decisão do monitor + o que o ESP32 confirmou
  if (monitor.error) {
    setBanner('unknown', 'Monitor aguardando', monitor.error);
  } else if (monitor.desired === 'OK') {
    setBanner('ok', 'Carga OK', `Médias dentro dos limites. LED e buzzer desligados. ESP32 confirmou: ${current.alertState || '--'}`);
  } else {
    setBanner('alert', monitor.desiredLabel, `${monitor.reasons.join('; ')}. Comando enviado: alert=${monitor.desired} · ESP32 confirmou: ${current.alertState || 'aguardando'}`);
  }

  const info = [];
  if (monitor.averages) info.push(`janela: ${limits.window} medidas`);
  if (monitor.commandSentAt) info.push('último comando às ' + hhmmss(monitor.commandSentAt));
  document.getElementById('monitor-info').textContent = info.join(' · ');
}

Object.keys(SERIES).forEach(createChart);
refresh();
setInterval(refresh, REFRESH_MS);
