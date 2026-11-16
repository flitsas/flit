// Copia a `public/captura-manual/` los archivos pesados del motor de validación de fotos (Feature #14307),
// para que FLIT los sirva él mismo (sin CDN ni servicios externos). Se ejecuta antes de `dev` y `build`.
// Salidas (ignoradas por git, ver .gitignore):
//   public/captura-manual/vision/  ← MediaPipe Tasks Vision (Apache-2.0): vision_wasm_internal.{js,wasm}
//   public/captura-manual/zxing/   ← zxing-wasm reader (MIT): zxing_reader.wasm
// El modelo de rostro (modelos/blaze_face_short_range.tflite, Apache-2.0) SÍ está versionado.
import { copyFileSync, existsSync, mkdirSync, statSync } from "node:fs";
import { dirname, join } from "node:path";
import { createRequire } from "node:module";
import { fileURLToPath } from "node:url";

const raiz = join(dirname(fileURLToPath(import.meta.url)), "..");
const require = createRequire(join(raiz, "package.json"));

function carpetaDelPaquete(nombre) {
  // `package.json` de cada paquete: resuelve bien con la estructura de pnpm.
  const candidatos = [join(raiz, "node_modules", nombre)];
  for (const c of candidatos) if (existsSync(join(c, "package.json"))) return c;
  return dirname(require.resolve(`${nombre}/package.json`));
}

const mediapipe = carpetaDelPaquete("@mediapipe/tasks-vision");
const zxing = carpetaDelPaquete("zxing-wasm");

const copias = [
  [join(mediapipe, "wasm", "vision_wasm_internal.js"), join(raiz, "public", "captura-manual", "vision")],
  [join(mediapipe, "wasm", "vision_wasm_internal.wasm"), join(raiz, "public", "captura-manual", "vision")],
  [join(zxing, "dist", "reader", "zxing_reader.wasm"), join(raiz, "public", "captura-manual", "zxing")],
];

for (const [origen, carpeta] of copias) {
  if (!existsSync(origen)) {
    console.error(`copiar-motores-fotos: falta ${origen}. ¿Corriste pnpm install?`);
    process.exit(1);
  }
  mkdirSync(carpeta, { recursive: true });
  const destino = join(carpeta, origen.split(/[\\/]/).pop());
  if (existsSync(destino) && statSync(destino).size === statSync(origen).size) continue;
  copyFileSync(origen, destino);
}
console.log("copiar-motores-fotos: motores de validación de fotos listos en public/captura-manual/");
