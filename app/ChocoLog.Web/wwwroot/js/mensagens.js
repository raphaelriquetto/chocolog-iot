// Consulta das mensagens IoT por Ajax: chama /api/mensagens e monta a tabela sem recarregar a página.

const NAMES = {
  temperature: ['Temperatura', '°C', 'text-bg-warning'],
  humidity: ['Umidade', '%', 'text-bg-info'],
  luminosity: ['Luminosidade', '%', 'text-bg-secondary'],
  alertState: ['Alerta', '', 'text-bg-danger'],
};
const ALERTS = { OK: 'OK (normal)', T: 'Temperatura', U: 'Umidade', L: 'Luminosidade' };

const form = document.getElementById('filtro');
const erro = document.getElementById('filtro-erro');
const linhas = document.getElementById('linhas');
const resumo = document.getElementById('resumo');
let timer = null;

function escapeHtml(s) {
  return String(s).replace(/[&<>"']/g, (c) => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' })[c]);
}

function formatValue(attr, value) {
  if (attr === 'alertState') return ALERTS[value] || value;
  const n = Number(value);
  return isNaN(n) ? value : n.toLocaleString('pt-BR', { maximumFractionDigits: 1 }) + ' ' + NAMES[attr][1];
}

async function consultar() {
  erro.textContent = '';
  const de = form.de.value, ate = form.ate.value;
  if (de && ate && de > ate) {
    erro.textContent = 'A data "De" precisa ser antes da data "Até".';
    return;
  }
  const params = new URLSearchParams({ attr: form.attr.value, quantidade: form.quantidade.value });
  if (de) params.set('de', new Date(de).toISOString());
  if (ate) params.set('ate', new Date(ate).toISOString());

  resumo.textContent = 'Consultando…';
  try {
    const res = await fetch('/api/mensagens?' + params, { cache: 'no-store' });
    const data = await res.json();
    if (!data.ok) throw new Error(data.error);

    linhas.innerHTML = data.items.length
      ? data.items.map((m) => {
          const [name, , badge] = NAMES[m.attribute] || [m.attribute, '', 'text-bg-light'];
          return `<tr>
            <td>${new Date(m.time).toLocaleString('pt-BR')}</td>
            <td><span class="badge badge-attr ${badge}">${name}</span></td>
            <td class="text-end fw-semibold">${escapeHtml(formatValue(m.attribute, m.value))}</td>
          </tr>`;
        }).join('')
      : '<tr><td colspan="3" class="text-muted text-center py-4">Nenhuma mensagem nesse filtro.</td></tr>';
    resumo.textContent = `${data.items.length} mensagens · consultado às ${new Date().toLocaleTimeString('pt-BR')}`;
  } catch (e) {
    resumo.textContent = 'Falha na consulta';
    linhas.innerHTML = `<tr><td colspan="3" class="text-danger py-4">${escapeHtml(e.message)}</td></tr>`;
  }
}

form.addEventListener('submit', (e) => { e.preventDefault(); consultar(); });
document.getElementById('limpar').addEventListener('click', () => { form.reset(); consultar(); });
document.getElementById('auto').addEventListener('change', (e) => {
  clearInterval(timer);
  if (e.target.checked) timer = setInterval(consultar, 5000);
});
consultar();
