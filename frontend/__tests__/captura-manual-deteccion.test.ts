import { describe, expect, it } from "vitest";
import {
  ANALYSIS_MARGIN,
  DOC_ASPECT,
  DOC_FRAME,
  analyzeDocumentFrame,
  decideGuidance,
  frameRectInVideo,
  motionBetween,
  toGray,
} from "@/lib/captura-manual/documentDetection";

const W = 192;
const H = 124;
// Marco dentro de la miniatura (con margen alrededor).
const INNER = { x: 24, y: 18, w: 144, h: 88 };

function image(pixel: (x: number, y: number) => number): Uint8ClampedArray {
  const g = new Uint8ClampedArray(W * H);
  for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) g[y * W + x] = pixel(x, y);
  return g;
}
const dentro = (x: number, y: number) => x >= INNER.x && x < INNER.x + INNER.w && y >= INNER.y && y < INNER.y + INNER.h;
/** Texto sintético: franjas finas oscuras sobre papel claro. */
const papel = (x: number, y: number) => (x % 4 < 2 || y % 5 < 2 ? 215 : 70);

describe("detección del documento (HU #13294)", () => {
  it("el marco conserva la proporción de cédula", () => {
    const r = frameRectInVideo(1920, 1080);
    expect(r.w / r.h).toBeCloseTo(DOC_ASPECT, 2);
    // video 16:9: el visor 10/9 muestra el centro; el marco queda dentro de esa porción
    expect(r.x).toBeCloseTo(360 + DOC_FRAME.x * 1200, 3);
    expect(r.y).toBeCloseTo(DOC_FRAME.y * 1080, 3);
  });

  it("la región analizada y el recorte crecen alrededor del marco sin salirse del video", () => {
    const base = frameRectInVideo(1280, 720);
    const grown = frameRectInVideo(1280, 720, ANALYSIS_MARGIN);
    expect(grown.x).toBeLessThan(base.x);
    expect(grown.w).toBeGreaterThan(base.w);
    const extremo = frameRectInVideo(1280, 720, 5);
    expect(extremo.x).toBeGreaterThanOrEqual(0);
    expect(extremo.x + extremo.w).toBeLessThanOrEqual(1280);
    expect(extremo.y + extremo.h).toBeLessThanOrEqual(720);
  });

  it("pared lisa: «empty»", () => {
    const a = analyzeDocumentFrame(image(() => 120), W, H, INNER);
    expect(decideGuidance(a, 0)).toBe("empty");
  });

  it("documento con texto dentro del marco y fondo distinto: «ready»", () => {
    const a = analyzeDocumentFrame(image((x, y) => (dentro(x, y) ? papel(x, y) : 40)), W, H, INNER);
    expect(a.alignedSides).toBe(4);
    expect(decideGuidance(a, 0)).toBe("ready");
  });

  it("documento más grande que el marco (no se ve su borde): «adjust»", () => {
    const a = analyzeDocumentFrame(image((x, y) => papel(x, y)), W, H, INNER);
    expect(a.alignedSides).toBeLessThan(3);
    expect(decideGuidance(a, 0)).toBe("adjust");
  });

  it("documento borroso (sin detalle fino): «blurry»", () => {
    const suave = (x: number, y: number) => 150 + 20 * Math.sin(x / 18) + 15 * Math.cos(y / 14);
    const a = analyzeDocumentFrame(image((x, y) => (dentro(x, y) ? suave(x, y) : 30)), W, H, INNER);
    expect(a.texture).toBeGreaterThan(0);
    expect(decideGuidance(a, 0)).not.toBe("ready");
  });

  it("en movimiento no se captura: «moving»", () => {
    const a = analyzeDocumentFrame(image((x, y) => (dentro(x, y) ? papel(x, y) : 40)), W, H, INNER);
    expect(decideGuidance(a, 30)).toBe("moving");
  });

  it("motionBetween mide 0 para cuadros iguales y crece con la diferencia", () => {
    const a = image(() => 100);
    expect(motionBetween(a, image(() => 100))).toBe(0);
    expect(motionBetween(a, image(() => 140))).toBeGreaterThan(30);
    expect(motionBetween(a, new Uint8ClampedArray(3))).toBe(Infinity);
  });

  it("toGray pondera el color (luma)", () => {
    const g = toGray([255, 0, 0, 255, 0, 255, 0, 255], 2, 1);
    expect(g[0]).toBeLessThan(g[1]);
  });
});
