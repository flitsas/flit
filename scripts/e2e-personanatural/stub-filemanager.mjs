// File-manager SIMULADO para la prueba local (HU #14069). El real es S3 corporativo y NO se toca desde local.
// Imita el contrato que usan core-personanatural y core-api: POST /api/v1/files -> { id, presignedUrl:{ url, method } },
// PUT /upload/{id} guarda los bytes, GET /api/v1/files/{id}/presigned-url -> { presignedUrl:{ url, method } } y
// GET /upload/{id} los devuelve. Guarda en la carpeta temporal para que los documentos sobrevivan a un reinicio.
import http from "node:http";
import { randomUUID } from "node:crypto";
import { existsSync, mkdirSync, readFileSync, writeFileSync } from "node:fs";
import { tmpdir } from "node:os";
import { extname, join } from "node:path";

const puerto = Number(process.env.STUB_FM_PORT ?? 4999);
const carpeta = process.env.STUB_FM_DIR ?? join(tmpdir(), "flit-pn-e2e-filemanager");
mkdirSync(carpeta, { recursive: true });

const ID_VALIDO = /^[\w-]+$/;
const rutaMeta = (id) => join(carpeta, `${id}.json`);
const rutaBytes = (id) => join(carpeta, `${id}.bin`);
const leerMeta = (id) => (ID_VALIDO.test(id) && existsSync(rutaMeta(id)) ? JSON.parse(readFileSync(rutaMeta(id), "utf8")) : null);
const leerBytes = (id) => (ID_VALIDO.test(id) && existsSync(rutaBytes(id)) ? readFileSync(rutaBytes(id)) : null);

const TIPOS = { ".pdf": "application/pdf", ".png": "image/png", ".jpg": "image/jpeg", ".jpeg": "image/jpeg", ".webp": "image/webp" };
const tipoDe = (nombre) => TIPOS[extname(nombre ?? "").toLowerCase()] ?? "application/octet-stream";

function leer(req) {
  return new Promise((ok) => {
    const trozos = [];
    req.on("data", (t) => trozos.push(t));
    req.on("end", () => ok(Buffer.concat(trozos)));
  });
}

http
  .createServer(async (req, res) => {
    const url = new URL(req.url ?? "/", `http://127.0.0.1:${puerto}`);
    const log = (extra = "") => console.log(`${new Date().toISOString()} ${req.method} ${url.pathname} ${extra}`);
    if (url.pathname === "/healthz") { res.writeHead(200).end("ok"); return; }

    if (req.method === "POST" && url.pathname === "/api/v1/files") {
      const meta = JSON.parse((await leer(req)).toString("utf8") || "{}");
      const id = randomUUID();
      writeFileSync(rutaMeta(id), JSON.stringify(meta));
      log(`crea ${id} (${meta.filename})`);
      res.writeHead(201, { "content-type": "application/json" });
      res.end(JSON.stringify({ id, presignedUrl: { url: `http://127.0.0.1:${puerto}/upload/${id}`, method: "PUT", fields: {} } }));
      return;
    }

    const objeto = url.pathname.match(/^\/upload\/([\w-]+)$/);
    if (objeto && (req.method === "PUT" || req.method === "POST")) {
      const bytes = await leer(req);
      if (!leerMeta(objeto[1])) { log("404"); res.writeHead(404).end(); return; }
      writeFileSync(rutaBytes(objeto[1]), bytes);
      log(`sube ${bytes.length} bytes`);
      res.writeHead(200).end();
      return;
    }
    if (objeto && req.method === "GET") {
      const bytes = leerBytes(objeto[1]);
      if (!bytes) { log("404"); res.writeHead(404).end(); return; }
      log(`baja ${bytes.length} bytes`);
      // Como S3 con disposition=inline: el navegador lo muestra, y el visor del front puede leerlo desde otro origen.
      res.writeHead(200, {
        "content-type": tipoDe(leerMeta(objeto[1])?.filename),
        "content-disposition": "inline",
        "access-control-allow-origin": "*",
      });
      res.end(bytes);
      return;
    }

    // Como el real: entrega el JSON con la URL de descarga, nunca los bytes.
    const firmada = url.pathname.match(/^\/api\/v1\/files\/([\w-]+)\/presigned-url$/);
    if (firmada && req.method === "GET") {
      if (!leerBytes(firmada[1])) { log("404"); res.writeHead(404).end(); return; }
      log("entrega url firmada");
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({ presignedUrl: { url: `http://127.0.0.1:${puerto}/upload/${firmada[1]}`, method: "GET" } }));
      return;
    }

    log("sin ruta");
    res.writeHead(404).end();
  })
  .listen(puerto, "127.0.0.1", () => console.log(`stub file-manager en http://127.0.0.1:${puerto} (carpeta ${carpeta})`));
