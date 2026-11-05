import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import {
  descargarArchivo,
  estadoZip,
  mensajeDescarga,
  nombreArchivo,
  pedirZip,
  puedeEmitirPdf,
  urlArchivoZip,
  urlExcelLote,
  urlPdfVehiculo,
  type EstadoEvidencia,
} from "@/lib/reportes/api";
import { ID, V1, V2, V3, celda, llamadas, simularApi, vehiculo } from "@/lib/tablero/__tests__/datos";

// Uso de ejemplo: <a href={urlPdfVehiculo(id, vid)} download> por el BFF; con JS, descargarArchivo(url, "reporte.pdf")
// baja el blob con el nombre del Content-Disposition y traduce los 409/404 con mensajeDescarga(error). El ZIP:
// pedirZip(id) → estadoZip(id, eid) cada 3 s hasta LISTA o ERROR → urlArchivoZip(id, eid).

const EID = "00000000-0000-7000-8000-0000000ee001";
const zip = (cambios: Partial<EstadoEvidencia> = {}): EstadoEvidencia => ({
  id: EID,
  tipo: "ZIP",
  estado: "PENDIENTE",
  vehiculos_incluidos: 2,
  vehiculos_pendientes: 1,
  creado_en: "2026-10-22T19:30:00+00:00",
  generado_en: null,
  tipo_error: null,
  ...cambios,
});

const pdf = (disposicion: string | null) =>
  new Response(new Blob(["%PDF-1.7 prueba"], { type: "application/pdf" }), {
    status: 200,
    headers: disposicion ? { "Content-Type": "application/pdf", "Content-Disposition": disposicion } : { "Content-Type": "application/pdf" },
  });

let descargados: string[];
beforeEach(() => {
  descargados = [];
  Object.defineProperty(URL, "createObjectURL", { value: vi.fn(() => "blob:prueba"), configurable: true });
  Object.defineProperty(URL, "revokeObjectURL", { value: vi.fn(), configurable: true });
  vi.spyOn(HTMLAnchorElement.prototype, "click").mockImplementation(function (this: HTMLAnchorElement) {
    descargados.push(this.download);
  });
});
afterEach(() => {
  vi.restoreAllMocks();
  vi.unstubAllGlobals();
});

describe("reportes/api — rutas del BFF (PDF, Excel y archivo del ZIP)", () => {
  it("arma la URL del PDF del vehículo bajo /api/v1/diagnostico, codificando los ids", () => {
    expect(urlPdfVehiculo(ID, "vehiculo-1")).toBe(`/api/v1/diagnostico/diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`);
    expect(urlPdfVehiculo("a/b", "c d")).toBe("/api/v1/diagnostico/diagnosticos/a%2Fb/vehiculos/c%20d/pdf");
  });

  it("arma la URL del Excel del lote", () => {
    expect(urlExcelLote(ID)).toBe(`/api/v1/diagnostico/diagnosticos/${ID}/excel`);
  });

  it("arma la URL del archivo del ZIP", () => {
    expect(urlArchivoZip(ID, EID)).toBe(`/api/v1/diagnostico/diagnosticos/${ID}/evidencias/${EID}/archivo`);
  });
});

describe("reportes/api — pedido y estado del ZIP", () => {
  it("pedirZip hace POST /diagnosticos/{id}/evidencias con {tipo:ZIP} y devuelve el estado del 202", async () => {
    const api = simularApi({ [`POST /diagnosticos/${ID}/evidencias`]: () => Response.json(zip(), { status: 202 }) });
    await expect(pedirZip(ID)).resolves.toEqual(zip());
    const [[, init]] = llamadas(api, "POST", `/diagnosticos/${ID}/evidencias`) as [[string, RequestInit]];
    expect(JSON.parse(String(init.body))).toEqual({ tipo: "ZIP" });
  });

  it("pedirZip con 200 (ya hay uno LISTA) devuelve ese estado", async () => {
    simularApi({ [`POST /diagnosticos/${ID}/evidencias`]: () => Response.json(zip({ estado: "LISTA", generado_en: "2026-10-22T19:31:00+00:00" })) });
    await expect(pedirZip(ID)).resolves.toMatchObject({ id: EID, estado: "LISTA" });
  });

  it("pedirZip con 409 SIN_VEHICULOS_TERMINADOS lanza ApiError y se traduce a texto", async () => {
    simularApi({ [`POST /diagnosticos/${ID}/evidencias`]: () => Response.json({ code: "SIN_VEHICULOS_TERMINADOS", error: "x" }, { status: 409 }) });
    const error = await pedirZip(ID).catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe("SIN_VEHICULOS_TERMINADOS");
    expect(mensajeDescarga(error)).toBe("Aún no hay vehículos terminados para armar el ZIP. Inténtalo cuando termine alguno.");
  });

  it("estadoZip hace GET /diagnosticos/{id}/evidencias/{eid}", async () => {
    const api = simularApi({ [`GET /diagnosticos/${ID}/evidencias/${EID}`]: () => Response.json(zip({ estado: "GENERANDO" })) });
    await expect(estadoZip(ID, EID)).resolves.toMatchObject({ estado: "GENERANDO" });
    expect(llamadas(api, "GET", `/diagnosticos/${ID}/evidencias/${EID}`)).toHaveLength(1);
  });
});

