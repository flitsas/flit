// HU #13394 (épica #13216, Feature #13308) — cliente de la bandeja OT para crear el lote de
// descarga masiva de consolidados MAESTROS: `POST /api/v1/admin/ot/consolidados/lotes` (contrato
// de #13391, diseño §5), con las mismas cabeceras que el resto de `admin-ot.ts` y los errores
// traducidos a `ConsolidadoLotesApiError` para que la confirmación (#13381) los interprete igual.
//
// Uso de ejemplo:
//   await crearLoteConsolidadosOt(
//     { tipoDocumento: "consolidado_maestro", confirmaEfectos: true, seleccion: lote.modelo },
//     undefined,
//     { transitOfficeId },          // el SuperAdmin lo exige (AC3); el ot_admin lo ignora
//   ) → LoteConsolidados (202)
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";

import { crearLoteConsolidadosOt, type CrearLoteConsolidadosOtRequest } from "../admin-ot";
import { ConsolidadoLotesApiError, interpretarErrorCrearLote } from "../consolidado-lotes-client";
import type { LoteConsolidados } from "../types-consolidado-lotes";
import { TOKEN_STORAGE_KEY } from "@/lib/auth/jwt";

const OT_ID = "aaaaaaaa-0001-4000-8000-000000000001";
const LOTE_ACTIVO_ID = "0f8c6a1e-0000-4000-8000-000000000099";

/** Lote sintético según `LoteConsolidados` del contrato. */
const LOTE: LoteConsolidados = {
  id: "0f8c6a1e-0000-4000-8000-000000000001",
  estado: "en_cola",
  tipoDocumento: "consolidado_maestro",
  total: 118,
  procesados: 0,
  incluidos: 0,
  omitidos: 0,
  creadoEn: "2026-10-07T15:00:00Z",
  partes: [],
};

const CUERPO: CrearLoteConsolidadosOtRequest = {
  tipoDocumento: "consolidado_maestro",
  confirmaEfectos: true,
  seleccion: {
    modo: "filtro",
    ids: [],
    excluidos: ["proc-0001", "proc-0002"],
    filtro: {
      familia: "TRASPASO",
      condiciones: [{ fieldId: "placa", operator: "es_alguno", values: ["AAA111", "BBB222"] }],
    },
  },
};

const fetchMock = vi.fn();
const responder = (status: number, body?: unknown) =>
  fetchMock.mockResolvedValue(
    new Response(body === undefined ? null : JSON.stringify(body), {
      status,
      headers: { "Content-Type": "application/json" },
    }),
  );

async function capturar(p: Promise<unknown>): Promise<unknown> {
  try {
    await p;
  } catch (e) {
    return e;
  }
  throw new Error("se esperaba un error");
}

beforeEach(() => {
  fetchMock.mockReset();
  vi.stubGlobal("fetch", fetchMock);
  window.localStorage.setItem(TOKEN_STORAGE_KEY, "jwt-de-prueba");
});
afterEach(() => {
  vi.unstubAllGlobals();
  window.localStorage.removeItem(TOKEN_STORAGE_KEY);
});

describe("crearLoteConsolidadosOt — HU #13394", () => {
  it("AC2 — 202: POST a /admin/ot/consolidados/lotes con el cuerpo tal cual y devuelve el lote", async () => {
    responder(202, LOTE);
    const lote = await crearLoteConsolidadosOt(CUERPO, undefined, { transitOfficeId: OT_ID });

    expect(lote).toEqual(LOTE);
    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(new URL(url).pathname).toBe("/api/v1/admin/ot/consolidados/lotes");
    expect(init.method).toBe("POST");
    expect(JSON.parse(String(init.body))).toEqual(CUERPO);
  });

  it("W-f — mismas cabeceras que el resto de admin-ot: Bearer del usuario y JSON", async () => {
    responder(202, LOTE);
    await crearLoteConsolidadosOt(CUERPO);
    const [, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(init.headers).toEqual({
      "Content-Type": "application/json",
      Authorization: "Bearer jwt-de-prueba",
    });
  });

  it("AC3 — con alcance de organismo (SuperAdmin) la petición lleva ?transitOfficeId=X", async () => {
    responder(202, LOTE);
    await crearLoteConsolidadosOt(CUERPO, undefined, { transitOfficeId: OT_ID });
    const [url] = fetchMock.mock.calls[0] as [string];
    expect(new URL(url).searchParams.get("transitOfficeId")).toBe(OT_ID);
  });

  it("AC3 — sin alcance no se envía transitOfficeId", async () => {
    responder(202, LOTE);
    await crearLoteConsolidadosOt(CUERPO);
    const [url] = fetchMock.mock.calls[0] as [string];
    expect(new URL(url).searchParams.has("transitOfficeId")).toBe(false);
  });

  it("AC4 — 409 lote_activo: ConsolidadoLotesApiError con el id del lote que ya corre", async () => {
    responder(409, { error: "lote_activo", loteActivoId: LOTE_ACTIVO_ID });
    const err = await capturar(crearLoteConsolidadosOt(CUERPO));
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect(err).toMatchObject({ status: 409, codigo: "lote_activo", loteActivoId: LOTE_ACTIVO_ID });
    expect(interpretarErrorCrearLote(err)).toEqual(
      expect.objectContaining({ tipo: "lote_activo", loteActivoId: LOTE_ACTIVO_ID }),
    );
  });

  it("AC4 — 409 con el id en extensions (ProblemDetails) también se lee", async () => {
    responder(409, { title: "Conflict", extensions: { error: "lote_activo", loteActivoId: LOTE_ACTIVO_ID } });
    const err = await capturar(crearLoteConsolidadosOt(CUERPO));
    expect(err).toMatchObject({ status: 409, loteActivoId: LOTE_ACTIVO_ID });
  });

  it.each([
    ["422 sin detail", 422, { errors: [] }, "tope"],
    ["422 con detail", 422, { detail: "Más de 10.000 excluidos" }, "tope"],
    ["403", 403, { title: "Forbidden" }, "permiso"],
    ["503", 503, { error: "auditoria_no_registrada" }, "reintentar"],
  ])("AC5 — %s: se traduce a ConsolidadoLotesApiError y la UI lo interpreta como «%s»", async (_n, status, body, tipo) => {
    responder(status, body);
    const err = await capturar(crearLoteConsolidadosOt(CUERPO));
    expect(err).toBeInstanceOf(ConsolidadoLotesApiError);
    expect((err as ConsolidadoLotesApiError).status).toBe(status);
    expect(interpretarErrorCrearLote(err).tipo).toBe(tipo);
  });

  it("AC5 — falla de red: status 0 y mensaje de reintento", async () => {
    fetchMock.mockRejectedValue(new TypeError("Failed to fetch"));
    const err = await capturar(crearLoteConsolidadosOt(CUERPO));
    expect(err).toMatchObject({ status: 0 });
    expect(interpretarErrorCrearLote(err).mensaje).toBe("No se pudo completar la descarga, intente de nuevo");
  });

  it("contrato — una cancelación (AbortError) se propaga tal cual, sin convertirse en error de red", async () => {
    const abort = new DOMException("aborted", "AbortError");
    fetchMock.mockRejectedValue(abort);
    const err = await capturar(crearLoteConsolidadosOt(CUERPO, new AbortController().signal));
    expect(err).toBe(abort);
  });
});
