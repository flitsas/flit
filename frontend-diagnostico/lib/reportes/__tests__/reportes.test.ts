import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import {
  ErrorDescargaReporte,
  consultaComoQuery,
  descargarReporte,
  mensajeDescargaReporte,
  obtenerCatalogo,
  obtenerReporte,
  rutaReporte,
} from "@/lib/reportes/api";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";
import { catalogoBackoffice, catalogoCompania, paginaVehiculos } from "./datosReportes";

// Uso de ejemplo: obtenerCatalogo(backoffice) → GET /reportes o /backoffice/reportes; obtenerReporte(ambito, "vehiculos",
// { desde, hasta, filtros, placa, pagina, tamano }) → PaginaReporteDto; descargarReporte(ambito, tipo, consulta, "excel")
// baja el blob con el nombre del Content-Disposition y libera la URL; 422/429 lanzan ErrorDescargaReporte.

const EMPRESA = "01920000-0000-7000-8000-0000000000a1";
const consulta = { desde: "2026-10-17", hasta: "2026-11-15", filtros: { estado: ["ERROR", "NA"] }, placa: "abc", pagina: 2, tamano: 20 };

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

describe("rutas de reportes por ámbito (contrato §0, mapa R/B)", () => {
  it("compañía, backoffice global y backoffice por compañía, en pantalla y en archivo", () => {
    expect(rutaReporte({ tipo: "COMPANIA" }, "vehiculos")).toBe("/reportes/vehiculos");
    expect(rutaReporte({ tipo: "COMPANIA" }, "vehiculos", "excel")).toBe("/reportes/vehiculos/excel");
    expect(rutaReporte({ tipo: "GLOBAL" }, "consumo", "pdf")).toBe("/backoffice/reportes/consumo/global/pdf");
    expect(rutaReporte({ tipo: "EMPRESA", empresa: EMPRESA }, "consultas")).toBe("/backoffice/reportes/consultas");
  });

  it("la query lleva desde, hasta, los enums repetidos, la placa normalizada y la paginación; empresa solo por compañía", () => {
    const q = consultaComoQuery({ tipo: "EMPRESA", empresa: EMPRESA }, consulta, true);
    expect(q.getAll("estado")).toEqual(["ERROR", "NA"]);
    expect(q.get("placa")).toBe("ABC");
    expect(q.get("pagina")).toBe("2");
    expect(q.get("tamano")).toBe("20");
    expect(q.get("empresa")).toBe(EMPRESA);
    const archivo = consultaComoQuery({ tipo: "GLOBAL" }, consulta, false);
    expect(archivo.has("pagina")).toBe(false);
    expect(archivo.has("empresa")).toBe(false);
  });
});

describe("obtenerCatalogo y obtenerReporte", () => {
  it("el catálogo sale de /reportes o de /backoffice/reportes", async () => {
    const fetchMock = simularApi({
      "GET /reportes": () => Response.json(catalogoCompania()),
      "GET /backoffice/reportes": () => Response.json(catalogoBackoffice()),
    });
    expect((await obtenerCatalogo(false)).reportes).toHaveLength(4);
    expect((await obtenerCatalogo(true)).reportes).toHaveLength(5);
    expect(llamadas(fetchMock, "GET", "/reportes")).toHaveLength(1);
  });

  it("el reporte en pantalla pide GET /reportes/{tipo} con la query", async () => {
    const fetchMock = simularApi({ "GET /reportes/vehiculos": () => Response.json(paginaVehiculos()) });
    const pagina = await obtenerReporte({ tipo: "COMPANIA" }, "vehiculos", consulta);
    expect(pagina.total).toBe(1830);
    const [url] = llamadas(fetchMock, "GET", "/reportes/vehiculos")[0];
    expect(new URL(String(url), "http://localhost").searchParams.get("desde")).toBe("2026-10-17");
  });
});

describe("descargarReporte — Blob, nombre del Content-Disposition y revokeObjectURL (SP-18)", () => {
  it("baja el archivo con el nombre del servidor y libera la URL", async () => {
    simularApi({
      "GET /reportes/vehiculos/excel": () =>
        new Response(new Blob(["xlsx"]), { status: 200, headers: { "Content-Disposition": 'attachment; filename="diagnostico-reporte-vehiculos-20261017-20261115.xlsx"' } }),
    });
    const nombre = await descargarReporte({ tipo: "COMPANIA" }, "vehiculos", consulta, "excel");
    expect(nombre).toBe("diagnostico-reporte-vehiculos-20261017-20261115.xlsx");
    expect(descargados).toEqual([nombre]);
    await vi.waitFor(() => expect(URL.revokeObjectURL).toHaveBeenCalledWith("blob:prueba"));
  });

  it("422 REPORTE_DEMASIADO_GRANDE trae filas y máximo y no descarga", async () => {
    simularApi({
      "GET /reportes/vehiculos/pdf": () => Response.json({ code: "REPORTE_DEMASIADO_GRANDE", error: "x", filas: 6120, maximo: 1000 }, { status: 422 }),
    });
    const error = await descargarReporte({ tipo: "COMPANIA" }, "vehiculos", consulta, "pdf").catch((e: unknown) => e);
    expect(error).toBeInstanceOf(ErrorDescargaReporte);
    expect((error as ErrorDescargaReporte).filas).toBe(6120);
    expect((error as ErrorDescargaReporte).maximo).toBe(1000);
    expect(mensajeDescargaReporte(error, "pdf")).toBe("El reporte tiene 6.120 registros y el PDF admite 1.000. Acota el rango o los filtros.");
    expect(descargados).toEqual([]);
  });

  it("429 EXPORTACION_EN_CURSO pide esperar", async () => {
    simularApi({
      "GET /reportes/vehiculos/excel": () =>
        Response.json({ code: "EXPORTACION_EN_CURSO", error: "x" }, { status: 429, headers: { "Retry-After": "10" } }),
    });
    const error = await descargarReporte({ tipo: "COMPANIA" }, "vehiculos", consulta, "excel").catch((e: unknown) => e);
    expect(mensajeDescargaReporte(error, "excel")).toBe("Ya hay una exportación en curso. Espera 10 segundos e intenta de nuevo.");
  });
});
