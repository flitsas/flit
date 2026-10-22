import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import {
  consultaListado,
  destinoDiagnostico,
  etiquetaEstado,
  filtrosDesdeUrl,
  hayFiltros,
  idCorto,
  listarDiagnosticos,
  nombreModo,
  traducirErrorListado,
  urlListado,
} from "@/lib/diagnosticos/listado";
import { llamadas, simularApi } from "@/lib/tablero/__tests__/datos";
import { diagnostico, filtros, lista } from "./datosListado";

// Uso de ejemplo: const f = filtrosDesdeUrl(new URLSearchParams("estado=COMPLETADO&nombre=flota"));
// listarDiagnosticos(f) pide GET /api/v1/diagnostico/diagnosticos?estado=COMPLETADO&nombre=flota&pagina=1&tamano=25 y
// urlListado(f) da «/?estado=COMPLETADO&nombre=flota», la URL del inicio que se comparte o a la que se vuelve atrás.

afterEach(() => vi.unstubAllGlobals());

describe("filtros del listado — consulta desde la URL (AC2)", () => {
  it("lee desde, hasta, modo, estado, nombre y pagina de la URL", () => {
    const f = filtrosDesdeUrl(new URLSearchParams("desde=2026-10-01&hasta=2026-10-22&modo=SIMPLE&estado=COMPLETADO&nombre=Flota&pagina=3"));
    expect(f).toEqual({ desde: "2026-10-01", hasta: "2026-10-22", modo: "SIMPLE", estado: "COMPLETADO", nombre: "Flota", pagina: 3, tamano: 25 });
  });

  it("descarta valores que la API no admite: CREADO, minúsculas, fechas mal formadas y páginas no válidas", () => {
    expect(filtrosDesdeUrl(new URLSearchParams("estado=CREADO&modo=simple&desde=22/10/2026&pagina=0"))).toEqual(filtros());
    expect(filtrosDesdeUrl(new URLSearchParams("estado=completado&pagina=abc&tamano=500"))).toEqual(filtros());
    expect(filtrosDesdeUrl(new URLSearchParams(""))).toEqual(filtros());
  });

  it("la consulta a la API omite los filtros vacíos, recorta el nombre y lleva siempre página y tamaño", () => {
    const q = new URLSearchParams(consultaListado(filtros({ estado: "COMPLETADO", nombre: "  Flota  ", pagina: 2 })));
    expect(Object.fromEntries(q)).toEqual({ estado: "COMPLETADO", nombre: "Flota", pagina: "2", tamano: "25" });
  });

  it("la URL del inicio refleja los filtros y omite la página 1 y el tamaño por defecto", () => {
    expect(urlListado(filtros())).toBe("/");
    expect(urlListado(filtros({ estado: "COMPLETADO", nombre: "Flota" }))).toBe("/?estado=COMPLETADO&nombre=Flota");
    expect(urlListado(filtros({ desde: "2026-10-01", hasta: "2026-10-22", modo: "CON_ANALISIS", pagina: 2, tamano: 50 }))).toBe(
      "/?desde=2026-10-01&hasta=2026-10-22&modo=CON_ANALISIS&pagina=2&tamano=50",
    );
  });

  it("hayFiltros distingue la compañía sin diagnósticos de los filtros sin resultados (AC5)", () => {
    expect(hayFiltros(filtros())).toBe(false);
    expect(hayFiltros(filtros({ pagina: 4, tamano: 50 }))).toBe(false);
    expect(hayFiltros(filtros({ nombre: "x" }))).toBe(true);
    expect(hayFiltros(filtros({ desde: "2026-10-01" }))).toBe(true);
    expect(hayFiltros(filtros({ modo: "SIMPLE" }))).toBe(true);
    expect(hayFiltros(filtros({ estado: "INCOMPLETO" }))).toBe(true);
  });
});

