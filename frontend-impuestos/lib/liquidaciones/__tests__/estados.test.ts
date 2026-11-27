import { describe, expect, it } from "vitest";
import {
  CONTADORES,
  ERRORES_LIQUIDACIONES,
  ESTADOS_ITEM,
  PANTALLA,
  chipFila,
  chipItem,
  esPlacaNoInscrita,
  esRelanzable,
  mensajeError,
  mensajePlacaNoInscrita,
} from "@/lib/liquidaciones/estados";

// Uso de ejemplo: mensajeError({ estado: "ERROR", errorCodigo: "source_inactive", errorMensaje: null, departamento: { nombre: "Boyacá" } })
// → «El proveedor tiene deshabilitado temporalmente el departamento de Boyacá. Puedes relanzar la placa más tarde.»
// esPlacaNoInscrita reconoce ERROR + portal_rejected + «no se encuentra inscrita»; esa placa y DATOS_INCOMPLETOS se relanzan.

const NO_INSCRITA = "EviewDetailVehicles: La placa ingresada no se encuentra inscrita en nuestra base de datos.";
const base = { estado: "ERROR", errorClase: null, errorCodigo: null, errorMensaje: null, departamento: { codigo: "15", nombre: "Boyacá" } } as const;
const ficha = (parcial: Record<string, unknown> = {}) => ({ ...base, ...parcial }) as never;

describe("mensajeError — catálogo por código cuando el proveedor no manda mensaje", () => {
  it("source_inactive con departamento lo nombra", () => {
    expect(mensajeError(ficha({ errorCodigo: "source_inactive" }))).toBe(
      "El proveedor tiene deshabilitado temporalmente el departamento de Boyacá. Puedes relanzar la placa más tarde.",
    );
  });

  it("source_inactive sin departamento dice «este departamento»", () => {
    expect(mensajeError(ficha({ errorCodigo: "source_inactive", departamento: null }))).toBe(
      "El proveedor tiene deshabilitado temporalmente este departamento. Puedes relanzar la placa más tarde.",
    );
    expect(mensajeError(ficha({ errorCodigo: "source_inactive", departamento: { codigo: "15", nombre: "  " } }))).toBe(
      "El proveedor tiene deshabilitado temporalmente este departamento. Puedes relanzar la placa más tarde.",
    );
  });

  it("captcha_blocked con el literal aprobado", () => {
    expect(mensajeError(ficha({ errorCodigo: "captcha_blocked" }))).toBe("El portal del departamento bloqueó el ingreso. Relánzala en unos minutos.");
  });

  it.each([
    "invalid_params",
    "owner_validation_failed",
    "vehicle_data_required",
    "portal_rejected",
    "auth_failed",
    "rate_limited",
    "timeout",
    "http_status",
    "proxy_failed",
    "adapter_unavailable",
    "parse_error",
    "unexpected",
    "worker_lost",
  ])("%s tiene texto propio, sin el código ni el respaldo", (codigo) => {
    const texto = mensajeError(ficha({ errorCodigo: codigo }));
    expect(texto).not.toBe("El proveedor no pudo liquidar esta placa.");
    expect(texto).not.toContain(codigo);
    expect(texto.trim().endsWith(".")).toBe(true);
  });

  it("código desconocido o sin código: el respaldo de siempre", () => {
    expect(mensajeError(ficha({ errorCodigo: "PROVIDER_TIMEOUT" }))).toBe("El proveedor no pudo liquidar esta placa.");
    expect(mensajeError(ficha())).toBe("El proveedor no pudo liquidar esta placa.");
  });

  it("si el proveedor mandó mensaje, manda el del proveedor", () => {
    expect(mensajeError(ficha({ errorCodigo: "source_inactive", errorMensaje: "  Fuente apagada.  " }))).toBe("Fuente apagada.");
  });
});

