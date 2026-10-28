// Siembra los datos de prueba FICTICIOS del portal Persona Natural en la base LOCAL temporal (HU #14069), como
// superadministrador y por la API de administracion de core-api. Idempotente: se puede correr de nuevo.
//   node scripts/e2e-personanatural/sembrar.mjs
// Variables: CORE_API (por defecto http://127.0.0.1:4003), SA_EMAIL / SA_PASSWORD (usuario seed de Development),
// TIPO (por defecto TRASPASO_STANDARD), ORGANISMO_CODIGO (por defecto 5631000, Sabaneta: el organismo debe ser un OT
// «operable» —con perfil y tenant OT activos, ver OtOperabilityGate— o core-api rechaza el tramite con ot_not_authorized_for_type).
const API = process.env.CORE_API ?? "http://127.0.0.1:4003";
const TIPO = process.env.TIPO ?? "TRASPASO_STANDARD";

async function llamar(metodo, ruta, cuerpo, token) {
  const r = await fetch(`${API}${ruta}`, {
    method: metodo,
    headers: { "content-type": "application/json", ...(token ? { authorization: `Bearer ${token}` } : {}) },
    body: cuerpo === undefined ? undefined : JSON.stringify(cuerpo),
  });
  const texto = await r.text();
  let json; try { json = texto ? JSON.parse(texto) : undefined; } catch { json = texto; }
  return { status: r.status, json };
}
const ok = (r, que) => { if (r.status >= 400) throw new Error(`${que}: HTTP ${r.status} ${JSON.stringify(r.json)}`); return r.json; };

const login = ok(await llamar("POST", "/api/v1/auth/login", { email: process.env.SA_EMAIL ?? "demo@flit.local", password: process.env.SA_PASSWORD ?? "DemoPass1!" }), "login superadmin");
const t = login.accessToken;
const base = "/api/v1/admin/plataforma/persona-natural";

// 1. Organismo
const organismos = ok(await llamar("GET", `${base}/organismos`, undefined, t), "organismos");
const codigoOrg = process.env.ORGANISMO_CODIGO ?? "5631000";
const org = organismos.find((o) => o.codigo === codigoOrg);
if (!org) throw new Error("No hay organismos de transito en la base");
ok(await llamar("PUT", `${base}/organismos/${org.id}`, { habilitado: true }, t), "habilitar organismo");
for (const otro of organismos.filter((o) => o.habilitado && o.id !== org.id)) {
  ok(await llamar("PUT", `${base}/organismos/${otro.id}`, { habilitado: false }, t), "deshabilitar otro organismo");
  console.log(`organismo deshabilitado: ${otro.codigo}`);
}
console.log(`organismo habilitado: ${org.codigo} ${org.nombre} (${org.id})`);

// 2. Tipo
const tipos = ok(await llamar("GET", `${base}/tipos`, undefined, t), "tipos");
console.log("tipos candidatos:", tipos.map((x) => `${x.codigo}${x.elegible ? "" : "(no elegible)"}`).join(", "));
const tipo = tipos.find((x) => x.codigo === TIPO) ?? tipos.find((x) => x.elegible);
if (!tipo) throw new Error("Ningun tipo elegible para habilitar");
ok(await llamar("PUT", `${base}/tipos/${tipo.id}`, { habilitado: true }, t), "habilitar tipo");
console.log(`tipo habilitado: ${tipo.codigo} (${tipo.id})`);

// 3. Tarifas ficticias (100 y 50 pesos) vigentes desde hoy en Colombia, solo si no hay una vigente
const vigentes = ok(await llamar("GET", `${base}/tarifas?soloVigentes=true&tipoTramiteId=${tipo.id}`, undefined, t), "tarifas");
if (vigentes.some((v) => v.concepto === "DERECHOS_TRANSITO") && vigentes.some((v) => v.concepto === "SERVICIO_FLIT")) {
  console.log("tarifas ya vigentes: se conservan");
} else {
  const hoy = new Intl.DateTimeFormat("sv-SE", { timeZone: "America/Bogota" }).format(new Date());
  ok(await llamar("POST", `${base}/tarifas`, { tipoTramiteId: tipo.id, vigenteDesde: hoy, valores: [{ concepto: "DERECHOS_TRANSITO", valorCop: 100 }, { concepto: "SERVICIO_FLIT", valorCop: 50 }] }, t), "tarifas nuevas");
  console.log(`tarifas de prueba creadas desde ${hoy}: DERECHOS_TRANSITO=100, SERVICIO_FLIT=50`);
}

// 4. Contenido: una pregunta frecuente y publicar
const preguntas = ok(await llamar("GET", `${base}/contenido/preguntas`, undefined, t), "preguntas");
if (!preguntas.some((p) => p.pregunta.startsWith("[PRUEBA E2E]"))) {
  ok(await llamar("POST", `${base}/contenido/preguntas`, { pregunta: "[PRUEBA E2E] ¿Cuanto tarda un traspaso?", respuesta: "Texto de prueba del entorno local.", activa: true }, t), "crear pregunta");
}
const pub = await llamar("POST", `${base}/contenido/publicar`, undefined, t);
console.log(`publicar contenido: HTTP ${pub.status}${pub.status === 409 ? " (sin cambios por publicar)" : ""}`);

console.log(JSON.stringify({ organismoId: org.id, tipoCodigo: tipo.codigo, tipoId: tipo.id }));
