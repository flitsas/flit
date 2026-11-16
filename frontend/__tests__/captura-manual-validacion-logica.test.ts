// Feature #14307 (HUs #14308 y #14309): decisión de la validación de fotos en el dispositivo. Sin imágenes
// reales ni librerías de visión: los motores son simulados con cajas sintéticas.
import { describe, expect, it, vi } from "vitest";
import {
  MAX_INTENTOS,
  SinSoporteError,
  armarValidacion,
  documentoSinReverso,
  juzgarAnverso,
  juzgarReverso,
  juzgarSelfie,
  mensajeRechazo,
  noVerificada,
  validarFoto,
  type CajaRostro,
  type MotorFotos,
} from "@/lib/captura-manual/validacionFotos";

// Fotograma de la selfie 1280x720: el cuadrado visible es el centro de 720x720; el óvalo mide 58 % x 78 %.
const SELFIE = { imgW: 1280, imgH: 720 };
const centrado: CajaRostro = { x: 540, y: 210, w: 200, h: 260 }; // centro (640, 340)
const alLado: CajaRostro = { x: 1000, y: 300, w: 150, h: 200 }; // fuera del óvalo
const foto = new Blob(["sintetico"]);

function motor(p: Partial<MotorFotos> = {}): MotorFotos {
  return {
    rostros: vi.fn().mockResolvedValue({ ...SELFIE, cajas: [] }),
    hayCodigo: vi.fn().mockResolvedValue(false),
    hayDocumento: vi.fn().mockResolvedValue(true),
    ...p,
  };
}

describe("selfie (HU #14308)", () => {
  it("AC1: exactamente un rostro dentro del óvalo pasa", () => {
    expect(juzgarSelfie({ ...SELFIE, cajas: [centrado] })).toEqual({ tipo: "ok" });
  });
  it("AC2: sin rostro → sin_rostro", () => {
    expect(juzgarSelfie({ ...SELFIE, cajas: [] })).toEqual({ tipo: "rechazada", motivo: "sin_rostro" });
  });
  it("un rostro fuera del óvalo no cuenta como el de la persona", () => {
    expect(juzgarSelfie({ ...SELFIE, cajas: [alLado] })).toEqual({ tipo: "rechazada", motivo: "sin_rostro" });
  });
  it("AC3: dos rostros dentro del óvalo → varios_rostros", () => {
    const otro: CajaRostro = { x: 560, y: 220, w: 150, h: 200 };
    expect(juzgarSelfie({ ...SELFIE, cajas: [centrado, otro] })).toEqual({ tipo: "rechazada", motivo: "varios_rostros" });
  });
  it("AC3: otra persona relevante fuera del óvalo → varios_rostros; una diminuta al fondo se ignora", () => {
    expect(juzgarSelfie({ ...SELFIE, cajas: [centrado, alLado] })).toEqual({ tipo: "rechazada", motivo: "varios_rostros" });
    const lejana: CajaRostro = { x: 1100, y: 100, w: 30, h: 40 };
    expect(juzgarSelfie({ ...SELFIE, cajas: [centrado, lejana] })).toEqual({ tipo: "ok" });
  });
});

describe("anverso y reverso (HU #14309)", () => {
  const doc = { imgW: 1000, imgH: 630 };
  const retrato: CajaRostro = { x: 700, y: 200, w: 110, h: 140 }; // ≈ 2,4 % del área
  it("AC1: documento con un rostro pequeño pasa", () => {
    expect(juzgarAnverso({ ...doc, cajas: [retrato] }, true)).toEqual({ tipo: "ok" });
  });
  it("AC2: documento sin rostro → lado_incorrecto", () => {
    expect(juzgarAnverso({ ...doc, cajas: [] }, true)).toEqual({ tipo: "rechazada", motivo: "lado_incorrecto" });
  });
  it("AC5: sin documento ni rostro → sin_documento", () => {
    expect(juzgarAnverso({ ...doc, cajas: [] }, false)).toEqual({ tipo: "rechazada", motivo: "sin_documento" });
  });
  it("un rostro que ocupa gran parte de la foto es una selfie, no un documento", () => {
    const selfie: CajaRostro = { x: 250, y: 100, w: 500, h: 430 };
    expect(juzgarAnverso({ ...doc, cajas: [selfie] }, true)).toEqual({ tipo: "rechazada", motivo: "sin_documento" });
  });
  it("AC3: reverso con código pasa", () => {
    expect(juzgarReverso(true, true)).toEqual({ tipo: "ok" });
  });
  it("AC4: reverso sin código pero con documento → lado_incorrecto", () => {
    expect(juzgarReverso(false, true)).toEqual({ tipo: "rechazada", motivo: "lado_incorrecto" });
  });
  it("AC5: reverso sin código ni documento → sin_documento", () => {
    expect(juzgarReverso(false, false)).toEqual({ tipo: "rechazada", motivo: "sin_documento" });
  });
});

