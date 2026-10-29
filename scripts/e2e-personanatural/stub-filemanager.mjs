// File-manager SIMULADO para la prueba local (HU #14069). El real es S3 corporativo y NO se toca desde local.
// Imita el contrato que usan core-personanatural y core-api: POST /api/v1/files -> { id, presignedUrl:{ url, method } },
// PUT /upload/{id} guarda los bytes en memoria, GET /api/v1/files/{id}[/...] los devuelve. Solo memoria; sin disco.
import http from "node:http";
import { randomUUID } from "node:crypto";

const puerto = Number(process.env.STUB_FM_PORT ?? 4999);
const archivos = new Map(); // id -> { meta, bytes }

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
      archivos.set(id, { meta, bytes: null });
      log(`crea ${id} (${meta.filename})`);
      res.writeHead(201, { "content-type": "application/json" });
      res.end(JSON.stringify({ id, presignedUrl: { url: `http://127.0.0.1:${puerto}/upload/${id}`, method: "PUT", fields: {} } }));
      return;
    }
    const sube = url.pathname.match(/^\/upload\/([\w-]+)$/);
    if (sube && (req.method === "PUT" || req.method === "POST")) {
      const bytes = await leer(req);
      const a = archivos.get(sube[1]);
      if (!a) { log("404"); res.writeHead(404).end(); return; }
      a.bytes = bytes;
      log(`sube ${bytes.length} bytes`);
      res.writeHead(200).end();
      return;
    }
    const baja = url.pathname.match(/^\/api\/v1\/files\/([\w-]+)(\/.*)?$/);
    if (baja && req.method === "GET") {
      const a = archivos.get(baja[1]);
      if (!a || !a.bytes) { log("404"); res.writeHead(404).end(); return; }
      log(`baja ${a.bytes.length} bytes`);
      if (baja[2]) { res.writeHead(200, { "content-type": "application/octet-stream" }).end(a.bytes); return; }
      res.writeHead(200, { "content-type": "application/json" });
      res.end(JSON.stringify({ id: baja[1], filename: a.meta.filename, presignedUrl: { url: `http://127.0.0.1:${puerto}/upload/${baja[1]}`, method: "GET" } }));
      return;
    }
    if (req.method === "GET" && sube) {
      const a = archivos.get(sube[1]);
      if (!a || !a.bytes) { res.writeHead(404).end(); return; }
      res.writeHead(200, { "content-type": "application/octet-stream" }).end(a.bytes);
      return;
    }
    log("sin ruta");
    res.writeHead(404).end();
  })
  .listen(puerto, "127.0.0.1", () => console.log(`stub file-manager en http://127.0.0.1:${puerto}`));
