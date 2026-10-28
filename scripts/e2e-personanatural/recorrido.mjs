// Recorrido E2E del portal Persona Natural contra piezas REALES corriendo en local (HU #14069, Epica #14021).
//   node scripts/e2e-personanatural/recorrido.mjs            (despues de levantar.ps1 y sembrar.mjs)
// Imprime PASS/FAIL por paso con su evidencia y termina con codigo 1 si algun paso falla. Solo datos FICTICIOS.
// Variables (todas opcionales): FRONT, GATEWAY, CORE_API, PG_DB, PSQL, RABBIT_CONTAINER, BUS_LOG (log del observador).
import { execFileSync } from "node:child_process";
import { existsSync, readFileSync } from "node:fs";

const FRONT = process.env.FRONT ?? "http://127.0.0.1:4031";
const GATEWAY = process.env.GATEWAY ?? "http://127.0.0.1:4002";
const CORE = process.env.CORE_API ?? "http://127.0.0.1:4003";
const DB = process.env.PG_DB ?? "flit_pn_e2e";
const PSQL = process.env.PSQL ?? "C:\\Program Files\\PostgreSQL\\17\\bin\\psql.exe";
const RABBIT = process.env.RABBIT_CONTAINER ?? "flit-local-rabbitmq-1";
const BUS_LOG = process.env.BUS_LOG ?? "";
const BASE = "/api/v1/persona-natural";
const EMPRESA_PN = "00000000-0000-0000-0000-0000000f1171";
const CONSENTIMIENTO = "v1";

// Datos ficticios (cedulas y correos de prueba; los documentos NO son reales)
// Placa distinta en cada corrida: con la misma, la regla de duplicidad de FLIT 2 (DUPLICATE_ACTIVE_PROCEDURE) ve el tramite anterior.
const letras = () => Array.from({ length: 3 }, () => "BCDFGHJKLMNPQRSTVWXZ"[Math.floor(Math.random() * 20)]).join("");
const PLACA = process.env.PLACA ?? `${letras()}${String(Math.floor(Math.random() * 900) + 100)}`;
const VENDEDOR = { tipoDocumento: "CC", numeroDocumento: "1000000002", correo: "vendedor.e2e@example.test", celular: "3000000001", direccion: "Calle 1 # 1-1" };
const COMPRADOR = { tipoDocumento: "CC", numeroDocumento: "1000000003", correo: "comprador.e2e@example.test", celular: "3000000002", direccion: "Calle 2 # 2-2" };
// Los topes antiabuso del BFF (por correo y por documento fallado) son reales: cada corrida usa correo y documentos de rechazo distintos.
const azar = (n) => String(Math.floor(Math.random() * 10 ** n)).padStart(n, "0");
const PNG = Buffer.from("iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==", "base64");

// ───────────────────────── utilidades ─────────────────────────
const resultados = [];
const ctx = {};
let cookie = "";

async function http(origen, metodo, ruta, { cuerpo, form, headers = {}, conCookie = true, captcha = true } = {}) {
  const h = { ...headers };
  if (cuerpo !== undefined) h["content-type"] = "application/json";
  if (captcha) h["x-captcha-token"] = "dev-e2e";
  if (conCookie && cookie) h.cookie = cookie;
  const r = await fetch(`${origen}${ruta}`, { method: metodo, headers: h, body: form ?? (cuerpo === undefined ? undefined : JSON.stringify(cuerpo)), redirect: "manual" });
  for (const s of r.headers.getSetCookie?.() ?? []) if (s.startsWith("pn_sesion=")) { cookie = s.split(";")[0]; ctx.setCookie = s; }
  const texto = await r.text();
  let json; try { json = texto ? JSON.parse(texto) : undefined; } catch { json = texto; }
  return { status: r.status, json, headers: r.headers, texto };
}
const pn = (m, r, o) => http(FRONT, m, `${BASE}${r}`, o);
const gw = (m, r, o) => http(GATEWAY, m, `${BASE}${r}`, o);

