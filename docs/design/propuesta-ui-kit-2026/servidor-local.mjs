#!/usr/bin/env node
// Servidor local de la propuesta conectado a la API real de FLIT, en SOLO LECTURA.
//
//   node servidor-local.mjs                    # producción: https://api.flitsas.com
//   FLIT_API=https://api.qa.flitsas.online node servidor-local.mjs   # QA
//
// Qué hace:
// - Sirve la propuesta en http://127.0.0.1:8732 (solo en este equipo).
// - POST /local/ingresar  → reenvía el ingreso a POST /api/v1/auth/login. El token se queda en la
//   memoria de este proceso; el navegador solo recibe una cookie de sesión aleatoria (HttpOnly).
// - GET  /local/sesion    → nombre, correo y rol leídos del token, y el ambiente al que apunta.
// - POST /local/salir     → borra la sesión.
// - GET  /api/v1/...      → reenvía la consulta con el token. CUALQUIER otro método hacia /api
//   responde 405 aquí mismo y nunca sale del equipo: la propuesta no puede escribir en FLIT.
// No registra contraseñas, tokens ni cuerpos de respuesta: solo método, ruta y código.
import http from "node:http";
import { readFile } from "node:fs/promises";
import { randomUUID } from "node:crypto";
import { extname, join, normalize, dirname } from "node:path";
import { fileURLToPath } from "node:url";

const RAIZ = dirname(fileURLToPath(import.meta.url));
const API = (process.env.FLIT_API || "https://api.flitsas.com").replace(/\/$/, "");
const PUERTO = Number(process.env.PUERTO || 8732);
const AMBIENTE = /qa\./.test(API) ? "QA" : /dev\.|localhost|127\.0\.0\.1/.test(API) ? "Desarrollo" : "Producción";
const TIPOS = { ".html": "text/html; charset=utf-8", ".js": "text/javascript; charset=utf-8", ".mjs": "text/javascript; charset=utf-8", ".css": "text/css; charset=utf-8", ".svg": "image/svg+xml", ".png": "image/png", ".webp": "image/webp", ".ico": "image/x-icon", ".json": "application/json" };
const sesiones = new Map(); // id → { token, exp, claims }

