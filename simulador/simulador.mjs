// Simulador do ChocoLog: imita o FIWARE e o ESP32 no próprio PC, para testar a aplicação sem a AWS e sem hardware.
//
//   Orion      http://localhost:1026   entidades, comandos e assinaturas (só o que a aplicação usa)
//   IoT Agent  http://localhost:4041   service groups e devices
//   STH-Comet  http://localhost:8666   histórico, alimentado pela assinatura, como no FIWARE real
//   ESP32      http://localhost:8090   página do device virtual: sensores, LED azul e buzzer
//
// Uso: node simulador/simulador.mjs   (Node 20 ou mais novo, sem dependências)
// Não testa: MQTT, o firmware de verdade e as portas da AWS.

import http from 'node:http';

const PORTS = { orion: 1026, iota: 4041, sth: 8666, device: 8090 };
const FORBIDDEN = /[<>"'=;()]/; // o Orion recusa esses caracteres nos valores de texto

const entities = new Map(); // id -> { id, type, attrs: { name: { type, value } } }
const subscriptions = new Map();
const services = [];
const devices = new Map();
const history = new Map(); // "id|attr" -> [{ recvTime, attrValue }]
const log = [];

const now = () => new Date().toISOString();
const note = (text) => {
  log.unshift({ at: new Date().toLocaleTimeString('pt-BR'), text });
  log.length = Math.min(log.length, 40);
};

// Utilidades HTTP

function send(res, status, body) {
  res.writeHead(status, {
    'Content-Type': 'application/json',
    'Access-Control-Allow-Origin': '*',
    'Access-Control-Allow-Headers': '*',
    'Access-Control-Allow-Methods': 'GET,POST,PATCH,PUT,DELETE,OPTIONS',
  });
  res.end(body === undefined ? '' : JSON.stringify(body));
}

const readJson = (req) =>
  new Promise((resolve) => {
    let raw = '';
    req.on('data', (c) => (raw += c));
    req.on('end', () => {
      try { resolve(raw ? JSON.parse(raw) : {}); } catch { resolve(Symbol.for('invalid')); }
    });
  });

function serve(port, name, handler) {
  http.createServer(async (req, res) => {
    if (req.method === 'OPTIONS') return send(res, 204);
    const url = new URL(req.url, 'http://x');
    try {
      await handler(req, res, url);
    } catch (e) {
      send(res, 500, { error: 'InternalError', description: String(e.message || e) });
    }
  }).on('error', (e) => {
    console.error(`\nNão deu para abrir a porta ${port} (${name}): ${e.code}. Feche o outro programa que usa essa porta.`);
    process.exit(1);
  }).listen(port, '0.0.0.0');
}

// Orion

function keyValues(entity, attrList) {
  const out = { id: entity.id, type: entity.type };
  for (const [k, a] of Object.entries(entity.attrs)) {
    if (!attrList || attrList.includes(k)) out[k] = a.value;
  }
  return out;
}

function invalidValue(attrs) {
  return Object.values(attrs).some((a) => typeof a?.value === 'string' && FORBIDDEN.test(a.value));
}

// Medida nova numa entidade: atualiza e notifica as assinaturas (o STH-Comet guarda)
function updateEntityAttrs(entity, attrs) {
  const changed = [];
  for (const [k, a] of Object.entries(attrs)) {
    entity.attrs[k] = { type: a.type ?? entity.attrs[k]?.type ?? 'Text', value: a.value };
    changed.push(k);
  }
  entity.attrs.TimeInstant = { type: 'DateTime', value: now() };
  for (const sub of subscriptions.values()) {
    const matches = sub.subject.entities.some((e) => e.id === entity.id);
    const cond = sub.subject.condition?.attrs ?? [];
    if (!matches || !changed.some((c) => cond.length === 0 || cond.includes(c))) continue;
    if (!String(sub.notification.httpCustom?.url ?? '').includes('sth-comet')) continue;
    for (const attr of sub.notification.attrs ?? cond) {
      if (!(attr in entity.attrs)) continue;
      const key = `${entity.id}|${attr}`;
      if (!history.has(key)) history.set(key, []);
      const list = history.get(key);
      list.push({ recvTime: now(), attrValue: String(entity.attrs[attr].value) });
      if (list.length > 5000) list.shift();
    }
    sub.notification.lastNotification = now();
    sub.notification.timesSent = (sub.notification.timesSent ?? 0) + 1;
  }
}

serve(PORTS.orion, 'Orion', async (req, res, url) => {
  const path = url.pathname;
  if (path === '/version') return send(res, 200, { orion: { version: '4.5.0 (simulador)' } });

  if (path === '/v2/entities') {
    if (req.method === 'GET') {
      const type = url.searchParams.get('type');
      return send(res, 200, [...entities.values()].filter((e) => !type || e.type === type).map((e) => keyValues(e)));
    }
    if (req.method === 'POST') {
      const body = await readJson(req);
      if (typeof body !== 'object' || !body.id || !body.type) return send(res, 400, { error: 'BadRequest', description: 'entity id and type are required' });
      const { id, type, ...attrs } = body;
      if (invalidValue(attrs)) return send(res, 400, { error: 'BadRequest', description: 'Invalid characters in attribute value' });
      const upsert = url.searchParams.get('options') === 'upsert';
      if (entities.has(id) && !upsert) return send(res, 422, { error: 'Unprocessable', description: 'Already Exists' });
      const entity = entities.get(id) ?? { id, type, attrs: {} };
      entities.set(id, entity);
      for (const [k, a] of Object.entries(attrs)) entity.attrs[k] = { type: a.type, value: a.value };
      note(`Orion: ${upsert ? 'upsert' : 'criou'} ${id}`);
      return send(res, upsert ? 204 : 201);
    }
  }

  const m = path.match(/^\/v2\/entities\/([^/]+)(\/attrs)?$/);
  if (m) {
    const id = decodeURIComponent(m[1]);
    const entity = entities.get(id);
    if (!entity) return send(res, 404, { error: 'NotFound', description: 'The requested entity has not been found. Check type and id' });
    if (req.method === 'GET' && !m[2]) {
      const attrs = url.searchParams.get('attrs')?.split(',');
      return send(res, 200, keyValues(entity, attrs));
    }
    if (req.method === 'DELETE' && !m[2]) {
      entities.delete(id);
      note(`Orion: apagou ${id}`);
      return send(res, 204);
    }
    if (req.method === 'PATCH' && m[2]) {
      const body = await readJson(req);
      if (typeof body !== 'object') return send(res, 400, { error: 'ParseError', description: 'Errors found in incoming JSON buffer' });
      if (invalidValue(body)) return send(res, 400, { error: 'BadRequest', description: 'Invalid characters in attribute value' });
      const device = [...devices.values()].find((d) => d.entity_name === id);
      const commands = device?.commands?.map((c) => c.name) ?? [];
      const missing = Object.keys(body).filter((k) => !(k in entity.attrs) && !commands.includes(k));
      if (missing.length) {
        return send(res, 422, { error: 'Unprocessable', description: `one or more of the attributes in the request do not exist: ${missing.join(', ')}` });
      }
      for (const [k, a] of Object.entries(body)) {
        if (commands.includes(k)) deviceCommand(entity, k, a.value);
        else entity.attrs[k] = { type: a.type ?? entity.attrs[k].type, value: a.value };
      }
      return send(res, 204);
    }
  }

  if (path === '/v2/subscriptions') {
    if (req.method === 'GET') return send(res, 200, [...subscriptions.values()]);
    if (req.method === 'POST') {
      const body = await readJson(req);
      const id = Math.random().toString(16).slice(2, 14).padEnd(12, '0') + Date.now().toString(16).slice(-12);
      subscriptions.set(id, { id, status: 'active', ...body });
      note(`Orion: assinatura criada (${(body.subject?.condition?.attrs ?? []).join(', ')})`);
      res.setHeader('Location', `/v2/subscriptions/${id}`);
      return send(res, 201);
    }
  }
  const s = path.match(/^\/v2\/subscriptions\/([^/]+)$/);
  if (s && req.method === 'DELETE') {
    if (!subscriptions.delete(s[1])) return send(res, 404, { error: 'NotFound', description: 'The requested subscription has not been found. Check id' });
    note('Orion: assinatura apagada');
    return send(res, 204);
  }

  send(res, 400, { error: 'BadRequest', description: 'service not found (simulador)' });
});

// IoT Agent

serve(PORTS.iota, 'IoT Agent', async (req, res, url) => {
  const path = url.pathname;
  if (path === '/iot/about') return send(res, 200, { libVersion: '4.7.0', port: '4041', baseRoot: '/', version: '3.7.0 (simulador)' });

  if (path === '/iot/services' || path === '/iot/services/') {
    if (req.method === 'GET') return send(res, 200, { count: services.length, services });
    if (req.method === 'POST') {
      const body = await readJson(req);
      for (const svc of body.services ?? []) {
        if (services.some((x) => x.apikey === svc.apikey && x.resource === svc.resource)) {
          return send(res, 409, { name: 'DUPLICATE_GROUP', message: `A device configuration already exists for resource ${svc.resource} and API Key ${svc.apikey}` });
        }
        services.push(svc);
        note(`IoT Agent: service group apikey ${svc.apikey}`);
      }
      return send(res, 201, {});
    }
  }

  if (path === '/iot/devices') {
    if (req.method === 'GET') return send(res, 200, { count: devices.size, devices: [...devices.values()] });
    if (req.method === 'POST') {
      const body = await readJson(req);
      for (const d of body.devices ?? []) {
        if (devices.has(d.device_id)) return send(res, 409, { name: 'DUPLICATE_DEVICE_ID', message: `Duplicate device id: ${d.device_id}` });
        if (!services.some((x) => x.apikey === d.apikey)) {
          return send(res, 400, { name: 'BAD_REQUEST', message: `No service group with apikey ${d.apikey}. Create the service group first.` });
        }
        devices.set(d.device_id, d);
        // Como o IoT Agent real: a entidade aparece no Orion com os atributos estáticos e o status do comando
        const attrs = {};
        for (const st of d.static_attributes ?? []) attrs[st.name] = { type: st.type, value: st.value };
        for (const c of d.commands ?? []) {
          attrs[`${c.name}_status`] = { type: 'commandStatus', value: 'UNKNOWN' };
          attrs[`${c.name}_info`] = { type: 'commandResult', value: ' ' };
        }
        entities.set(d.entity_name, { id: d.entity_name, type: d.entity_type, attrs });
        note(`IoT Agent: device ${d.device_id} -> ${d.entity_name}`);
      }
      return send(res, 201, {});
    }
  }

  const m = path.match(/^\/iot\/devices\/([^/]+)$/);
  if (m) {
    const id = decodeURIComponent(m[1]);
    const device = devices.get(id);
    if (!device) return send(res, 404, { name: 'DEVICE_NOT_FOUND', message: `No device was found with id:${id}` });
    if (req.method === 'GET') return send(res, 200, device);
    if (req.method === 'PUT') {
      const body = await readJson(req);
      if (body.static_attributes) {
        device.static_attributes = body.static_attributes;
        const entity = entities.get(device.entity_name);
        if (entity) for (const st of body.static_attributes) entity.attrs[st.name] = { type: st.type, value: st.value };
      }
      note(`IoT Agent: device ${id} atualizado`);
      return send(res, 204);
    }
    if (req.method === 'DELETE') {
      devices.delete(id);
      note(`IoT Agent: device ${id} removido`);
      return send(res, 204);
    }
  }
  send(res, 404, { name: 'NOT_FOUND', message: 'Route not found (simulador)' });
});

// STH-Comet

serve(PORTS.sth, 'STH-Comet', async (req, res, url) => {
  if (url.pathname === '/version') return send(res, 200, { version: '2.11.0 (simulador)' });
  const m = url.pathname.match(/^\/STH\/v1\/contextEntities\/type\/([^/]+)\/id\/([^/]+)\/attributes\/([^/]+)$/);
  if (!m) return send(res, 404, { error: 'NotFound' });
  const [, type, rawId, attr] = m;
  const id = decodeURIComponent(rawId);
  const q = url.searchParams;
  const lastN = Number(q.get('lastN') || 0);
  const hLimit = Number(q.get('hLimit') || 0);
  if (!lastN && !hLimit) {
    return send(res, 400, { statusCode: 400, error: 'Bad Request', message: 'A combination of the following query params is required: lastN, hLimit and hOffset' });
  }
  const from = q.get('dateFrom') ? new Date(q.get('dateFrom')) : null;
  const to = q.get('dateTo') ? new Date(q.get('dateTo')) : null;
  let values = (history.get(`${id}|${attr}`) ?? []).filter(
    (v) => (!from || new Date(v.recvTime) >= from) && (!to || new Date(v.recvTime) <= to));
  values = lastN ? values.slice(-lastN) : values.slice(Number(q.get('hOffset') || 0), Number(q.get('hOffset') || 0) + hLimit);
  send(res, 200, {
    contextResponses: [{
      contextElement: { attributes: [{ name: attr, values }], id, isPattern: false, type },
      statusCode: { code: '200', reasonPhrase: 'OK' },
    }],
  });
});

// ESP32 virtual

const sensors = { temperature: 18.5, humidity: 52, luminosity: 12 };
const manual = { temperature: null, humidity: null, luminosity: null }; // valores fixados na página
const deviceState = { alert: 'OK' };
const clamp = (v, a, b) => Math.max(a, Math.min(b, v));
const walk = (v, step, a, b) => clamp(v + (Math.random() - 0.5) * step, a, b);

function provisionedDevice() {
  return [...devices.values()].find((d) => services.some((s) => s.apikey === d.apikey));
}

// A cada 5 s, como o firmware: lê os "sensores" e publica t, h, l
setInterval(() => {
  sensors.temperature = manual.temperature ?? walk(sensors.temperature, 0.6, 16, 21);
  sensors.humidity = manual.humidity ?? walk(sensors.humidity, 2, 47, 58);
  sensors.luminosity = manual.luminosity ?? walk(sensors.luminosity, 4, 3, 30);
  const d = provisionedDevice();
  const entity = d && entities.get(d.entity_name);
  if (!entity) return;
  updateEntityAttrs(entity, {
    temperature: { type: 'Number', value: Math.round(sensors.temperature * 10) / 10 },
    humidity: { type: 'Number', value: Math.round(sensors.humidity) },
    luminosity: { type: 'Number', value: Math.round(sensors.luminosity) },
  });
}, 5000);

// Comando chegando ao ESP32: fica PENDING, o device obedece e confirma (como o cmdexe do MQTT)
function deviceCommand(entity, command, value) {
  entity.attrs[`${command}_status`] = { type: 'commandStatus', value: 'PENDING' };
  note(`MQTT -> ESP32: ${command}|${value}`);
  setTimeout(() => {
    const v = String(value).toUpperCase();
    const ok = command === 'alert' && ['OK', 'T', 'U', 'L'].includes(v);
    if (ok) deviceState.alert = v;
    entity.attrs[`${command}_status`] = { type: 'commandStatus', value: 'OK' };
    entity.attrs[`${command}_info`] = { type: 'commandResult', value: ok ? v : 'valor invalido' };
    if (ok) updateEntityAttrs(entity, { alertState: { type: 'Text', value: v } });
    note(`ESP32 -> cmdexe: ${command}|${ok ? v : 'valor invalido'}`);
  }, 300);
}

const PATTERNS = {
  OK: { label: 'Carga OK', blink: 0, hz: 0 },
  T: { label: 'ALERTA: TEMPERATURA', blink: 125, hz: 2500 },
  U: { label: 'ALERTA: UMIDADE', blink: 250, hz: 1200 },
  L: { label: 'ALERTA: LUMINOSIDADE', blink: 500, hz: 600 },
};

serve(PORTS.device, 'ESP32 virtual', async (req, res, url) => {
  if (url.pathname === '/estado') {
    const d = provisionedDevice();
    return send(res, 200, { sensors, manual, alert: deviceState.alert, pattern: PATTERNS[deviceState.alert], provisioned: Boolean(d), log });
  }
  if (url.pathname === '/fixar' && req.method === 'POST') {
    const body = await readJson(req);
    for (const k of Object.keys(manual)) if (k in body) manual[k] = body[k] === null ? null : Number(body[k]);
    note('Sensores: ' + Object.entries(manual).map(([k, v]) => `${k}=${v ?? 'auto'}`).join(', '));
    return send(res, 200, { manual });
  }
  res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
  res.end(DEVICE_PAGE);
});

console.log(`
  Simulador do ChocoLog rodando (Ctrl+C para parar)

  Orion ........ http://localhost:${PORTS.orion}
  IoT Agent .... http://localhost:${PORTS.iota}
  STH-Comet .... http://localhost:${PORTS.sth}
  ESP32 virtual  http://localhost:${PORTS.device}

  Na aplicação, use o servidor "localhost" e clique em "Provisionar tudo" (Configuração).
`);

const DEVICE_PAGE = `<!doctype html><html lang="pt-BR"><head><meta charset="utf-8">
<meta name="viewport" content="width=device-width,initial-scale=1"><title>ESP32 virtual · ChocoLog</title>
<style>
  body{margin:0;font:15px/1.5 "Fira Sans",system-ui,sans-serif;background:#faf6f1;color:#2a1d16}
  header{background:#4a2c1d;color:#fff;padding:14px 20px;font-weight:700}
  main{max-width:980px;margin:0 auto;padding:20px;display:grid;gap:16px;grid-template-columns:1fr 1fr}
  @media(max-width:760px){main{grid-template-columns:1fr}}
  .card{background:#fff;border:1px solid #eadfd5;border-radius:14px;padding:16px}
  h2{font-size:13px;text-transform:uppercase;letter-spacing:1px;color:#6f6259;margin:0 0 12px}
  .tft{background:#111;color:#fff;border-radius:10px;padding:12px;font-family:Consolas,monospace}
  .tft .row{display:flex;justify-content:space-between;padding:4px 0;font-size:18px}
  .tft .foot{margin-top:8px;padding:8px;border-radius:6px;text-align:center;font-weight:700}
  .leds{display:flex;gap:24px;align-items:center;margin-top:14px}
  .led{width:46px;height:46px;border-radius:50%;background:#1d2a44;border:2px solid #33415a}
  .led.on{background:#3b82f6;box-shadow:0 0 22px 6px #3b82f6aa}
  .buzz{font-weight:700}
  label{display:block;font-size:13px;color:#6f6259;margin-top:10px}
  input[type=range]{width:100%}
  .presets{display:flex;flex-wrap:wrap;gap:8px;margin-top:12px}
  button{border:1px solid #6b3f27;background:#fff;color:#6b3f27;border-radius:8px;padding:6px 10px;cursor:pointer;font:inherit;font-size:13px}
  button:hover{background:#6b3f27;color:#fff}
  ul{list-style:none;margin:0;padding:0;font:13px Consolas,monospace;max-height:340px;overflow:auto}
  li{padding:4px 0;border-top:1px solid #f1e8df} li:first-child{border-top:0}
  .muted{color:#6f6259;font-size:13px}
</style></head><body>
<header>ESP32 virtual · ChocoLog</header>
<main>
  <section class="card">
    <h2>Display e alertas</h2>
    <div class="tft">
      <div class="row"><span>Temperatura</span><span id="t">--</span></div>
      <div class="row"><span>Umidade</span><span id="h">--</span></div>
      <div class="row"><span>Luz</span><span id="l">--</span></div>
      <div class="foot" id="foot">--</div>
    </div>
    <div class="leds"><div class="led" id="led" aria-label="LED azul"></div><div><div class="buzz" id="buzz">Buzzer mudo</div><div class="muted" id="prov"></div></div></div>
  </section>
  <section class="card">
    <h2>Sensores (fixe um valor para provocar o alerta)</h2>
    <label>Temperatura: <strong id="vt"></strong> °C</label><input type="range" id="rt" min="0" max="50" step="0.5">
    <label>Umidade: <strong id="vh"></strong> %</label><input type="range" id="rh" min="20" max="90">
    <label>Luminosidade: <strong id="vl"></strong> %</label><input type="range" id="rl" min="0" max="100">
    <div class="presets">
      <button data-p='{"temperature":30}'>Caminhão quente (30 °C)</button>
      <button data-p='{"humidity":75}'>Umidade alta (75 %)</button>
      <button data-p='{"luminosity":85}'>Caixa aberta (luz 85 %)</button>
      <button data-p='{"temperature":30,"humidity":75,"luminosity":85}'>Tudo fora</button>
      <button data-p='{"temperature":null,"humidity":null,"luminosity":null}'>Voltar ao normal</button>
    </div>
    <p class="muted">O alerta só muda quando a <b>média</b> da janela sai do limite (até ~1 min), como na aplicação real.</p>
  </section>
  <section class="card" style="grid-column:1/-1"><h2>Mensagens</h2><ul id="log"></ul></section>
</main>
<script>
  let blinkTimer = null, blinkOn = false, current = null, dragging = false;
  function blink(p) {
    const key = p.blink;
    if (current === key) return;
    current = key; clearInterval(blinkTimer); blinkOn = false;
    document.getElementById('led').classList.remove('on');
    if (p.blink) blinkTimer = setInterval(() => {
      blinkOn = !blinkOn;
      document.getElementById('led').classList.toggle('on', blinkOn);
      document.getElementById('buzz').textContent = blinkOn ? 'Buzzer: ' + p.hz + ' Hz' : 'Buzzer: ...';
    }, p.blink);
    else document.getElementById('buzz').textContent = 'Buzzer mudo';
  }
  async function tick() {
    const s = await (await fetch('/estado')).json();
    const f = (v, d) => Number(v).toFixed(d).replace('.', ',');
    document.getElementById('t').textContent = f(s.sensors.temperature, 1) + ' C';
    document.getElementById('h').textContent = f(s.sensors.humidity, 0) + ' %';
    document.getElementById('l').textContent = f(s.sensors.luminosity, 0) + ' %';
    const foot = document.getElementById('foot');
    foot.textContent = s.pattern.label; foot.style.background = s.alert === 'OK' ? '#1f8a4c' : '#c62828';
    document.getElementById('prov').textContent = s.provisioned ? 'Device provisionado: publicando a cada 5 s' : 'Aguardando provisionamento (aplicação > Configuração)';
    blink(s.pattern);
    if (!dragging) for (const [k, id] of [['temperature','t'],['humidity','h'],['luminosity','l']]) {
      document.getElementById('r' + id).value = s.manual[k] ?? s.sensors[k];
      document.getElementById('v' + id).textContent = s.manual[k] === null ? f(s.sensors[k], 1) + ' (auto)' : s.manual[k];
    }
    document.getElementById('log').innerHTML = s.log.map(l => '<li>' + l.at + '  ' + l.text.replace(/</g,'&lt;') + '</li>').join('');
  }
  const fixar = (body) => fetch('/fixar', { method: 'POST', body: JSON.stringify(body) }).then(tick);
  for (const [k, id] of [['temperature','t'],['humidity','h'],['luminosity','l']]) {
    const r = document.getElementById('r' + id);
    r.addEventListener('input', () => { dragging = true; document.getElementById('v' + id).textContent = r.value; });
    r.addEventListener('change', () => { dragging = false; fixar({ [k]: Number(r.value) }); });
  }
  document.querySelectorAll('[data-p]').forEach(b => b.addEventListener('click', () => fixar(JSON.parse(b.dataset.p))));
  tick(); setInterval(tick, 700);
</script></body></html>`;
