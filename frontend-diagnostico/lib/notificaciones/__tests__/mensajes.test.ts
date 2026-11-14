import { describe, expect, it } from "vitest";
import { textoNotificacion } from "@/lib/notificaciones/mensajes";
import { aviso } from "./datos";

// Uso de ejemplo: textoNotificacion(item) → { titulo, mensaje } armados en el cliente con rótulos fijos a partir de
// `tipo` y `datos` (la tabla no guarda texto, ADR-0086) y el nombre del lote; null si el tipo no es conocido.

describe("HU14562 AC53 textos de la campana sin PII", () => {
  it("ZIP listo, ZIP fallido, lote terminado y derivación con contadores", () => {
    expect(textoNotificacion(aviso(1, { tipo: "ZIP_EVIDENCIAS_LISTO" }))).toEqual({
      titulo: "ZIP de evidencias listo",
      mensaje: "Las evidencias del lote «Lote flota norte» están listas para descargar.",
    });
    expect(textoNotificacion(aviso(2, { tipo: "ZIP_EVIDENCIAS_FALLIDO" }))?.titulo).toBe("No se pudo generar el ZIP de evidencias");
    expect(textoNotificacion(aviso(3, { tipo: "LOTE_TERMINADO", datos: { estado: "INCOMPLETO" } }))?.mensaje).toBe(
      "El lote «Lote flota norte» terminó con consultas incompletas.",
    );
    expect(textoNotificacion(aviso(4, { tipo: "DERIVACION_TERMINADA", datos: { creadas: 3, omitidas: 0, fallidas: 1 } }))?.mensaje).toBe(
      "Lote «Lote flota norte»: 3 trámites creados, 0 omitidos y 1 fallido.",
    );
  });

  it("ignora cualquier texto en datos: solo usa números y códigos conocidos", () => {
    const t = textoNotificacion(aviso(5, { tipo: "DERIVACION_TERMINADA", datos: { creadas: "ABC123", placa: "ABC123" } }));
    expect(JSON.stringify(t)).not.toContain("ABC123");
  });

  it("sin nombre del lote usa un rótulo genérico", () => {
    expect(textoNotificacion(aviso(6, { tipo: "LOTE_TERMINADO", diagnostico_nombre: null }))?.mensaje).toBe("Tu lote terminó.");
  });

  it("no existe aviso por dictamen ajustado (D8): un tipo desconocido no se pinta", () => {
    expect(textoNotificacion(aviso(7, { tipo: "DICTAMEN_AJUSTADO" as never }))).toBeNull();
  });

  it("forma real del API (sin nombre ni datos, con mensaje del servidor): título fijo del cliente y el mensaje del servidor", () => {
    const real = aviso(8, { tipo: "ZIP_EVIDENCIAS_LISTO", diagnostico_nombre: undefined, referencia_id: undefined, datos: undefined, mensaje: "El ZIP de evidencias del lote «Lote X» está listo para descargar." });
    expect(textoNotificacion(real)).toEqual({ titulo: "ZIP de evidencias listo", mensaje: "El ZIP de evidencias del lote «Lote X» está listo para descargar." });
  });
});

describe("HU14577 AC39 avisos del saneamiento (SP-23: solo tipo y datos)", () => {
  it.each([
    ["SANEAMIENTO_TOMADO", { cantidad: 3 }, "Saneamiento en gestión", "FLIT empezó el saneamiento de 3 vehículos del lote «Lote flota norte»."],
    ["SANEAMIENTO_TOMADO", { cantidad: 1 }, "Saneamiento en gestión", "FLIT empezó el saneamiento de 1 vehículo del lote «Lote flota norte»."],
    ["SANEAMIENTO_CERRADO", { cantidad: 2, resultado: "SANEADO" }, "Saneamiento terminado", "2 vehículos del lote «Lote flota norte» quedaron saneados."],
    ["SANEAMIENTO_CERRADO", { cantidad: 1, resultado: "SANEADO" }, "Saneamiento terminado", "1 vehículo del lote «Lote flota norte» quedó saneado."],
    ["SANEAMIENTO_CERRADO", { cantidad: 1, resultado: "NO_PROCEDE" }, "Saneamiento no procede", "El saneamiento de 1 vehículo del lote «Lote flota norte» no procede."],
    ["SANEAMIENTO_CERRADO", { cantidad: 4, resultado: "NO_PROCEDE" }, "Saneamiento no procede", "El saneamiento de 4 vehículos del lote «Lote flota norte» no procede."],
    ["SANEAMIENTO_REABIERTO", { cantidad: 1 }, "Saneamiento reabierto", "FLIT reabrió el saneamiento de un vehículo del lote «Lote flota norte»."],
    ["SANEAMIENTO_ANULADO", { cantidad: 1 }, "Saneamiento anulado", "FLIT anuló el saneamiento de un vehículo del lote «Lote flota norte»."],
  ])("%s con %j → «%s: %s»", (tipo, datos, titulo, mensaje) => {
    expect(textoNotificacion(aviso(20, { tipo: tipo as never, datos }))).toEqual({ titulo, mensaje });
  });

  it("sin cantidad numérica (o menor que 1) o con un resultado desconocido no se pinta", () => {
    expect(textoNotificacion(aviso(21, { tipo: "SANEAMIENTO_TOMADO" as never, datos: {} }))).toBeNull();
    expect(textoNotificacion(aviso(22, { tipo: "SANEAMIENTO_TOMADO" as never, datos: { cantidad: 0 } }))).toBeNull();
    expect(textoNotificacion(aviso(23, { tipo: "SANEAMIENTO_TOMADO" as never, datos: { cantidad: "3" } }))).toBeNull();
    expect(textoNotificacion(aviso(24, { tipo: "SANEAMIENTO_CERRADO" as never, datos: { cantidad: 1, resultado: "OTRO" } }))).toBeNull();
  });

  it("nunca interpola nada fuera de datos: ni placa ni otros textos, aunque lleguen", () => {
    const t = textoNotificacion(aviso(25, { tipo: "SANEAMIENTO_TOMADO" as never, datos: { cantidad: 1, placa: "ABC123", documento: "900000001" } }));
    expect(JSON.stringify(t)).not.toMatch(/ABC123|900000001/);
  });

  it("sin nombre del lote usa «de tu lote»", () => {
    expect(textoNotificacion(aviso(26, { tipo: "SANEAMIENTO_ANULADO" as never, diagnostico_nombre: null, datos: { cantidad: 1 } }))?.mensaje).toBe(
      "FLIT anuló el saneamiento de un vehículo de tu lote.",
    );
  });
});