describe("mensajes exactos", () => {
  it("usa los textos cerrados", () => {
    expect(mensajeRechazo("rostro", "sin_rostro")).toBe("No vemos tu rostro");
    expect(mensajeRechazo("rostro", "varios_rostros")).toBe("Debe aparecer solo una persona");
    expect(mensajeRechazo("anverso", "lado_incorrecto")).toBe("Muestra el lado de tu documento con tu foto");
    expect(mensajeRechazo("reverso", "lado_incorrecto")).toBe("Muestra el lado con el código de barras");
    expect(mensajeRechazo("anverso", "sin_documento")).toMatch(/Encuadra el documento/);
  });
});

describe("validarFoto", () => {
  it("selfie válida / sin rostro, con el motor simulado", async () => {
    const ok = motor({ rostros: vi.fn().mockResolvedValue({ ...SELFIE, cajas: [centrado] }) });
    expect(await validarFoto({ kind: "rostro", foto, motor: ok })).toEqual({ tipo: "ok" });
    expect(await validarFoto({ kind: "rostro", foto, motor: motor() })).toEqual({ tipo: "rechazada", motivo: "sin_rostro" });
  });

  it("anverso sin rostro consulta si es un documento; con rostro no hace falta", async () => {
    const m = motor();
    expect(await validarFoto({ kind: "anverso", foto, motor: m })).toEqual({ tipo: "rechazada", motivo: "lado_incorrecto" });
    expect(m.hayDocumento).toHaveBeenCalledTimes(1);
    const conRostro = motor({ rostros: vi.fn().mockResolvedValue({ imgW: 1000, imgH: 630, cajas: [{ x: 700, y: 200, w: 110, h: 140 }] }) });
    expect(await validarFoto({ kind: "anverso", foto, motor: conRostro })).toEqual({ tipo: "ok" });
    expect(conRostro.hayDocumento).not.toHaveBeenCalled();
  });

  it("reverso: con código pasa sin medir textura; sin código usa el motor de documento", async () => {
    const conCodigo = motor({ hayCodigo: vi.fn().mockResolvedValue(true) });
    expect(await validarFoto({ kind: "reverso", foto, motor: conCodigo })).toEqual({ tipo: "ok" });
    expect(conCodigo.hayDocumento).not.toHaveBeenCalled();
    expect(await validarFoto({ kind: "reverso", foto, motor: motor() })).toEqual({ tipo: "rechazada", motivo: "lado_incorrecto" });
  });

  it("AC7 (#14309): documento sin reverso solo valida que sea un documento, sin rostro ni código", async () => {
    const m = motor();
    expect(await validarFoto({ kind: "anverso", foto, motor: m, sinReverso: true })).toEqual({ tipo: "ok" });
    expect(await validarFoto({ kind: "reverso", foto, motor: m, sinReverso: true })).toEqual({ tipo: "ok" });
    expect(m.rostros).not.toHaveBeenCalled();
    expect(m.hayCodigo).not.toHaveBeenCalled();
    const sinDoc = motor({ hayDocumento: vi.fn().mockResolvedValue(false) });
    expect(await validarFoto({ kind: "reverso", foto, motor: sinDoc, sinReverso: true })).toEqual({ tipo: "rechazada", motivo: "sin_documento" });
  });

  it("AC5 (#14308): si el motor no puede correr, sin_soporte", async () => {
    const roto = motor({ rostros: vi.fn().mockRejectedValue(new SinSoporteError()) });
    expect(await validarFoto({ kind: "rostro", foto, motor: roto })).toEqual({ tipo: "sin_soporte", motivo: "sin_soporte" });
  });

  it("un error inesperado del análisis es sin_resultado", async () => {
    const roto = motor({ rostros: vi.fn().mockRejectedValue(new Error("boom")) });
    expect(await validarFoto({ kind: "rostro", foto, motor: roto })).toEqual({ tipo: "sin_soporte", motivo: "sin_resultado" });
  });

  it("si el análisis supera el tiempo razonable, el dispositivo se trata como sin soporte", async () => {
    vi.useFakeTimers();
    try {
      const colgado = motor({ rostros: vi.fn(() => new Promise<never>(() => undefined)) });
      const p = validarFoto({ kind: "rostro", foto, motor: colgado, timeoutMs: 5000 });
      await vi.advanceTimersByTimeAsync(5001);
      expect(await p).toEqual({ tipo: "sin_soporte", motivo: "sin_soporte" });
    } finally {
      vi.useRealTimers();
    }
  });
});

describe("tipos de documento y contrato", () => {
  it("el pasaporte (PAS) no tiene reverso; las cédulas sí", () => {
    expect(documentoSinReverso("PAS")).toBe(true);
    expect(documentoSinReverso(" pas ")).toBe(true);
    for (const t of ["CC", "CE", "TI", "PPT", "NIT", "", null, undefined]) expect(documentoSinReverso(t)).toBe(false);
  });

  it("armarValidacion respeta la forma del contrato y marca sin_resultado lo que falte", () => {
    expect(armarValidacion({ rostro: { estado: "verificada", motivo: null }, anverso: noVerificada("lado_incorrecto") })).toEqual({
      rostro: { estado: "verificada", motivo: null },
      anverso: { estado: "no_verificada", motivo: "lado_incorrecto" },
      reverso: { estado: "no_verificada", motivo: "sin_resultado" },
    });
  });

  it("la salida se ofrece tras 3 intentos fallidos", () => {
    expect(MAX_INTENTOS).toBe(3);
  });
});