function sql(consulta) {
  const salida = execFileSync(PSQL, ["-h", "127.0.0.1", "-U", "postgres", "-d", DB, "-tA", "-F", "|", "-c", consulta], { env: { ...process.env, PGPASSWORD: process.env.PGPASSWORD ?? "flitdb" }, encoding: "utf8" });
  return salida.replace(/\r/g, "").trim().split("\n").filter(Boolean).map((l) => l.split("|"));
}
function colas() {
  const salida = execFileSync("docker", ["exec", RABBIT, "rabbitmqctl", "list_queues", "-p", "flit", "name", "messages", "consumers", "--formatter", "json"], { encoding: "utf8", env: { ...process.env, MSYS_NO_PATHCONV: "1" } });
  return JSON.parse(salida.slice(salida.indexOf("[")));
}
const dormir = (ms) => new Promise((r) => setTimeout(r, ms));
async function hasta(descripcion, fn, { intentos = 40, cada = 1500 } = {}) {
  let ultimo;
  for (let i = 0; i < intentos; i++) { ultimo = await fn(); if (ultimo) return ultimo; await dormir(cada); }
  throw new Error(`Tiempo agotado esperando: ${descripcion}`);
}
function verifica(cond, mensaje) { if (!cond) throw new Error(mensaje); }
async function paso(nombre, fn) {
  const t0 = Date.now();
  try {
    const evidencia = await fn();
    resultados.push({ nombre, ok: true, evidencia });
    console.log(`PASS  ${nombre}\n        ${evidencia}   (${Date.now() - t0} ms)`);
  } catch (e) {
    resultados.push({ nombre, ok: false, evidencia: String(e.message ?? e) });
    console.log(`FAIL  ${nombre}\n        ${String(e.message ?? e).split("\n").join("\n        ")}`);
  }
}
const resumen = (j, max = 140) => JSON.stringify(j).slice(0, max);

// ───────────────────────── recorrido ─────────────────────────
console.log(`Portal ${FRONT}  gateway ${GATEWAY}  core-api ${CORE}  base ${DB}\n`);

const otrasEmpresasAntes = (() => { try { return sql(`select count(*) from tramites.procedure_instances where tenant_id <> '${EMPRESA_PN}'`)[0][0]; } catch { return null; } })();

await paso("1a. GET /organismos (portal) ofrece el organismo habilitado", async () => {
  const r = await pn("GET", "/organismos", { captcha: false });
  verifica(r.status === 200 && Array.isArray(r.json) && r.json.length > 0, `HTTP ${r.status} ${resumen(r.json)}`);
  ctx.organismo = r.json[0];
  const g = await gw("GET", "/organismos", { captcha: false });
  verifica(g.status === 200 && g.json.length === r.json.length, `gateway HTTP ${g.status}`);
  return `${r.json.length} organismo(s): ${ctx.organismo.codigo} ${ctx.organismo.nombre}; el gateway 4002 responde lo mismo`;
});

await paso("1b. GET /tipos-tramite ofrece el tipo habilitado con tarifa", async () => {
  const r = await pn("GET", `/tipos-tramite?organismoId=${ctx.organismo.id}`, { captcha: false });
  verifica(r.status === 200, `HTTP ${r.status}`);
  const disp = r.json.filter((t) => t.disponible);
  verifica(disp.length > 0, `ningun tipo disponible: ${resumen(r.json)}`);
  ctx.tipo = disp.find((t) => t.code === "TRASPASO_STANDARD") ?? disp[0];
  return `disponibles: ${disp.map((t) => t.code).join(", ")} (de ${r.json.length} tipos listados)`;
});

