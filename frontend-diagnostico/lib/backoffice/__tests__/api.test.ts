import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import {
  EmpresaNoElegida,
  aplicarLlenado,
  esLlenable,
  listarLotesBackoffice,
  llenarConsultaManual,
  obtenerFormularioManual,
  obtenerGrillaBackoffice,
  traducirErrorLlenado,
} from "@/lib/backoffice/api";
import { EMPRESA_A, LOTE, celdaBo, grillaBo, listaLotes, lote, respuestaLlenado, simularApi } from "./datos";

// Uso de ejemplo: listarLotesBackoffice(empresa, { conErrores: true }) → GET /backoffice/diagnosticos?empresa=…&con_errores=true;
// obtenerGrillaBackoffice(id, empresa, 0); obtenerFormularioManual(cid, empresa); llenarConsultaManual(cid, empresa, cuerpo).
// Sin una compañía válida ninguna sale a la red (rechaza con EmpresaNoElegida).

afterEach(() => vi.unstubAllGlobals());

const urlDe = (fetchMock: { mock: { calls: unknown[][] } }, n = 0) => new URL(String(fetchMock.mock.calls[n][0]), "http://localhost");

describe("lib/backoffice/api — toda llamada lleva ?empresa=", () => {
  it("lista de lotes con empresa, estado, con_errores, pagina y tamano", async () => {
    const fetchMock = simularApi({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([lote(1)])) });

    const lista = await listarLotesBackoffice(EMPRESA_A, { estado: "INCOMPLETO", conErrores: true, pagina: 2, tamano: 25 });

    const url = urlDe(fetchMock);
    expect(url.pathname).toBe("/api/v1/diagnostico/backoffice/diagnosticos");
    expect(Object.fromEntries(url.searchParams)).toEqual({ empresa: EMPRESA_A, estado: "INCOMPLETO", con_errores: "true", pagina: "2", tamano: "25" });
    expect(lista.diagnosticos).toHaveLength(1);
  });

  it("sin filtros solo manda la empresa y la página 1", async () => {
    const fetchMock = simularApi({ "GET /backoffice/diagnosticos": () => Response.json(listaLotes([])) });
    await listarLotesBackoffice(EMPRESA_A);
    expect(Object.fromEntries(urlDe(fetchMock).searchParams)).toEqual({ empresa: EMPRESA_A, pagina: "1" });
  });

  it("grilla del lote con empresa y desde", async () => {
    const fetchMock = simularApi({ [`GET /backoffice/diagnosticos/${LOTE}/grilla`]: () => Response.json(grillaBo()) });
    await obtenerGrillaBackoffice(LOTE, EMPRESA_A, 7);
    const url = urlDe(fetchMock);
    expect(url.searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(url.searchParams.get("desde")).toBe("7");
  });

  it("formulario GET y llenado PUT con empresa; el PUT manda el cuerpo JSON", async () => {
    const fetchMock = simularApi({
      "GET /backoffice/consultas/celda-1/manual": () => Response.json({ id: "celda-1" }),
      "PUT /backoffice/consultas/celda-1/manual": () => Response.json(respuestaLlenado("SIMIT_VEHICULO")),
    });
    await obtenerFormularioManual("celda-1", EMPRESA_A);
    const cuerpo = { version: 10, resultado: { semaforo: "VERDE", cantidad_comparendos: 0, comparendos: [] } };
    const r = await llenarConsultaManual("celda-1", EMPRESA_A, cuerpo);

    expect(urlDe(fetchMock, 0).searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(urlDe(fetchMock, 1).searchParams.get("empresa")).toBe(EMPRESA_A);
    const init = fetchMock.mock.calls[1][1] as RequestInit;
    expect(init.method).toBe("PUT");
    expect(JSON.parse(String(init.body))).toEqual(cuerpo);
    expect(r.origen).toBe("MANUAL");
  });

  it.each(["", "   ", "no-es-uuid", "00000000-0000-0000-0000-000000000000"])("sin compañía válida («%s») no se llama a la API", async (empresa) => {
    const fetchMock = simularApi({});
    await expect(listarLotesBackoffice(empresa)).rejects.toBeInstanceOf(EmpresaNoElegida);
    await expect(obtenerGrillaBackoffice(LOTE, empresa, 0)).rejects.toBeInstanceOf(EmpresaNoElegida);
    await expect(obtenerFormularioManual("celda-1", empresa)).rejects.toBeInstanceOf(EmpresaNoElegida);
    await expect(llenarConsultaManual("celda-1", empresa, { version: 1, resultado: {} })).rejects.toBeInstanceOf(EmpresaNoElegida);
    expect(fetchMock).not.toHaveBeenCalled();
  });
});

describe("esLlenable — «Llenar a mano» solo en celdas en ERROR no bloqueadas de fuentes llenables", () => {
  it.each([
    ["SIMIT_VEHICULO", "ERROR", "TIMEOUT", true],
    ["AVALUO", "ERROR", "HTTP_5XX", true],
    ["RUNT_PROPIETARIO", "ERROR", "PERSONAS_CON_ERROR", true],
    ["SIMIT_LOCATARIO", "ERROR", "TIMEOUT", true],
    ["RUNT", "ERROR", "TIMEOUT", false],
    ["IMPUESTO_DEPARTAMENTAL", "ERROR", "TIMEOUT", false],
    ["SIMIT_PROPIETARIO", "ERROR", "BLOQUEADA_POR_RUNT", false],
    ["SIMIT_VEHICULO", "COMPLETADO", null, false],
    ["SIMIT_VEHICULO", "NA", null, false],
  ] as const)("%s en %s (%s) → %s", (fuente, estado, tipo_error, esperado) => {
    expect(esLlenable(celdaBo(1, fuente, estado, { tipo_error }))).toBe(esperado);
  });
});

describe("traducirErrorLlenado — el 422 va junto al campo, el 409 como aviso", () => {
  it("422 RESULTADO_INVALIDO con campo → error de campo con el mensaje de la API", () => {
    const e = traducirErrorLlenado(new ApiError(422, "El valor debe ser mayor que cero.", "RESULTADO_INVALIDO", "resultado.fuentes[0].valor"));
    expect(e).toEqual({ tipo: "CAMPO", campo: "resultado.fuentes[0].valor", mensaje: "El valor debe ser mayor que cero.", recargarGrilla: false });
  });

  it("422 PERSONA_NO_EN_ERROR con campo → error de campo de la persona", () => {
    const e = traducirErrorLlenado(new ApiError(422, "La persona no está en error.", "PERSONA_NO_EN_ERROR", "personas[0].persona_id"));
    expect(e.tipo).toBe("CAMPO");
    expect(e.campo).toBe("personas[0].persona_id");
  });

  it("409 CELDA_MODIFICADA → aviso que pide recargar la grilla", () => {
    const e = traducirErrorLlenado(new ApiError(409, "x", "CELDA_MODIFICADA"));
    expect(e.tipo).toBe("AVISO");
    expect(e.recargarGrilla).toBe(true);
    expect(e.mensaje).toMatch(/cambió/i);
  });

  it.each(["CELDA_NO_EN_ERROR", "CELDA_BLOQUEADA_POR_RUNT"])("409 %s → aviso claro, sin campo", (code) => {
    const e = traducirErrorLlenado(new ApiError(409, "x", code));
    expect(e.tipo).toBe("AVISO");
    expect(e.campo).toBeUndefined();
    expect(e.recargarGrilla).toBe(true);
  });

  it("422 FUENTE_NO_ADMITE_MANUAL y 403 → aviso", () => {
    expect(traducirErrorLlenado(new ApiError(422, "x", "FUENTE_NO_ADMITE_MANUAL")).tipo).toBe("AVISO");
    expect(traducirErrorLlenado(new ApiError(403, "x")).mensaje).toMatch(/permiso/i);
  });
});

describe("aplicarLlenado — la celda pasa a verde con origen manual", () => {
  it("celda COMPLETADO, origen MANUAL con su usuario, sin tipo_error, y el estado del lote de la respuesta", () => {
    const g = aplicarLlenado(grillaBo(), respuestaLlenado("SIMIT_VEHICULO", { estado_lote: "EN_PROCESO" }));
    const c = g.vehiculos[0].celdas.find((x) => x.fuente === "SIMIT_VEHICULO")!;
    expect(c).toMatchObject({ estado: "COMPLETADO", origen: "MANUAL", usuario_manual_id: expect.any(String), tipo_error: null, version: 21 });
    expect(g.estado).toBe("EN_PROCESO");
  });

  it("el vehículo queda terminado si ya no le quedan celdas abiertas ni en error", () => {
    let g = aplicarLlenado(grillaBo(), respuestaLlenado("SIMIT_VEHICULO"));
    expect(g.vehiculos[0].terminado).toBe(false);
    g = aplicarLlenado(g, respuestaLlenado("AVALUO", { id: "celda-1-avaluo", estado_lote: "COMPLETADO" }));
    expect(g.vehiculos[0].terminado).toBe(true);
    expect(g.avance.terminados).toBe(1);
  });
});