describe("esPlacaNoInscrita y su mensaje", () => {
  it("reconoce ERROR + portal_rejected + «no se encuentra inscrita» sin distinguir mayúsculas", () => {
    expect(esPlacaNoInscrita(ficha({ errorCodigo: "portal_rejected", errorMensaje: NO_INSCRITA }))).toBe(true);
    expect(esPlacaNoInscrita(ficha({ errorCodigo: "portal_rejected", errorMensaje: "LA PLACA NO SE ENCUENTRA INSCRITA" }))).toBe(true);
  });

  it("no la reconoce con otro código, otro estado u otro mensaje", () => {
    expect(esPlacaNoInscrita(ficha({ errorCodigo: "invalid_params", errorMensaje: NO_INSCRITA }))).toBe(false);
    expect(esPlacaNoInscrita(ficha({ estado: "DATOS_INCOMPLETOS", errorCodigo: "portal_rejected", errorMensaje: NO_INSCRITA }))).toBe(false);
    expect(esPlacaNoInscrita(ficha({ errorCodigo: "portal_rejected", errorMensaje: "Documento no coincide." }))).toBe(false);
    expect(esPlacaNoInscrita(ficha({ errorCodigo: "portal_rejected", errorMensaje: null }))).toBe(false);
  });

  it("final «en nuestra base de datos.» se reemplaza", () => {
    expect(mensajePlacaNoInscrita(NO_INSCRITA)).toBe(
      "EviewDetailVehicles: La placa ingresada no se encuentra inscrita en nuestra base de datos o no tiene vigencias pendientes.",
    );
  });

  it("otro final: se quita el punto y se agrega « o no tiene vigencias pendientes.»", () => {
    expect(mensajePlacaNoInscrita("La placa no se encuentra inscrita.")).toBe("La placa no se encuentra inscrita o no tiene vigencias pendientes.");
    expect(mensajePlacaNoInscrita("La placa no se encuentra inscrita")).toBe("La placa no se encuentra inscrita o no tiene vigencias pendientes.");
  });
});

describe("esRelanzable", () => {
  it("INCIERTO_REQUIERE_DECISION y ERROR REINTENTABLE, como antes", () => {
    expect(esRelanzable(ficha({ estado: "INCIERTO_REQUIERE_DECISION" }))).toBe(true);
    expect(esRelanzable(ficha({ errorClase: "REINTENTABLE", errorCodigo: "source_inactive" }))).toBe(true);
  });

  it("DATOS_INCOMPLETOS pasa a ser relanzable", () => {
    expect(esRelanzable(ficha({ estado: "DATOS_INCOMPLETOS", campoFaltante: "OwnerEmail" }))).toBe(true);
  });

  it("la placa no inscrita (clase DEPENDE) es relanzable; otro portal_rejected no", () => {
    expect(esRelanzable(ficha({ errorClase: "DEPENDE", errorCodigo: "portal_rejected", errorMensaje: NO_INSCRITA }))).toBe(true);
    expect(esRelanzable(ficha({ errorClase: "DEPENDE", errorCodigo: "portal_rejected", errorMensaje: "Documento no coincide." }))).toBe(false);
  });

  it("EXITOSO y ERROR CORREGIR_DATOS no", () => {
    expect(esRelanzable(ficha({ estado: "EXITOSO" }))).toBe(false);
    expect(esRelanzable(ficha({ errorClase: "CORREGIR_DATOS", errorCodigo: "invalid_params" }))).toBe(false);
  });
});

describe("chipItem — placa no inscrita en advertencia", () => {
  it("ERROR de placa no inscrita: «Con novedad» en warning", () => {
    expect(chipItem(ficha({ errorCodigo: "portal_rejected", errorMensaje: NO_INSCRITA, resultadoStatus: "error" }))).toEqual({ texto: "Con novedad", tono: "warning" });
  });

  it("otro ERROR sigue en danger", () => {
    expect(chipItem(ficha({ errorCodigo: "portal_rejected", errorMensaje: "Documento no coincide.", resultadoStatus: "error" }))).toEqual({ texto: "Con error", tono: "danger" });
  });
});

describe("chipFila — placa no inscrita en la tabla principal (ResumenLiquidacion.placaNoInscrita)", () => {
  const contadores = { exitosas: 0, conDeuda: 0, pazYSalvo: 0, error: 1, datosIncompletos: 0, departamentoNoHabilitado: 0, incierto: 0, sinResultado: 0, pendientes: 0, noAplica: 0 };
  const individual = { tipo: "INDIVIDUAL", estado: "COMPLETADO_CON_NOVEDADES", estadoItem: "ERROR", contadores } as const;

  it("individual en ERROR con placaNoInscrita true: «Con novedad» en warning", () => {
    expect(chipFila({ ...individual, placaNoInscrita: true })).toEqual({ texto: "Con novedad", tono: "warning" });
  });

  it("individual en ERROR sin el campo (backend viejo), con null o con false: «Con error» en danger", () => {
    expect(chipFila(individual)).toEqual({ texto: "Con error", tono: "danger" });
    expect(chipFila({ ...individual, placaNoInscrita: null })).toEqual({ texto: "Con error", tono: "danger" });
    expect(chipFila({ ...individual, placaNoInscrita: false })).toEqual({ texto: "Con error", tono: "danger" });
  });

  it("placaNoInscrita true solo cuenta en ERROR: otro estadoItem sigue con su chip", () => {
    expect(chipFila({ ...individual, estadoItem: "DATOS_INCOMPLETOS", placaNoInscrita: true })).toEqual(
      chipFila({ ...individual, estadoItem: "DATOS_INCOMPLETOS" }),
    );
  });

  it("la masiva no cambia: lleva el chip del lote aunque llegue placaNoInscrita", () => {
    const masiva = { tipo: "MASIVA", estado: "COMPLETADO_CON_NOVEDADES", estadoItem: null, contadores } as const;
    expect(chipFila({ ...masiva, placaNoInscrita: true })).toEqual(chipFila(masiva));
    expect(chipFila({ ...masiva, placaNoInscrita: null })).toEqual(chipFila(masiva));
  });
});