await paso("1c. GET /configuracion publica el contenido (pregunta de prueba)", async () => {
  const r = await pn("GET", "/configuracion", { captcha: false });
  verifica(r.status === 200 && r.json.version !== "0" && r.json.preguntas.length > 0, `HTTP ${r.status} ${resumen(r.json, 200)}`);
  return `version ${r.json.version}, ${r.json.preguntas.length} pregunta(s): «${r.json.preguntas[0].pregunta.slice(0, 40)}»`;
});

await paso("2a. POST /cotizaciones: 2 lineas + total", async () => {
  const r = await pn("POST", "/cotizaciones", { cuerpo: { organismoId: ctx.organismo.id, tipoCodigo: ctx.tipo.code } });
  verifica(r.status === 200 && r.json.lineas.length === 2, `HTTP ${r.status} ${resumen(r.json)}`);
  const suma = r.json.lineas.reduce((a, l) => a + l.valorCop, 0);
  verifica(suma === r.json.totalCop && r.json.totalCop === 150, `total ${r.json.totalCop} != 150 (100+50)`);
  return `${r.json.lineas.map((l) => `${l.codigo}=${l.valorCop}`).join(" + ")} = ${r.json.totalCop}`;
});

await paso("2b. POST /cotizaciones/enviar-correo (core-notificaciones real, SMTP a consola)", async () => {
  const llave = `e2e-cot-${Date.now()}`;
  const body = { organismoId: ctx.organismo.id, tipoCodigo: ctx.tipo.code, correo: `prueba.e2e.${azar(6)}@example.test` };
  const r = await pn("POST", "/cotizaciones/enviar-correo", { cuerpo: body, headers: { "idempotency-key": llave } });
  verifica(r.status === 200 && r.json.enviado === true, `HTTP ${r.status} ${resumen(r.json)}`);
  const r2 = await pn("POST", "/cotizaciones/enviar-correo", { cuerpo: body, headers: { "idempotency-key": llave } });
  verifica(r2.json?.repetido === true, `la repeticion con la misma llave no se marco repetido: ${resumen(r2.json)}`);
  return "enviado=true; con la misma llave repetido=true (el correo sale por la consola de core-notificaciones: ver su log, «[DEV EMAIL]»)";
});

await paso("3a. POST /consultas/vehiculo con el mock de proveedores", async () => {
  const r = await pn("POST", "/consultas/vehiculo", { cuerpo: { placa: PLACA, propietario: { tipo: VENDEDOR.tipoDocumento, numero: VENDEDOR.numeroDocumento }, consentimiento: true, consentimientoVersion: CONSENTIMIENTO } });
  verifica(r.status === 200 && r.json.existe === true, `HTTP ${r.status} ${resumen(r.json)}`);
  return `existe=true ${r.json.marca} ${r.json.linea} ${r.json.modeloAno} (datos del mock)`;
});

await paso("3b. POST /radicados/consulta con un radicado inexistente responde 404 generico", async () => {
  const r = await pn("POST", "/radicados/consulta", { cuerpo: { radicado: "PN-2026-999999", cedula: `9${azar(9)}` } });
  verifica(r.status === 404 && r.json.code === "solicitud_no_encontrada", `HTTP ${r.status} ${resumen(r.json)}`);
  return `404 ${r.json.code}: «${r.json.detail}»`;
});

await paso("4a. POST /sesiones (consentimiento) deja la cookie HttpOnly y no filtra el token", async () => {
  cookie = "";
  const r = await pn("POST", "/sesiones", { cuerpo: { tipoCodigo: ctx.tipo.code, organismoId: ctx.organismo.id, cedula: VENDEDOR.numeroDocumento, placa: PLACA, consentimiento: true, consentimientoVersion: CONSENTIMIENTO } });
  verifica(r.status === 201 && r.json.solicitudId, `HTTP ${r.status} ${resumen(r.json)}`);
  ctx.solicitudId = r.json.solicitudId; ctx.codigoPublico = r.json.codigoPublico;
  verifica(cookie.startsWith("pn_sesion="), "no llego la cookie pn_sesion");
  verifica(/HttpOnly/i.test(ctx.setCookie ?? ""), `la cookie no es HttpOnly: ${(ctx.setCookie ?? "").replace(/pn_sesion=[^;]+/, "pn_sesion=<oculto>")}`);
  verifica(!("token" in r.json) && !JSON.stringify(r.json).includes("Bearer"), "el cuerpo expone un token");
  return `201 ${r.json.codigoPublico}; cookie HttpOnly (${(ctx.setCookie ?? "").replace(/pn_sesion=[^;]+/, "pn_sesion=<oculto>")})`;
});