describe("reportes/api — descarga por blob con el nombre del Content-Disposition", () => {
  it("prefiere filename*=UTF-8'' y luego filename; sin cabecera, el nombre por defecto", () => {
    expect(nombreArchivo("attachment; filename=diagnostico-0000.pdf; filename*=UTF-8''diagnostico-0000-TST001.pdf", "x.pdf")).toBe("diagnostico-0000-TST001.pdf");
    expect(nombreArchivo('attachment; filename="diagnostico-0000.xlsx"', "x.xlsx")).toBe("diagnostico-0000.xlsx");
    expect(nombreArchivo(null, "x.zip")).toBe("x.zip");
    expect(nombreArchivo("attachment", "x.zip")).toBe("x.zip");
  });

  it("200 application/pdf: baja el blob con el nombre del servidor y devuelve ese nombre", async () => {
    simularApi({ [`GET /diagnosticos/${ID}/vehiculos/vehiculo-1/pdf`]: () => pdf("attachment; filename=diagnostico-00000000-TST001.pdf") });
    await expect(descargarArchivo(urlPdfVehiculo(ID, "vehiculo-1"), "reporte.pdf")).resolves.toBe("diagnostico-00000000-TST001.pdf");
    expect(descargados).toEqual(["diagnostico-00000000-TST001.pdf"]);
    expect(URL.createObjectURL).toHaveBeenCalledTimes(1);
  });

  it("409 VEHICULO_NO_TERMINADO: lanza ApiError, no descarga nada y se traduce a texto", async () => {
    simularApi({ [`GET /diagnosticos/${ID}/vehiculos/vehiculo-2/pdf`]: () => Response.json({ code: "VEHICULO_NO_TERMINADO", error: "x" }, { status: 409 }) });
    const error = await descargarArchivo(urlPdfVehiculo(ID, "vehiculo-2"), "reporte.pdf").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ApiError);
    expect((error as ApiError).code).toBe("VEHICULO_NO_TERMINADO");
    expect(descargados).toEqual([]);
    expect(mensajeDescarga(error)).toBe("Este vehículo todavía tiene consultas en curso. El PDF estará disponible cuando terminen.");
  });

  it("404 NO_ENCONTRADO se traduce a «no encontramos»", async () => {
    simularApi({});
    const error = await descargarArchivo(urlExcelLote("otro"), "lote.xlsx").catch((e: unknown) => e);
    expect((error as ApiError).status).toBe(404);
    expect(mensajeDescarga(error)).toBe("No encontramos este diagnóstico o ya no tienes acceso a él.");
  });

  it("403 sin cuerpo se traduce a falta de permiso", async () => {
    simularApi({ [`GET /diagnosticos/${ID}/excel`]: () => new Response(null, { status: 403 }) });
    const error = await descargarArchivo(urlExcelLote(ID), "lote.xlsx").catch((e: unknown) => e);
    expect(mensajeDescarga(error)).toBe("No tienes permiso para descargar reportes.");
  });

  it("traduce LOTE_SIN_LANZAR y EVIDENCIA_NO_LISTA; un error ajeno usa su mensaje", () => {
    expect(mensajeDescarga(new ApiError(409, "x", "LOTE_SIN_LANZAR"))).toBe("El lote todavía no se ha lanzado: aún no hay nada que descargar.");
    expect(mensajeDescarga(new ApiError(409, "x", "EVIDENCIA_NO_LISTA"))).toBe("El ZIP todavía se está preparando. Espera a que quede listo.");
    expect(mensajeDescarga(new Error("Sin red"))).toBe("Sin red");
    expect(mensajeDescarga("raro")).toBe("No se pudo descargar el archivo.");
  });
});

describe("reportes/api — puedeEmitirPdf, misma regla que EmisionPdf.Puede del backend (R-7/Q7)", () => {
  it("vehículo terminado: sí, aunque el lote siga EN_PROCESO", () => {
    expect(puedeEmitirPdf("EN_PROCESO", V1)).toBe(true);
  });

  it("vehículo sin terminar en un lote EN_PROCESO: no", () => {
    expect(puedeEmitirPdf("EN_PROCESO", V2)).toBe(false);
    expect(puedeEmitirPdf("EN_PROCESO", V3)).toBe(false);
  });

  it("lote INCOMPLETO con celdas en ERROR y todas cerradas: sí", () => {
    const conError = vehiculo(4, [celda(4, "RUNT", "ERROR", { tipo_error: "TIMEOUT" }), celda(4, "SIMIT_PROPIETARIO", "NA")]);
    expect(conError.terminado).toBe(false);
    expect(puedeEmitirPdf("INCOMPLETO", conError)).toBe(true);
  });

  it("lote INCOMPLETO con una celda abierta o sin celdas: no", () => {
    expect(puedeEmitirPdf("INCOMPLETO", vehiculo(5, [celda(5, "RUNT", "ERROR"), celda(5, "SIMIT_PROPIETARIO", "PENDIENTE")]))).toBe(false);
    expect(puedeEmitirPdf("INCOMPLETO", vehiculo(6, [], { terminado: false }))).toBe(false);
  });
});
