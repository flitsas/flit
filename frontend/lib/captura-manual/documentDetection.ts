// Detección liviana del documento en el visor (sin librerías): mide, dentro del marco guía, si hay un
// documento (textura), si está bien encuadrado (contraste entre el borde interior y exterior del marco en
// sus cuatro lados), si está enfocado (varianza del Laplaciano) y si está quieto (diferencia entre cuadros).
// Todo sobre una miniatura en grises: barato y sin enviar nada a ningún servidor.

/** Proporción de una cédula (ancho/alto). */
export const DOC_ASPECT = 1.586;
/** Proporción del visor de documento (ancho/alto): el contenedor usa `aspect-[10/9]`. */
export const VIEWER_ASPECT = 10 / 9;

/** Marco guía como fracción del visor. Alto derivado de la proporción de la cédula. */
export const DOC_FRAME = {
  x: 0.06,
  y: 0.08,
  w: 0.88,
  h: 0.88 / DOC_ASPECT / (1 / VIEWER_ASPECT),
} as const;

/** Margen (fracción del alto/ancho del marco) que se analiza alrededor del marco para ver su borde. */
export const ANALYSIS_MARGIN = 0.08;
/** Holgura (fracción) con la que se recorta la captura alrededor del marco. */
export const CROP_PADDING = 0.015;

export interface Rect {
  x: number;
  y: number;
  w: number;
  h: number;
}

/**
 * Rectángulo del marco guía en píxeles del VIDEO. El visor muestra el video con `object-cover`: se ve el
 * centro del video recortado a la proporción del visor, y el marco se mide sobre esa porción visible.
 * `grow` agranda el rectángulo (fracción del marco) y se limita a los bordes del video.
 */
export function frameRectInVideo(videoW: number, videoH: number, grow = 0): Rect {
  const videoAspect = videoW / videoH;
  let visW = videoW;
  let visH = videoH;
  if (videoAspect > VIEWER_ASPECT) visW = videoH * VIEWER_ASPECT;
  else visH = videoW / VIEWER_ASPECT;
  const offX = (videoW - visW) / 2;
  const offY = (videoH - visH) / 2;
  const fx = offX + DOC_FRAME.x * visW;
  const fy = offY + DOC_FRAME.y * visH;
  const fw = DOC_FRAME.w * visW;
  const fh = DOC_FRAME.h * visH;
  const gx = fw * grow;
  const gy = fh * grow;
  const x = Math.max(0, fx - gx);
  const y = Math.max(0, fy - gy);
  const x2 = Math.min(videoW, fx + fw + gx);
  const y2 = Math.min(videoH, fy + fh + gy);
  return { x, y, w: x2 - x, h: y2 - y };
}

export type GuidanceState = "empty" | "adjust" | "blurry" | "moving" | "ready";

export interface FrameAnalysis {
  /** Detalle medio dentro del marco (0 = pared lisa). */
  texture: number;
  /** Varianza del Laplaciano dentro del marco (enfoque). */
  sharpness: number;
  /** Lados del marco (0–4) donde el interior y el exterior contrastan: el documento llega al marco. */
  alignedSides: number;
}

export const THRESHOLDS = {
  texture: 5,
  sharpness: 40,
  contrast: 14,
  alignedSides: 3,
  motion: 6,
} as const;

/** Mensajes del aviso de encuadre (cortos, en el tono del flujo). */
export const GUIDANCE_HINT: Record<GuidanceState, string> = {
  empty: "Muestra el documento dentro del marco",
  adjust: "Ajusta el documento al marco",
  blurry: "Enfoca: aleja un poco el documento",
  moving: "Mantén el documento quieto",
  ready: "¡Perfecto! No te muevas…",
};

/** Convierte RGBA a grises (luma). */
export function toGray(rgba: Uint8ClampedArray | number[], width: number, height: number): Uint8ClampedArray {
  const out = new Uint8ClampedArray(width * height);
  for (let i = 0, p = 0; i < out.length; i++, p += 4) {
    out[i] = (rgba[p] * 299 + rgba[p + 1] * 587 + rgba[p + 2] * 114) / 1000;
  }
  return out;
}