await paso("4b. definicion del tipo y GET /solicitudes/{id}", async () => {
  const d = await pn("GET", `/tipos-tramite/${ctx.tipo.code}/definicion?organismoId=${ctx.organismo.id}`, { captcha: false });
  verifica(d.status === 200 && d.json.actores?.length > 0, `definicion HTTP ${d.status}`);
  ctx.definicion = d.json;
  const s = await pn("GET", `/solicitudes/${ctx.solicitudId}`);
  verifica(s.status === 200 && s.json.estado === "borrador", `solicitud HTTP ${s.status} ${resumen(s.json)}`);
  ctx.version = s.json.version;
  return `definicion v${d.json.version}: pasos ${d.json.pasos.map((p) => p.clave).join(">")}; solicitud ${s.json.codigoPublico} en borrador, version ${s.json.version}`;
});

await paso("5a. persona-detalle (mock) + PATCH /actores (vendedor y comprador)", async () => {
  const pd = await pn("POST", `/solicitudes/${ctx.solicitudId}/consultas/persona-detalle`, { cuerpo: { tipoDocumento: VENDEDOR.tipoDocumento, documento: VENDEDOR.numeroDocumento, rol: "vendedor", consentimiento: true, consentimientoVersion: CONSENTIMIENTO } });
  verifica(pd.status === 200 && pd.json.existe, `persona-detalle HTTP ${pd.status} ${resumen(pd.json)}`);
  const r = await pn("PATCH", `/solicitudes/${ctx.solicitudId}/actores`, { cuerpo: { version: ctx.version, actores: [
    { rol: "vendedor", ordinal: 1, ...VENDEDOR, nombre: pd.json.nombreCompleto },
    { rol: "comprador", ordinal: 1, ...COMPRADOR, nombre: "COMPRADOR PRUEBA E2E" }] } });
  verifica(r.status === 200 && r.json.actores.length === 2, `PATCH HTTP ${r.status} ${resumen(r.json, 300)}`);
  ctx.version = r.json.version;
  return `persona-detalle existe=true; 2 actores guardados, version ${r.json.version}`;
});

await paso("5b. identidad: pide la validacion de cada parte (core-api real, proveedor mock)", async () => {
  for (const parte of ["vendedor", "comprador"]) {
    const r = await pn("POST", `/solicitudes/${ctx.solicitudId}/identidad/${parte}`, { cuerpo: { ordinal: 1 } });
    verifica(r.status === 200 || r.status === 202, `${parte}: HTTP ${r.status} ${resumen(r.json)}`);
    verifica(r.json.estado === "pendiente", `${parte}: estado ${r.json.estado}`);
  }
  const filas = sql(`select id, status, provider from tramites.procedure_instance_biometric_validations where portal_solicitud_ref = '${ctx.solicitudId}' order by created_at`);
  verifica(filas.length === 2 && filas.every((f) => f[1] === "enviado" && f[2] === "mock"), `filas en core-api: ${JSON.stringify(filas)}`);
  ctx.validaciones = filas.map((f) => f[0]);
  return `2 validaciones mock en estado 'enviado' en tramites.procedure_instance_biometric_validations`;
});