describe("listarDiagnosticos — contrato GET /diagnosticos (HU #13845)", () => {
  it("pide GET /diagnosticos con la consulta de los filtros y devuelve la lista", async () => {
    const respuesta = lista([diagnostico(1)]);
    const fetchMock = simularApi({ "GET /diagnosticos": () => Response.json(respuesta) });
    const r = await listarDiagnosticos(filtros({ modo: "SIMPLE", nombre: "Flota" }));
    expect(r).toEqual(respuesta);
    const [[url]] = llamadas(fetchMock, "GET", "/diagnosticos");
    expect(Object.fromEntries(new URL(String(url), "http://localhost").searchParams)).toEqual({ modo: "SIMPLE", nombre: "Flota", pagina: "1", tamano: "25" });
  });

  it("rechaza una respuesta sin la lista de diagnósticos", async () => {
    simularApi({ "GET /diagnosticos": () => Response.json({ pagina: 1 }) });
    await expect(listarDiagnosticos(filtros())).rejects.toThrow();
  });
});

describe("traducirErrorListado — 400 con campo y 403", () => {
  it("400 RANGO_INVALIDO marca el filtro hasta", () => {
    const e = traducirErrorListado(new ApiError(400, "Rango", "RANGO_INVALIDO", "hasta"));
    expect(e).toMatchObject({ tipo: "CAMPO", campo: "hasta" });
    expect(e.tipo === "CAMPO" && e.mensaje).toMatch(/anterior/i);
  });

  it.each(["desde", "hasta", "modo", "estado", "nombre"] as const)("400 FILTRO_INVALIDO con campo %s marca ese filtro", (campo) => {
    expect(traducirErrorListado(new ApiError(400, "Filtro", "FILTRO_INVALIDO", campo))).toMatchObject({ tipo: "CAMPO", campo });
  });

  it("400 PAGINACION_INVALIDA es un aviso que vuelve a la página 1", () => {
    expect(traducirErrorListado(new ApiError(400, "Página", "PAGINACION_INVALIDA", "pagina"))).toMatchObject({ tipo: "AVISO", reiniciarPagina: true });
  });

  it("403 sin cuerpo lleva a la 403 de permisos; 403 con código lleva a la 403 con ese código", () => {
    expect(traducirErrorListado(new ApiError(403, "No"))).toEqual({ tipo: "ACCESO", code: "SIN_PERMISOS_DIAGNOSTICO" });
    expect(traducirErrorListado(new ApiError(403, "No", "PRODUCTO_NO_HABILITADO"))).toEqual({ tipo: "ACCESO", code: "PRODUCTO_NO_HABILITADO" });
  });

  it("cualquier otra falla es un aviso con su mensaje", () => {
    expect(traducirErrorListado(new ApiError(500, "Diagnóstico no respondió."))).toEqual({ tipo: "AVISO", mensaje: "Diagnóstico no respondió.", reiniciarPagina: false });
    expect(traducirErrorListado("x")).toMatchObject({ tipo: "AVISO", reiniciarPagina: false });
  });
});

describe("presentación de cada fila", () => {
  it("un lote lanzado abre su tablero; uno sin lanzar (CREADO) abre su parametrización (AC1, AC3)", () => {
    const d = diagnostico(1);
    expect(destinoDiagnostico(d)).toBe(`/diagnosticos/${d.id}`);
    expect(destinoDiagnostico({ ...d, estado: "CREADO" })).toBe(`/diagnosticos/${d.id}/carga`);
    expect(destinoDiagnostico({ id: "a b", estado: "COMPLETADO" })).toBe("/diagnosticos/a%20b");
  });

  it("rótulos: CREADO es «Sin lanzar»; modo Simple es «Diagnóstico Simple»; ID corto de 8 caracteres", () => {
    expect(etiquetaEstado("CREADO")).toEqual({ texto: "Sin lanzar", tono: "neutral" });
    expect(etiquetaEstado("COMPLETADO")).toEqual({ texto: "Completado", tono: "success" });
    expect(etiquetaEstado("EN_PROCESO").texto).toBe("En proceso");
    expect(etiquetaEstado("INCOMPLETO").texto).toBe("Incompleto");
    expect(nombreModo("SIMPLE")).toBe("Diagnóstico Simple");
    expect(nombreModo("CON_ANALISIS")).toBe("Con Análisis IA");
    expect(idCorto("0192f0a1-0000-7000-8000-000000000001")).toBe("0192F0A1");
  });
});
