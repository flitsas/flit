import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import { columnasVisibles, filtrosVacios, mensajeExportacion, obtenerCatalogosReportes, obtenerReporte, urlExportacion, type FiltrosReporte } from "@/lib/reportes/api";

// Uso de ejemplo:
//   urlExportacion("detalle", "pdf", filtros, empresa)      → /api/v1/impuestos/reportes/detalle/pdf?<mismos filtros que el GET>
//   obtenerCatalogosReportes(empresa)                       → { departamentos, usuarios: [{ id, nombre }] } (GET /catalogos/reportes)
//   mensajeExportacion(error422, "pdf")                     → literal TOPE_EXPORTACION con {tope} (del cuerpo o del formato)
//   columnasVisibles(columnas, vistaGlobal)                 → «Compañía» primero solo en la vista global del Super Admin
// Contrato: impuestos.v1.yaml 1.2.x, deltas 9, 11 y 12.

const EMPRESA = "00000000-0000-7000-8000-0000000000e2";
const filtros: FiltrosReporte = {
  rango: { desde: "2026-11-01", hasta: "2026-11-24" },
  tipoLiquidacion: "MASIVA",
  estado: "ERROR",
  departamento: "25",
  usuario: "00000000-0000-7000-8000-0000000000c1",
  placa: " ABC1 ",
};

afterEach(() => vi.unstubAllGlobals());

function capturar(respuesta: () => Response = () => Response.json({})) {
  const urls: URL[] = [];
  vi.stubGlobal(
    "fetch",
    vi.fn(async (u: RequestInfo | URL) => {
      urls.push(new URL(String(u), "http://app"));
      return respuesta();
    }),
  );
  return urls;
}

describe("urlExportacion — delta 9: mismos filtros que GET /reportes/{tipo}", () => {
  it.each(["detalle", "lotes", "deuda", "errores"] as const)("en %s la exportación lleva los mismos parámetros que el GET salvo la paginación", async (tipo) => {
    const urls = capturar(() => Response.json({ tipo, columnas: [], items: [], total: 0, page: 1, pageSize: 50 }));
    await obtenerReporte(tipo, filtros, { page: 3, pageSize: 50 }, EMPRESA);
    const get = new URLSearchParams(urls[0].searchParams);
    get.delete("page");
    get.delete("pageSize");
    for (const formato of ["excel", "pdf"] as const) {
      const exp = new URL(urlExportacion(tipo, formato, filtros, EMPRESA), "http://app");
      expect(exp.pathname).toBe(`/api/v1/impuestos/reportes/${tipo}/${formato}`);
      expect(Object.fromEntries([...exp.searchParams].sort())).toEqual(Object.fromEntries([...get].sort()));
      expect(exp.searchParams.has("page")).toBe(false);
    }
  });

  it("sin filtros solo viajan desde y hasta (y empresa si la hay)", () => {
    const exp = new URL(urlExportacion("detalle", "excel", filtrosVacios({ desde: "2026-11-01", hasta: "2026-11-24" }), null), "http://app");
    expect([...exp.searchParams.keys()].sort()).toEqual(["desde", "hasta"]);
  });
});

describe("obtenerCatalogosReportes — delta 12: GET /catalogos/reportes", () => {
  it("pide la ruta propia con ?empresa y devuelve departamentos y usuarios (id y nombre)", async () => {
    const urls = capturar(() =>
      Response.json({ departamentos: [{ codigo: "25", nombre: "Cundinamarca", activa: true }], usuarios: [{ id: "u-1", nombre: "Gestora Demo" }] }),
    );
    const catalogos = await obtenerCatalogosReportes(EMPRESA);
    expect(urls[0].pathname).toBe("/api/v1/impuestos/catalogos/reportes");
    expect(urls[0].searchParams.get("empresa")).toBe(EMPRESA);
    expect(catalogos).toEqual({ departamentos: [{ codigo: "25", nombre: "Cundinamarca", activa: true }], usuarios: [{ id: "u-1", nombre: "Gestora Demo" }] });
  });

  it("sin empresa (vista global del SA o usuario de compañía) no envía el parámetro", async () => {
    const urls = capturar(() => Response.json({ departamentos: [], usuarios: [] }));
    await obtenerCatalogosReportes(null);
    expect(urls[0].searchParams.has("empresa")).toBe(false);
  });

  it("tolera listas ausentes o nulas y descarta usuarios sin id o sin nombre", async () => {
    capturar(() => Response.json({ usuarios: [{ id: "u-1", nombre: "" }, { id: "", nombre: "X" }, { id: "u-2", nombre: "Ana" }], departamentos: null }));
    expect(await obtenerCatalogosReportes(null)).toEqual({ departamentos: [], usuarios: [{ id: "u-2", nombre: "Ana" }] });
  });

  it("un error de la API se propaga como ApiError", async () => {
    capturar(() => Response.json({ code: "EMPRESA_REQUERIDA", error: "Elige una compañía." }, { status: 400 }));
    await expect(obtenerCatalogosReportes("no-uuid")).rejects.toBeInstanceOf(ApiError);
  });
});

describe("mensajeExportacion — 422 TOPE_EXPORTACION con {tope}", () => {
  const literal = (tope: number) => `El reporte supera el máximo de ${tope} filas para exportar: ajusta los filtros.`;

  it("usa el `tope` del cuerpo del error", () => {
    expect(mensajeExportacion(new ApiError(422, "x", "TOPE_EXPORTACION", { tope: 5000 }), "excel")).toBe(literal(5000));
  });

  it("sin `tope` en el cuerpo usa el del formato (Excel 5000, PDF 1000), nunca deja «{tope}»", () => {
    expect(mensajeExportacion(new ApiError(422, "x", "TOPE_EXPORTACION"), "pdf")).toBe(literal(1000));
    expect(mensajeExportacion(new ApiError(422, "x", "TOPE_EXPORTACION"), "excel")).toBe(literal(5000));
  });

  it("otro fallo da el genérico", () => {
    expect(mensajeExportacion(new Error("x"), "pdf")).toBe("No se pudo generar el archivo. Intenta de nuevo.");
  });
});

describe("columnasVisibles — delta 11: «Compañía» solo en la vista global del Super Admin", () => {
  const base = [
    { clave: "placa", etiqueta: "Placa" },
    { clave: "estado", etiqueta: "Estado" },
  ];

  it("en la vista global pone «Compañía» primero, la traiga o no `columnas`", () => {
    expect(columnasVisibles(base, true).map((c) => c.clave)).toEqual(["empresa", "placa", "estado"]);
    expect(columnasVisibles([...base, { clave: "empresa", etiqueta: "Compañía" }], true)).toEqual([{ clave: "empresa", etiqueta: "Compañía" }, ...base]);
  });

  it("fuera de la vista global quita la columna aunque llegue", () => {
    expect(columnasVisibles([{ clave: "empresa", etiqueta: "Compañía" }, ...base], false)).toEqual(base);
  });
});