await paso("5c. identidad: aprueba cada parte por el camino del mock (magic-link con token conocido, ver nota)", async () => {
  // El proveedor mock guarda solo el HASH del token del enlace y nadie lo envia (en el portal no hay correo del mock):
  // la prueba fija el hash de un token conocido en su base temporal y completa la captura por el endpoint publico real.
  let i = 0;
  for (const id of ctx.validaciones) {
    const token = `e2e-token-${ctx.solicitudId.slice(-8)}-${++i}`;
    sql(`update tramites.procedure_instance_biometric_validations set token_hash = encode(sha256(convert_to('${token}','UTF8')),'hex') where id = '${id}'`);
    const form = new FormData();
    for (const campo of ["rostro", "cedula_frontal", "cedula_reverso"]) form.append(campo, new Blob([PNG], { type: "image/png" }), `${campo}.png`);
    const r = await http(GATEWAY, "POST", `/api/v1/public/biometric/${token}`, { form, conCookie: false, captcha: false });
    verifica(r.status === 200 && r.json.estado === "aprobado", `captura ${i}: HTTP ${r.status} ${resumen(r.json)}`);
  }
  const e = await hasta("espejo aprobado en el BFF", async () => {
    const r = await pn("GET", `/solicitudes/${ctx.solicitudId}/identidad`);
    return r.status === 200 && r.json.completa ? r.json : null;
  }, { intentos: 10, cada: 1000 });
  return `ambas partes aprobada en el BFF (completa=${e.completa}). NOTA: el camino mock no publica tramites.identity_validation.completed; el espejo se actualiza por consulta (pull) a core-api`;
});

await paso("5d. campos, comercial, requisitos, adjunto (file-manager simulado) y diagnostico", async () => {
  let r = await pn("PUT", `/solicitudes/${ctx.solicitudId}/campos`, { cuerpo: { version: ctx.version, valores: { plate: PLACA, owner_document_type: VENDEDOR.tipoDocumento, owner_document_number: VENDEDOR.numeroDocumento } } });
  verifica(r.status === 200, `campos HTTP ${r.status} ${resumen(r.json)}`);
  r = await pn("PUT", `/solicitudes/${ctx.solicitudId}/comercial`, { cuerpo: { version: r.json.version, valorComercial: 50000000 } });
  verifica(r.status === 200, `comercial HTTP ${r.status} ${resumen(r.json)}`);
  r = await pn("PUT", `/solicitudes/${ctx.solicitudId}/requisitos`, { cuerpo: { version: r.json.version, transformaciones: [], observaciones: "prueba e2e" } });
  verifica(r.status === 200, `requisitos HTTP ${r.status} ${resumen(r.json)}`);
  const form = new FormData();
  form.append("tipo", "paz_salvo");
  form.append("archivo", new Blob([PNG], { type: "image/png" }), "paz-salvo-e2e.png");
  const a = await pn("POST", `/solicitudes/${ctx.solicitudId}/adjuntos`, { form });
  verifica(a.status === 201, `adjunto HTTP ${a.status} ${resumen(a.json)}`);
  const d = await pn("POST", `/solicitudes/${ctx.solicitudId}/diagnostico`, { cuerpo: { propietario: { tipoDocumento: VENDEDOR.tipoDocumento, numeroDocumento: VENDEDOR.numeroDocumento } } });
  verifica(d.status === 200 && d.json.puedeContinuar, `diagnostico HTTP ${d.status} ${resumen(d.json)}`);
  const s = await pn("GET", `/solicitudes/${ctx.solicitudId}`);
  verifica(s.json.completo === true, `la solicitud no queda completa: pendientes ${resumen(s.json.pendientes, 300)}`);
  ctx.version = s.json.version;
  return `adjunto ${a.json.nombreArchivo} (${a.json.tamanoBytes} B); diagnostico puedeContinuar=true; solicitud completa=true`;
});

