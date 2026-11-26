import { describe, expect, it } from "vitest";
import { chipItem, esPlacaNoInscrita, esRelanzable, mensajeError, mensajePlacaNoInscrita } from "@/lib/liquidaciones/estados";

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
