import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import { formatearPesos, obtenerDetalleCelda, rutaDetalleCelda, textoMotivoNa, textoTipoError, traducirErrorDetalle } from "@/lib/tablero/detalle";
import { simitVehiculo } from "./datosDetalle";

// Uso de ejemplo: const detalle = await obtenerDetalleCelda(loteId, vehiculoId, "SIMIT_VEHICULO", signal);
// si falla, traducirErrorDetalle(error) dice si no está disponible (404), si falta permiso (403) o el mensaje de la API.

afterEach(() => vi.unstubAllGlobals());

const espacios = (texto: string) => texto.replace(/\s/g, " ");

describe("detalle — URL por vehículo y fuente", () => {
  it("arma /diagnosticos/{id}/vehiculos/{vid}/consultas/{fuente}", () => {
    expect(rutaDetalleCelda("lote-1", "vehiculo-2", "SIMIT_VEHICULO")).toBe("/diagnosticos/lote-1/vehiculos/vehiculo-2/consultas/SIMIT_VEHICULO");
  });

  it("codifica los segmentos de la ruta", () => {
    expect(rutaDetalleCelda("a/b", "c d", "RUNT")).toBe("/diagnosticos/a%2Fb/vehiculos/c%20d/consultas/RUNT");
  });

  it("obtenerDetalleCelda hace GET por el BFF y devuelve el detalle tal cual", async () => {
    const cuerpo = simitVehiculo();
    const fetchMock = vi.fn(async () => Response.json(cuerpo));
    vi.stubGlobal("fetch", fetchMock);
    await expect(obtenerDetalleCelda("lote-1", "vehiculo-2", "SIMIT_VEHICULO")).resolves.toEqual(cuerpo);
    expect(fetchMock).toHaveBeenCalledTimes(1);
    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/diagnostico/diagnosticos/lote-1/vehiculos/vehiculo-2/consultas/SIMIT_VEHICULO");
    expect(init.method).toBe("GET");
  });
});

describe("detalle — traduce 403 y 404", () => {
  it("404 NO_ENCONTRADO: el detalle no está disponible", () => {
    const e = traducirErrorDetalle(new ApiError(404, "No existe", "NO_ENCONTRADO"));
    expect(e.tipo).toBe("NO_DISPONIBLE");
    expect(e.titulo).toMatch(/no está disponible/i);
  });

  it("403: sin permiso, con o sin código", () => {
    expect(traducirErrorDetalle(new ApiError(403, "x")).tipo).toBe("SIN_PERMISO");
    const e = traducirErrorDetalle(new ApiError(403, "x", "SIN_EMPRESA"));
    expect(e.tipo).toBe("SIN_PERMISO");
    expect(e.titulo).toMatch(/permiso/i);
  });

  it("otro error: FALLO con el mensaje de la API", () => {
    const e = traducirErrorDetalle(new ApiError(500, "Diagnóstico no respondió. Intenta de nuevo en un momento."));
    expect(e).toEqual({ tipo: "FALLO", titulo: "No se pudo cargar el detalle", mensaje: "Diagnóstico no respondió. Intenta de nuevo en un momento." });
  });

  it("un error que no es de la API no rompe la traducción", () => {
    expect(traducirErrorDetalle("raro").tipo).toBe("FALLO");
  });
});

describe("detalle — textos de error, N/A y pesos", () => {
  it.each([
    ["TIMEOUT", /no respondió a tiempo/i],
    ["RED", /conexión/i],
    ["PERSONAS_CON_ERROR", /personas no se pudieron consultar/i],
    ["BLOQUEADA_POR_RUNT", /RUNT del vehículo/i],
    ["SIN_PLACA", /no tiene placa/i],
    ["SIN_IDENTIFICADOR", /ni placa ni VIN/i],
    ["NO_CONECTADO", /no está conectada/i],
  ])("%s se traduce", (codigo, texto) => {
    expect(textoTipoError(codigo)).toMatch(texto);
  });

  it("un código desconocido o nulo da un texto genérico, sin mostrar el código crudo", () => {
    expect(textoTipoError("ALGO_NUEVO")).toBe("La fuente respondió con un error.");
    expect(textoTipoError(null)).toBe("La fuente respondió con un error.");
  });

  it("motivos de N/A", () => {
    expect(textoMotivoNa("SERVICIO_PUBLICO")).toMatch(/servicio público/i);
    expect(textoMotivoNa("DEPARTAMENTO_NO_SOPORTADO")).toMatch(/departamento/i);
    expect(textoMotivoNa("SIN_DOCUMENTO_LOCATARIO")).toMatch(/locatario/i);
    expect(textoMotivoNa(null)).toBe("Esta consulta no aplica a este vehículo.");
  });

  it("pesos sin decimales a partir de texto o número; vacío como guion", () => {
    expect(espacios(formatearPesos("500000"))).toBe("$ 500.000");
    expect(espacios(formatearPesos(45000000))).toBe("$ 45.000.000");
    expect(formatearPesos(null)).toBe("—");
    expect(formatearPesos("no-numero")).toBe("—");
  });
});