const llaveRadicar = `e2e-radicar-${Date.now()}`;
await paso("6a. POST /radicar con Idempotency-Key responde 202 y queda en cola", async () => {
  ctx.antesProc = sql(`select count(*) from tramites.procedure_instances where tenant_id = '${EMPRESA_PN}'`)[0][0];
  const r = await pn("POST", `/solicitudes/${ctx.solicitudId}/radicar`, { headers: { "idempotency-key": llaveRadicar } });
  verifica(r.status === 202, `HTTP ${r.status} ${resumen(r.json, 300)}`);
  ctx.radicarJson = r.json;
  return `202 estado=${r.json.estado} codigoPublico=${r.json.codigoPublico}`;
});

await paso("6b. core-api consume el evento, lee por RPC y CREA el tramite BORRADOR en la empresa Persona Natural", async () => {
  const fila = await hasta("tramite creado en tramites.procedure_instances", async () => {
    const f = sql(`select id, reference_number, status, origin, external_ref, plate from tramites.procedure_instances where tenant_id = '${EMPRESA_PN}' and external_ref = '${ctx.solicitudId}'`);
    return f[0];
  }, { intentos: 40, cada: 1500 });
  ctx.tramite = { id: fila[0], referencia: fila[1], estado: fila[2], origen: fila[3] };
  verifica(fila[2] === "borrador", `estado ${fila[2]} (se esperaba borrador)`);
  const actores = sql(`select count(*) from tramites.procedure_instance_actors where procedure_instance_id = '${fila[0]}'`)[0][0];
  const adjuntos = sql(`select count(*) from tramites.procedure_instance_attachments where procedure_instance_id = '${fila[0]}' and source = 'portal'`)[0][0];
  verifica(actores === "2" && adjuntos === "1", `el borrador trae ${actores} actores y ${adjuntos} adjuntos del portal (se esperaban 2 y 1)`);
  return `procedure_instances: ref ${fila[1]} estado=${fila[2]} origin=${fila[3]} external_ref=solicitud placa=${fila[5]}; ${actores} actores y ${adjuntos} adjunto(s) origen portal`;
});

await paso("6c. el evento de vuelta llega: espejo del BFF 'creada' con numeroReferencia", async () => {
  const e = await hasta("estado creada con numeroReferencia", async () => {
    const r = await pn("GET", `/solicitudes/${ctx.solicitudId}/estado`);
    return r.status === 200 && r.json.numeroReferencia ? r.json : null;
  }, { intentos: 30, cada: 1500 });
  verifica(e.estado === "creada", `estado ${e.estado}`);
  verifica(e.numeroReferencia === ctx.tramite.referencia, `numeroReferencia ${e.numeroReferencia} != ${ctx.tramite.referencia}`);
  return `GET /estado: estado=${e.estado} numeroReferencia=${e.numeroReferencia}`;
});

await paso("6d. bus: la salida del evento radicacion_solicitada y la vuelta quedan en las colas", async () => {
  const q = colas();
  const mensajes = Object.fromEntries(q.map((c) => [c.name, c.messages]));
  const dlq = q.filter((c) => c.name.endsWith(".dlq") && c.messages > 0).map((c) => `${c.name}=${c.messages}`);
  verifica(dlq.length === 0, `mensajes en DLQ: ${dlq.join(", ")}`);
  const consumidores = ["tramites.personanatural-radicacion", "tramites.personanatural-estado", "personanatural.resolucion", "personanatural.estado", "personanatural.identidad"];
  for (const c of consumidores) verifica(q.find((x) => x.name === c)?.consumers >= 1, `la cola ${c} no tiene consumidor`);
  let traza = "(sin BUS_LOG: define BUS_LOG con el log del observador para ver los mensajes)";
  if (BUS_LOG && existsSync(BUS_LOG)) {
    const lineas = readFileSync(BUS_LOG, "utf8").split("\n").filter((l) => /persona_natural/.test(l));
    const claves = [...new Set(lineas.map((l) => /rk=(\S+)/.exec(l)?.[1]))];
    for (const k of ["tramites.persona_natural.radicacion_solicitada", "tramites.persona_natural.solicitud_resuelta"])
      verifica(claves.includes(k), `el observador no vio ${k}; vio ${claves.join(", ") || "nada"}`);
    traza = `observador vio: ${claves.join(", ")}`;
  }
  return `sin mensajes en DLQ; consumidores activos en las 5 colas del portal; ${traza}`;
});

