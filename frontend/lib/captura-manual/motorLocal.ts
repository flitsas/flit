// Motor de validación de fotos que corre EN EL DISPOSITIVO (Feature #14307). Dos librerías, ambas cargadas
// bajo demanda (import dinámico) solo cuando la persona llega a un paso de captura, y servidas desde FLIT:
//  - Rostros: MediaPipe Tasks Vision (Apache-2.0) + modelo BlazeFace short-range (Apache-2.0).
//  - Códigos: zxing-wasm, solo el lector (MIT): PDF417 de la cédula amarilla y QR de la digital.
// Los archivos pesados (.wasm y modelo) están en `public/captura-manual/` (ver scripts/copiar-motores-fotos.mjs):
// nada se pide a un CDN ni se envía una imagen a la red. Un fallo de carga se trata como «sin soporte».
import type { FaceDetector } from "@mediapipe/tasks-vision";
import { THRESHOLDS, analyzeDocumentFrame, toGray } from "./documentDetection";
import { SinSoporteError, type MotorFotos, type RostrosDetectados } from "./validacionFotos";

/** Ruta pública (mismo origen) de los motores. */
export const RUTA_MOTORES = "/captura-manual";
const RUTA_VISION = `${RUTA_MOTORES}/vision`;
const RUTA_ZXING = `${RUTA_MOTORES}/zxing`;
const RUTA_MODELO_ROSTRO = `${RUTA_MOTORES}/modelos/blaze_face_short_range.tflite`;

/** Confianza mínima del detector: evita contar manchas como rostros. */
const CONFIANZA_ROSTRO = 0.6;
/** Ancho de la miniatura con la que se mide la textura del documento. */
const ANCHO_TEXTURA = 192;
/** Ancho máximo con el que se buscan códigos: la foto guardada ya mide como mucho 1280 px de lado. */
const ANCHO_CODIGOS = 1280;
/**
 * Giros (grados) con los que se reintenta la lectura. El PDF417 de la cédula amarilla solo se lee con una
 * inclinación de unos 2-3°; una foto a mano suele estar más torcida, así que se prueba enderezarla.
 */
const GIROS_GRADOS = [0, 5, -5, 10, -10, 15, -15];

let detectorRostro: Promise<FaceDetector> | null = null;
let lectorZxing: Promise<typeof import("zxing-wasm/reader")> | null = null;

function exigirNavegador(): void {
  if (typeof createImageBitmap !== "function" || typeof document === "undefined" || typeof WebAssembly === "undefined") {
    throw new SinSoporteError();
  }
}

function cargarDetector(): Promise<FaceDetector> {
  detectorRostro ??= (async () => {
    const { FaceDetector, FilesetResolver } = await import("@mediapipe/tasks-vision");
    const vision = await FilesetResolver.forVisionTasks(RUTA_VISION);
    return FaceDetector.createFromOptions(vision, {
      baseOptions: { modelAssetPath: RUTA_MODELO_ROSTRO, delegate: "CPU" },
      runningMode: "IMAGE",
      minDetectionConfidence: CONFIANZA_ROSTRO,
    });
  })().catch((e: unknown) => {
    detectorRostro = null; // un fallo de red no debe quedar fijado para toda la sesión
    throw new SinSoporteError(e instanceof Error ? e.message : undefined);
  });
  return detectorRostro;
}

function cargarZxing(): Promise<typeof import("zxing-wasm/reader")> {
  lectorZxing ??= (async () => {
    const zx = await import("zxing-wasm/reader");
    // Por defecto la librería baja el .wasm de jsDelivr: se redirige al archivo servido por FLIT.
    await zx.prepareZXingModule({
      overrides: { locateFile: (archivo: string, prefijo: string) => (archivo.endsWith(".wasm") ? `${RUTA_ZXING}/${archivo}` : prefijo + archivo) },
      fireImmediately: true,
    });
    return zx;
  })().catch((e: unknown) => {
    lectorZxing = null;
    throw new SinSoporteError(e instanceof Error ? e.message : undefined);
  });
  return lectorZxing;
}

async function aBitmap(foto: Blob): Promise<ImageBitmap> {
  exigirNavegador();
  try {
    return await createImageBitmap(foto);
  } catch {
    throw new SinSoporteError("No se pudo leer la foto");
  }
}

function pixeles(bmp: ImageBitmap, ancho: number, giroGrados = 0): ImageData {
  const alto = Math.max(8, Math.round((ancho * bmp.height) / bmp.width));
  const canvas = document.createElement("canvas");
  canvas.width = ancho;
  canvas.height = alto;
  const ctx = canvas.getContext("2d", { willReadFrequently: true });
  if (!ctx) throw new SinSoporteError();
  if (giroGrados) {
    ctx.fillStyle = "#fff";
    ctx.fillRect(0, 0, ancho, alto);
    ctx.translate(ancho / 2, alto / 2);
    ctx.rotate((giroGrados * Math.PI) / 180);
    ctx.drawImage(bmp, -ancho / 2, -alto / 2, ancho, alto);
  } else {
    ctx.drawImage(bmp, 0, 0, ancho, alto);
  }
  return ctx.getImageData(0, 0, ancho, alto);
}

export const motorLocal: MotorFotos = {
  precargar(kind, sinReverso) {
    try {
      exigirNavegador();
    } catch {
      return;
    }
    // Calentamiento silencioso de lo que ese paso usa: si falla, la validación lo reintenta y, si insiste,
    // es «sin soporte». Sin reverso (pasaporte) el anverso y el reverso solo miden textura: no cargan nada.
    if (kind === "rostro" || (kind === "anverso" && !sinReverso)) void cargarDetector().catch(() => undefined);
    if (kind === "reverso" && !sinReverso) void cargarZxing().catch(() => undefined);
  },

  async rostros(foto): Promise<RostrosDetectados> {
    const bmp = await aBitmap(foto);
    try {
      const detector = await cargarDetector();
      const { detections } = detector.detect(bmp);
      const cajas = detections.flatMap((d) =>
        d.boundingBox ? [{ x: d.boundingBox.originX, y: d.boundingBox.originY, w: d.boundingBox.width, h: d.boundingBox.height }] : [],
      );
      return { imgW: bmp.width, imgH: bmp.height, cajas };
    } finally {
      bmp.close();
    }
  },

  async hayCodigo(foto) {
    const bmp = await aBitmap(foto);
    try {
      const zx = await cargarZxing();
      const ancho = Math.min(bmp.width, ANCHO_CODIGOS);
      for (const giro of GIROS_GRADOS) {
        const lecturas = await zx.readBarcodes(pixeles(bmp, ancho, giro), {
          formats: ["PDF417", "QRCode", "Code128", "Code39", "DataMatrix"],
          tryHarder: true,
          tryRotate: true,
          tryInvert: true,
          maxNumberOfSymbols: 1,
        });
        if (lecturas.some((l) => l.isValid)) return true;
      }
      return false;
    } catch (e) {
      throw e instanceof SinSoporteError ? e : new SinSoporteError(e instanceof Error ? e.message : undefined);
    } finally {
      bmp.close();
    }
  },

  async hayDocumento(foto) {
    const bmp = await aBitmap(foto);
    try {
      const datos = pixeles(bmp, ANCHO_TEXTURA);
      const gris = toGray(datos.data, datos.width, datos.height);
      // La foto de documento ya es el recorte del marco: se mide todo su interior.
      const { texture } = analyzeDocumentFrame(gris, datos.width, datos.height, { x: 0, y: 0, w: datos.width, h: datos.height });
      return texture >= THRESHOLDS.texture;
    } finally {
      bmp.close();
    }
  },
};