describe("contrato 1.3.0 — NO_APLICA (servicio público) y relanzar como nuevo intento", () => {
  const SERVICIO_PUBLICO = "El RUNT reporta el vehículo como de servicio público. Los vehículos de servicio público no pagan impuesto vehicular.";
  const AYUDA = "Vehículo de servicio público: no paga impuesto vehicular";
  const noAplica = ficha({ estado: "NO_APLICA", errorCodigo: "servicio_publico", resultadoStatus: null });
  const ceros = { exitosas: 0, conDeuda: 0, pazYSalvo: 0, error: 0, datosIncompletos: 0, departamentoNoHabilitado: 0, incierto: 0, sinResultado: 0, pendientes: 0, noAplica: 0 };

  it("chipItem NO_APLICA: «No aplica» en neutral con la ayuda del servicio público (ni error ni advertencia)", () => {
    expect(chipItem(noAplica)).toEqual({ texto: "No aplica", tono: "neutral", ayuda: AYUDA });
  });

  it("chipFila: la individual con estadoItem NO_APLICA dice «No aplica»; sin estadoItem lo deduce de contadores.noAplica", () => {
    expect(chipFila({ tipo: "INDIVIDUAL", estado: "COMPLETADO_CON_NOVEDADES", estadoItem: "NO_APLICA", contadores: { ...ceros, noAplica: 1 } })).toEqual({
      texto: "No aplica",
      tono: "neutral",
      ayuda: AYUDA,
    });
    expect(chipFila({ tipo: "INDIVIDUAL", estado: "COMPLETADO_CON_NOVEDADES", estadoItem: null, contadores: { ...ceros, noAplica: 1 } })).toEqual({
      texto: "No aplica",
      tono: "neutral",
      ayuda: AYUDA,
    });
  });

  it("etiquetas del contrato: ESTADOS_ITEM.NO_APLICA y CONTADORES.noAplica", () => {
    expect(ESTADOS_ITEM.NO_APLICA).toBe("No aplica (servicio público)");
    expect(CONTADORES.noAplica).toBe("No aplica");
  });

  it("mensajeError servicio_publico sin errorMensaje: el texto del contrato", () => {
    expect(mensajeError(noAplica)).toBe(SERVICIO_PUBLICO);
  });

  it("esRelanzable: NO_APLICA nunca; con el lote CERRADO_MANUALMENTE tampoco; con otro estado del lote, como antes", () => {
    expect(esRelanzable(noAplica)).toBe(false);
    const reintentable = ficha({ errorClase: "REINTENTABLE", errorCodigo: "timeout" });
    expect(esRelanzable(reintentable, "CERRADO_MANUALMENTE")).toBe(false);
    expect(esRelanzable(ficha({ estado: "INCIERTO_REQUIERE_DECISION" }), "CERRADO_MANUALMENTE")).toBe(false);
    expect(esRelanzable(reintentable, "COMPLETADO_CON_NOVEDADES")).toBe(true);
    expect(esRelanzable(reintentable, null)).toBe(true);
  });

  it("literales 1.3.0: NO_RELANZABLE nuevo y confirmación del nuevo intento", () => {
    expect(ERRORES_LIQUIDACIONES.NO_RELANZABLE).toBe(
      "Solo se relanzan placas en «Incierto — requiere decisión», con datos incompletos, con error reintentable o con placa no inscrita, que no estén en curso y cuya liquidación no se haya cerrado a mano.",
    );
    expect(PANTALLA.confirmarRelanzar).toBe("La placa se volverá a consultar como un nuevo intento de este mismo registro. El intento actual quedará en el historial.");
  });
});