await paso("7. el tramite NO esta en el feed de Flito y no hay efectos en otras empresas", async () => {
  const enFeed = sql(`select count(*) from tramites.procedure_instance_status_history h where h.procedure_instance_id = '${ctx.tramite.id}' and h.to_status in ('preasignacion','entregado')`);
  verifica(enFeed[0][0] === "0", "el tramite tiene historial de radicacion (entraria al feed por esa regla)");
  const otras = sql(`select count(*) from tramites.procedure_instances where tenant_id <> '${EMPRESA_PN}'`)[0][0];
  verifica(otrasEmpresasAntes === null || otras === otrasEmpresasAntes, `otras empresas: ${otrasEmpresasAntes} -> ${otras}`);
  const exclusion = readFileSync(new URL("../../services/core-api/src/Flit.Infrastructure/Persistence/Repositories/ProcedureSyncReadRepository.cs", import.meta.url), "utf8");
  verifica(exclusion.includes(`tenant_id <> '${EMPRESA_PN}'`), "la consulta del feed ya no excluye a la empresa Persona Natural");
  return `sin historial a preasignacion/entregado (el feed solo trae radicados) y la consulta del feed excluye la empresa Persona Natural; tramites de otras empresas: ${otrasEmpresasAntes} -> ${otras}`;
});

await paso("8. re-radicar con la misma llave no duplica el tramite", async () => {
  const r = await pn("POST", `/solicitudes/${ctx.solicitudId}/radicar`, { headers: { "idempotency-key": llaveRadicar } });
  verifica(r.status === 202 || r.status === 200, `HTTP ${r.status} ${resumen(r.json, 300)}`);
  await dormir(4000);
  const n = sql(`select count(*) from tramites.procedure_instances where tenant_id = '${EMPRESA_PN}' and external_ref = '${ctx.solicitudId}'`)[0][0];
  verifica(n === "1", `hay ${n} tramites para la solicitud`);
  const total = sql(`select count(*) from tramites.procedure_instances where tenant_id = '${EMPRESA_PN}'`)[0][0];
  return `HTTP ${r.status}; sigue habiendo 1 tramite para la solicitud (empresa PN: ${ctx.antesProc} -> ${total})`;
});

await paso("9. POST /radicados/consulta con el radicado real (sin sesion)", async () => {
  cookie = "";
  const r = await pn("POST", "/radicados/consulta", { cuerpo: { placa: PLACA, radicado: ctx.tramite.referencia, cedula: VENDEDOR.numeroDocumento } });
  verifica(r.status === 200, `HTTP ${r.status} ${resumen(r.json, 300)}`);
  const mal = await pn("POST", "/radicados/consulta", { cuerpo: { placa: PLACA, radicado: ctx.tramite.referencia, cedula: `9${azar(9)}` } });
  verifica(mal.status === 404, `con otra cedula debia ser 404 y fue ${mal.status}`);
  return `200 macroEstado=${r.json.macroEstado?.codigo} «${r.json.macroEstado?.etiqueta}»; con otra cedula 404 generico`;
});

// ───────────────────────── informe ─────────────────────────
const ok = resultados.filter((r) => r.ok).length;
console.log(`\n==== ${ok}/${resultados.length} PASS ====`);
for (const r of resultados) console.log(`${r.ok ? "PASS" : "FAIL"}  ${r.nombre}`);
process.exit(ok === resultados.length ? 0 : 1);