function bandMean(gray: Uint8ClampedArray, width: number, x0: number, y0: number, x1: number, y1: number): number {
  let sum = 0;
  let n = 0;
  for (let y = Math.max(0, y0); y < y1; y++) {
    for (let x = Math.max(0, x0); x < x1; x++) {
      sum += gray[y * width + x];
      n++;
    }
  }
  return n ? sum / n : 0;
}

/**
 * Analiza una miniatura en grises de la región (marco + margen). `inner` es el marco dentro de esa miniatura.
 */
export function analyzeDocumentFrame(gray: Uint8ClampedArray, width: number, height: number, inner: Rect): FrameAnalysis {
  const t = Math.max(2, Math.round(inner.w * 0.05));
  const ix0 = Math.round(inner.x);
  const iy0 = Math.round(inner.y);
  const ix1 = Math.min(width, Math.round(inner.x + inner.w));
  const iy1 = Math.min(height, Math.round(inner.y + inner.h));

  // Interior sin la banda del borde: textura y enfoque.
  const sx0 = ix0 + t;
  const sy0 = iy0 + t;
  const sx1 = ix1 - t;
  const sy1 = iy1 - t;
  let grad = 0;
  let lapSum = 0;
  let lapSq = 0;
  let n = 0;
  for (let y = sy0 + 1; y < sy1 - 1; y++) {
    for (let x = sx0 + 1; x < sx1 - 1; x++) {
      const c = gray[y * width + x];
      const gx = gray[y * width + x + 1] - gray[y * width + x - 1];
      const gy = gray[(y + 1) * width + x] - gray[(y - 1) * width + x];
      grad += (Math.abs(gx) + Math.abs(gy)) / 2;
      const lap = gray[y * width + x - 1] + gray[y * width + x + 1] + gray[(y - 1) * width + x] + gray[(y + 1) * width + x] - 4 * c;
      lapSum += lap;
      lapSq += lap * lap;
      n++;
    }
  }
  const texture = n ? grad / n : 0;
  const sharpness = n ? lapSq / n - (lapSum / n) ** 2 : 0;

  // Contraste a cada lado del borde del marco: dentro (documento) frente a fuera (fondo).
  const sides: Array<[number, number]> = [
    [bandMean(gray, width, ix0 + t, iy0, ix1 - t, iy0 + t), bandMean(gray, width, ix0 + t, iy0 - t, ix1 - t, iy0)],
    [bandMean(gray, width, ix0 + t, iy1 - t, ix1 - t, iy1), bandMean(gray, width, ix0 + t, iy1, ix1 - t, Math.min(height, iy1 + t))],
    [bandMean(gray, width, ix0, iy0 + t, ix0 + t, iy1 - t), bandMean(gray, width, ix0 - t, iy0 + t, ix0, iy1 - t)],
    [bandMean(gray, width, ix1 - t, iy0 + t, ix1, iy1 - t), bandMean(gray, width, ix1, iy0 + t, Math.min(width, ix1 + t), iy1 - t)],
  ];
  const alignedSides = sides.filter(([dentro, fuera]) => Math.abs(dentro - fuera) >= THRESHOLDS.contrast).length;
  return { texture, sharpness, alignedSides };
}

/** Diferencia media absoluta entre dos miniaturas del mismo tamaño (0 = quieto). */
export function motionBetween(a: Uint8ClampedArray, b: Uint8ClampedArray): number {
  if (a.length !== b.length || a.length === 0) return Infinity;
  let sum = 0;
  for (let i = 0; i < a.length; i += 3) sum += Math.abs(a[i] - b[i]);
  return sum / Math.ceil(a.length / 3);
}

/** Estado de la guía a partir del análisis y del movimiento (prioridad: lo que más urge arreglar primero). */
export function decideGuidance(analysis: FrameAnalysis, motion: number): GuidanceState {
  if (analysis.texture < THRESHOLDS.texture) return "empty";
  if (analysis.alignedSides < THRESHOLDS.alignedSides) return "adjust";
  if (analysis.sharpness < THRESHOLDS.sharpness) return "blurry";
  if (motion > THRESHOLDS.motion) return "moving";
  return "ready";
}
