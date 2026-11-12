// Prueba de carga del masivo «Crear trámites» desde el diagnóstico (Punto 4, ADR-0084, diseño §9).
// Guion completo, datos y umbrales: docs/diagnosticos/prueba-carga-derivacion.md. Solo QA, datos sintéticos, nunca PDN.
//
// Uso:
//   DGN_BASE_URL=https://qa.<dominio>/api/v1/diagnostico ESCENARIO=E1 DGN_COMPANIAS_JSON=/fuera/del/repo/companias.json \
//     k6 run tests/load/diagnostico-derivacion.js
// El JSON trae, por compañía, un token de usuario y los lotes (ver la guía). El script no imprime tokens ni placas.
import http from 'k6/http';
import { check, sleep } from 'k6';
import { Counter, Rate, Trend } from 'k6/metrics';

const BASE = __ENV.DGN_BASE_URL;
const TRAMITES = __ENV.TRAMITES_BASE_URL || '';
const ESCENARIO = (__ENV.ESCENARIO || 'E1').toUpperCase();
const RUIDO = (__ENV.TRAMITES_RUIDO_PATHS || '/instances/search').split(',');
const COMPANIAS = JSON.parse(open(__ENV.DGN_COMPANIAS_JSON));
const TOPE_LOTE_MS = Number(__ENV.TOPE_LOTE_MS || 3 * 60 * 60 * 1000);

const creados = new Counter('derivacion_creados');
const omitidos = new Counter('derivacion_omitidos');
const fallidos = new Counter('derivacion_fallidos');
const loteMs = new Trend('derivacion_lote_ms', true);
const lotePequenoMs = new Trend('derivacion_lote_pequeno_ms', true);
const errores = new Rate('derivacion_errores_http');

const porRol = (rol) => COMPANIAS.filter((c) => (c.rol || 'base') === rol);

function escenarios() {
  const base = {
    masivo: { executor: 'per-vu-iterations', vus: porRol('base').length, iterations: 1, maxDuration: '4h', exec: 'masivoBase' },
  };
  if (ESCENARIO === 'E2') {
    base.ballena = { executor: 'per-vu-iterations', vus: Math.max(1, porRol('ballena').length), iterations: 1, maxDuration: '4h', exec: 'masivoBallena' };
    base.pequenas = {
      executor: 'constant-arrival-rate', rate: Math.max(1, porRol('pequena').length), timeUnit: '2m', duration: '40m',
      preAllocatedVUs: 6, maxVUs: 12, exec: 'masivoPequena', startTime: '1m',
    };
  }
  if (ESCENARIO === 'E4') {
    base.ruido = { executor: 'constant-vus', vus: 30, duration: '40m', exec: 'ruidoTramites', startTime: '30s' };
  }
  return base;
}

export const options = {
  scenarios: escenarios(),
  thresholds: {
    derivacion_errores_http: ['rate<0.005'],
    derivacion_lote_pequeno_ms: ['max<180000'],
    'http_req_duration{grupo:tramites}': ['p(95)<1500'],
  },
};

const cabeceras = (token) => ({ headers: { Authorization: `Bearer ${token}`, 'Content-Type': 'application/json' } });

function elegibles(compania, lote) {
  const r = http.get(`${BASE}/diagnosticos/${lote}/grilla?desde=0`, { ...cabeceras(compania.token), tags: { grupo: 'diagnostico' } });
  errores.add(r.status >= 500);
  if (!check(r, { 'grilla 200': (x) => x.status === 200 })) return [];
  return (r.json('vehiculos') || []).filter((v) => v.derivacion && v.derivacion.elegible).map((v) => v.id);
}

function derivarLote(compania, lote, tendencia) {
  const ids = elegibles(compania, lote);
  if (ids.length === 0) return;
  const inicio = Date.now();
  const r = http.post(`${BASE}/diagnosticos/${lote}/tramites`, JSON.stringify({ vehiculo_ids: ids }), { ...cabeceras(compania.token), tags: { grupo: 'diagnostico' } });
  errores.add(r.status >= 500);
  if (!check(r, { 'masivo 202': (x) => x.status === 202 })) return;
  const operacion = r.json('operacion_id');
  let espera = 3;
  let anterior = -1;
  while (Date.now() - inicio < TOPE_LOTE_MS) {
    sleep(espera);
    const p = http.get(`${BASE}/diagnosticos/${lote}/tramites/operaciones/${operacion}`, { ...cabeceras(compania.token), tags: { grupo: 'diagnostico' } });
    errores.add(p.status >= 500);
    if (p.status !== 200) continue;
    const cuerpo = p.json();
    if (cuerpo.estado === 'TERMINADA') {
      creados.add(cuerpo.creadas);
      omitidos.add(cuerpo.omitidas);
      fallidos.add(cuerpo.fallidas);
      tendencia.add(Date.now() - inicio);
      return;
    }
    // Como el front: 3 s mientras avanza; hasta 10 s si no cambia.
    const hechos = cuerpo.creadas + cuerpo.omitidas + cuerpo.fallidas;
    espera = hechos === anterior ? Math.min(10, espera + 2) : 3;
    anterior = hechos;
  }
  check(null, { 'operacion terminada a tiempo': () => false });
}

export function masivoBase() {
  const compania = porRol('base')[(__VU - 1) % porRol('base').length];
  for (const lote of compania.lotes) derivarLote(compania, lote, loteMs);
}

export function masivoBallena() {
  const compania = porRol('ballena')[(__VU - 1) % porRol('ballena').length];
  for (const lote of compania.lotes) derivarLote(compania, lote, loteMs);
}

export function masivoPequena() {
  const pequenas = porRol('pequena');
  const compania = pequenas[__ITER % pequenas.length];
  const lote = compania.lotes[Math.floor(__ITER / pequenas.length) % compania.lotes.length];
  derivarLote(compania, lote, lotePequenoMs);
}

export function ruidoTramites() {
  const compania = porRol('base')[(__VU - 1) % porRol('base').length];
  const ruta = RUIDO[__ITER % RUIDO.length];
  const r = http.get(`${TRAMITES}${ruta}`, { ...cabeceras(compania.tokenTramites || compania.token), tags: { grupo: "tramites" } });
  errores.add(r.status >= 500);
  sleep(1);
}