const json = (res, codigo, cuerpo, cabeceras = {}) => { res.writeHead(codigo, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", ...cabeceras }); res.end(JSON.stringify(cuerpo)); };
const leerCuerpo = (req) => new Promise((ok, mal) => { let b = ""; req.on("data", (c) => { b += c; if (b.length > 1e5) req.destroy(); }); req.on("end", () => ok(b)); req.on("error", mal); });
const cookie = (req) => Object.fromEntries((req.headers.cookie || "").split(";").map((p) => p.trim().split("=")).filter((p) => p[0]));
function sesionDe(req) {
  const s = sesiones.get(cookie(req).flit_propuesta);
  if (!s) return null;
  if (s.exp && s.exp * 1000 < Date.now()) { sesiones.delete(cookie(req).flit_propuesta); return null; }
  return s;
}
function leerToken(token) {
  try { return JSON.parse(Buffer.from(token.split(".")[1].replace(/-/g, "+").replace(/_/g, "/"), "base64").toString("utf8")); } catch { return {}; }
}
const primero = (o, ...k) => { for (const x of k) { const v = o[x]; if (v) return Array.isArray(v) ? v[0] : v; } return null; };
function perfil(c) {
  return {
    nombre: primero(c, "name", "given_name", "unique_name", "full_name", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/name"),
    correo: primero(c, "email", "upn", "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/emailaddress"),
    rol: primero(c, "role_code", "role", "roles", "http://schemas.microsoft.com/ws/2008/06/identity/claims/role"),
    compania: primero(c, "tenant_name", "company_name", "companyName"),
    vence: c.exp ? new Date(c.exp * 1000).toISOString() : null
  };
}
const registrar = (m, ruta, codigo) => console.log(`${new Date().toLocaleTimeString("es-CO")}  ${m.padEnd(5)} ${ruta.split("?")[0]}  ${codigo}`);

const servidor = http.createServer(async (req, res) => {
  const url = new URL(req.url, "http://local");
  try {
    if (url.pathname === "/local/ingresar" && req.method === "POST") {
      const { correo, clave } = JSON.parse((await leerCuerpo(req)) || "{}");
      if (!correo || !clave) return json(res, 400, { error: "Escribe tu correo y tu contraseña." });
      const r = await fetch(API + "/api/v1/auth/login", { method: "POST", headers: { "Content-Type": "application/json", Accept: "application/json" }, body: JSON.stringify({ email: correo, password: clave }) });
      registrar("POST", "/api/v1/auth/login", r.status);
      if (!r.ok) {
        const msg = r.status === 401 || r.status === 400 ? "El correo o la contraseña no coinciden." : r.status === 423 || r.status === 429 ? "La cuenta está bloqueada por intentos fallidos. Espera unos minutos o recupera tu contraseña." : "FLIT no respondió el ingreso (" + r.status + "). Intenta de nuevo en un momento.";
        return json(res, r.status === 401 || r.status === 400 ? 401 : 502, { error: msg });
      }
      const d = await r.json();
      const claims = leerToken(d.accessToken);
      const id = randomUUID();
      sesiones.set(id, { token: d.accessToken, exp: claims.exp, claims });
      return json(res, 200, { ...perfil(claims), ambiente: AMBIENTE }, { "Set-Cookie": `flit_propuesta=${id}; HttpOnly; SameSite=Strict; Path=/` });
    }
    if (url.pathname === "/local/sesion") {
      const s = sesionDe(req);
      return s ? json(res, 200, { ...perfil(s.claims), ambiente: AMBIENTE }) : json(res, 401, { ambiente: AMBIENTE });
    }
    if (url.pathname === "/local/salir" && req.method === "POST") {
      sesiones.delete(cookie(req).flit_propuesta);
      return json(res, 200, {}, { "Set-Cookie": "flit_propuesta=; HttpOnly; SameSite=Strict; Path=/; Max-Age=0" });
    }
    if (url.pathname.startsWith("/api/")) {
      if (req.method !== "GET") { registrar(req.method, url.pathname, "405 bloqueado"); return json(res, 405, { error: "La propuesta es de solo lectura: no puede cambiar nada en FLIT." }); }
      const s = sesionDe(req);
      if (!s) return json(res, 401, { error: "Tu sesión terminó. Vuelve a ingresar." });
      const r = await fetch(API + url.pathname + url.search, { headers: { Authorization: "Bearer " + s.token, Accept: "application/json" } });
      registrar("GET", url.pathname, r.status);
      const cuerpo = await r.text();
      res.writeHead(r.status, { "Content-Type": r.headers.get("content-type") || "application/json", "Cache-Control": "no-store" });
      return res.end(cuerpo);
    }
    // Archivos de la propuesta
    const ruta = normalize(join(RAIZ, url.pathname === "/" ? "index.html" : decodeURIComponent(url.pathname)));
    if (!ruta.startsWith(RAIZ) || /servidor-local|\.md$/.test(ruta)) { res.writeHead(404); return res.end(); }
    const datos = await readFile(ruta);
    res.writeHead(200, { "Content-Type": TIPOS[extname(ruta)] || "application/octet-stream", "Cache-Control": "no-cache" });
    res.end(datos);
  } catch (e) {
    if (e.code === "ENOENT") { res.writeHead(404); return res.end(); }
    console.error("Error:", e.message);
    json(res, 502, { error: "No pudimos hablar con FLIT. Revisa tu conexión y vuelve a intentar." });
  }
});
servidor.listen(PUERTO, "127.0.0.1", () => {
  console.log(`Propuesta FLIT en http://127.0.0.1:${PUERTO}`);
  console.log(`Conectada a ${API} (${AMBIENTE}), solo lectura.`);
});
