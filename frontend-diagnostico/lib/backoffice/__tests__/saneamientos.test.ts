import { afterEach, describe, expect, it, vi } from "vitest";
import { EmpresaNoElegida } from "@/lib/backoffice/api";
import {
  MAXIMO_EVIDENCIA_BYTES,
  MAXIMO_TOMAR,
  TAMANOS_BANDEJA,
  TAMANO_BANDEJA_POR_DEFECTO,
  actualizarRenglon,
  agregarRenglon,
  anularSaneamiento,
  cerrarSaneamiento,
  consultaBandeja,
  editarObservacion,
  filtrosBandejaDesdeUrl,
  hayFiltrosBandeja,
  listarBandeja,
  obtenerDeudaBackoffice,
  obtenerResumenSaneamientos,
  obtenerSaneamientoBackoffice,
  quitarEvidencia,
  quitarRenglon,
  reabrirSaneamiento,
  subirEvidencia,
  tomarSaneamiento,
  tomarSaneamientos,
  urlBandeja,
  urlEvidenciaBackoffice,
  urlExcelBandeja,
  type FiltrosBandeja,
  type SaneamientoBackoffice,
} from "@/lib/backoffice/saneamientos";
import { TAMANOS_DE_PAGINA, TAMANO_DE_PAGINA_POR_DEFECTO } from "@flit/ui/usePaginacion";
import { EMPRESA_A, llamadas, simularApi } from "./datos";

// Uso de ejemplo: grupo A (entre compañías): await listarBandeja(filtrosBandejaDesdeUrl(params)); await tomarSaneamientos(items).
// Grupo B (con ?empresa=): await obtenerSaneamientoBackoffice(sid, empresa); await actualizarRenglon(sid, rid, empresa, cambios).
// Sin compañía válida, el grupo B no sale a la red (EmpresaNoElegida).

afterEach(() => vi.unstubAllGlobals());

const SID = "00000000-0000-7000-8000-00000000e001";
const RID = "00000000-0000-7000-8000-00000000d001";
const BASE = `/backoffice/saneamientos/${SID}`;
const urlDe = (fetchMock: { mock: { calls: unknown[][] } }, n = 0) => new URL(String(fetchMock.mock.calls[n][0]), "http://localhost");
const cuerpo = (fetchMock: { mock: { calls: unknown[][] } }, n = 0) => JSON.parse(String((fetchMock.mock.calls[n][1] as RequestInit).body));
const porDefecto = (): FiltrosBandeja => filtrosBandejaDesdeUrl(new URLSearchParams());

const detalle: SaneamientoBackoffice = {
  id: SID,
  version: 9,
  estado: "EN_GESTION",
  solicitado_en: "2026-11-14T15:00:00Z",
  tomado_en: "2026-11-14T16:00:00Z",
  cerrado_en: null,
  motivo_cierre: null,
  motivo_anulacion: null,
  observacion_flit: null,
  renglones: [],
  historial: [{ evento: "TOMADO", ocurrido_en: "2026-11-14T16:00:00Z", actor: "FLIT", fuente: null, motivo: null, usuario_id: "u-flit" }],
};

describe("bandeja: filtros en la URL (grupo A, §6.2)", () => {
  it("UA-2: por defecto: solo SOLICITADO, sin compañía, página 1 y el tamaño por defecto del kit (10)", () => {
    expect(porDefecto()).toEqual({ estados: ["SOLICITADO"], empresa: "", placa: "", desde: "", hasta: "", pagina: 1, tamano: 10 });
    expect(TAMANO_BANDEJA_POR_DEFECTO).toBe(TAMANO_DE_PAGINA_POR_DEFECTO);
    expect(TAMANOS_BANDEJA).toEqual([...TAMANOS_DE_PAGINA]);
    expect(urlBandeja(porDefecto())).toBe("/backoffice/saneamientos");
    expect(hayFiltrosBandeja(porDefecto())).toBe(false);
  });

  it("multiselección de estado repetible, incluido ANULADO (D-06); lo inválido se descarta", () => {
    const f = filtrosBandejaDesdeUrl(new URLSearchParams("estado=EN_GESTION&estado=ANULADO&estado=OTRO&empresa=no-es-uuid&tamano=7"));
    expect(f.estados).toEqual(["EN_GESTION", "ANULADO"]);
    expect(f.empresa).toBe("");
    expect(f.tamano).toBe(10);
  });

  it("UA-2: acepta los tamaños del kit (10/20/30/50) y descarta los de antes (25, 100, 200)", () => {
    for (const n of [10, 20, 30, 50]) expect(filtrosBandejaDesdeUrl(new URLSearchParams(`tamano=${n}`)).tamano).toBe(n);
    for (const n of [25, 100, 200]) expect(filtrosBandejaDesdeUrl(new URLSearchParams(`tamano=${n}`)).tamano).toBe(10);
    expect(urlBandeja({ ...porDefecto(), tamano: 20 })).toBe("/backoffice/saneamientos?tamano=20");
  });

  it("todos apagados (estado= vacío) equivale a todos: la consulta no manda estado", () => {
    const f = filtrosBandejaDesdeUrl(new URLSearchParams("estado="));
    expect(f.estados).toEqual([]);
    expect(urlBandeja(f)).toBe("/backoffice/saneamientos?estado=");
    expect(new URLSearchParams(consultaBandeja(f)).has("estado")).toBe(false);
    expect(hayFiltrosBandeja(f)).toBe(true);
  });

  it("ida y vuelta de la URL con todos los filtros", () => {
    const f: FiltrosBandeja = { estados: ["SOLICITADO", "SANEADO"], empresa: EMPRESA_A, placa: "ABC", desde: "2026-11-01", hasta: "2026-11-14", pagina: 2, tamano: 50 };
    const url = urlBandeja(f);
    expect(filtrosBandejaDesdeUrl(new URLSearchParams(url.split("?")[1]))).toEqual(f);
  });

  it("listarBandeja: GET con estados repetidos, placa en mayúscula, página y tamaño", async () => {
    const fetchMock = simularApi({ "GET /backoffice/saneamientos": () => Response.json({ pagina: 1, tamano: 25, total: 0, saneamientos: [] }) });
    await listarBandeja({ ...porDefecto(), estados: ["SOLICITADO", "EN_GESTION"], placa: " abc ", empresa: EMPRESA_A });
    const url = urlDe(fetchMock);
    expect(url.pathname).toBe("/api/v1/diagnostico/backoffice/saneamientos");
    expect(url.searchParams.getAll("estado")).toEqual(["SOLICITADO", "EN_GESTION"]);
    expect(url.searchParams.get("placa")).toBe("ABC");
    expect(url.searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(url.searchParams.get("pagina")).toBe("1");
    expect(url.searchParams.get("tamano")).toBe("10");
  });

  it("listarBandeja con una respuesta sin lista lanza un error legible", async () => {
    simularApi({ "GET /backoffice/saneamientos": () => Response.json({ total: 0 }) });
    await expect(listarBandeja(porDefecto())).rejects.toMatchObject({ message: expect.stringContaining("respondió algo inesperado") });
  });

  it("Excel de la bandeja: URL del BFF con los mismos filtros, sin página", () => {
    const url = new URL(urlExcelBandeja({ ...porDefecto(), placa: "abc", pagina: 3 }), "http://localhost");
    expect(url.pathname).toBe("/api/v1/diagnostico/backoffice/saneamientos/excel");
    expect(url.searchParams.getAll("estado")).toEqual(["SOLICITADO"]);
    expect(url.searchParams.get("placa")).toBe("ABC");
    expect(url.searchParams.has("pagina")).toBe(false);
  });

  it("resumen para la insignia: GET …/resumen", async () => {
    simularApi({ "GET /backoffice/saneamientos/resumen": () => Response.json({ solicitados: 7 }) });
    await expect(obtenerResumenSaneamientos()).resolves.toEqual({ solicitados: 7 });
  });

  it("tomar varios: items {empresa, id}, 1 a 200; fuera de rango no sale a la red", async () => {
    const fetchMock = simularApi({ "POST /backoffice/saneamientos/tomar": () => Response.json({ tomados: 1, omitidos: [{ id: "s2", motivo: "TRANSICION_INVALIDA" }] }) });
    const r = await tomarSaneamientos([{ empresa: EMPRESA_A, id: "s1" }, { empresa: EMPRESA_A, id: "s2" }]);
    expect(cuerpo(fetchMock)).toEqual({ items: [{ empresa: EMPRESA_A, id: "s1" }, { empresa: EMPRESA_A, id: "s2" }] });
    expect(r.omitidos[0].motivo).toBe("TRANSICION_INVALIDA");
    expect(MAXIMO_TOMAR).toBe(200);
    await expect(tomarSaneamientos([])).rejects.toMatchObject({ status: 400, code: "LISTA_INVALIDA" });
    await expect(tomarSaneamientos(Array.from({ length: 201 }, (_, i) => ({ empresa: EMPRESA_A, id: `s${i}` })))).rejects.toMatchObject({ code: "LISTA_INVALIDA" });
    expect(fetchMock).toHaveBeenCalledTimes(1);
  });
});

describe("gestión de un saneamiento (grupo B, ?empresa=)", () => {
  it("sin compañía válida ninguna llamada sale a la red", async () => {
    const fetchMock = simularApi({});
    await expect(obtenerSaneamientoBackoffice(SID, "")).rejects.toBeInstanceOf(EmpresaNoElegida);
    await expect(obtenerDeudaBackoffice(SID, "x")).rejects.toBeInstanceOf(EmpresaNoElegida);
    await expect(tomarSaneamiento(SID, " ")).rejects.toBeInstanceOf(EmpresaNoElegida);
    expect(() => urlEvidenciaBackoffice(SID, RID, "")).toThrow(EmpresaNoElegida);
    expect(fetchMock).not.toHaveBeenCalled();
  });

  it("detalle con historial completo y usuario_id (SP-15: tipo propio de FLIT)", async () => {
    const fetchMock = simularApi({ [`GET ${BASE}`]: () => Response.json(detalle) });
    const s = await obtenerSaneamientoBackoffice(SID, EMPRESA_A);
    expect(urlDe(fetchMock).searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(s.historial[0].usuario_id).toBe("u-flit");
  });

  it("SP-7: la deuda de la gestión sale de su propia ruta del backoffice", async () => {
    const fetchMock = simularApi({ [`GET ${BASE}/deuda`]: () => Response.json({ vehiculo: { id: "v", placa: "ABC123" }, modo: "SIMPLE", dictamen: null, fuentes: [], saneamiento: null }) });
    const panel = await obtenerDeudaBackoffice(SID, EMPRESA_A);
    expect(urlDe(fetchMock).pathname).toBe(`/api/v1/diagnostico${BASE}/deuda`);
    expect(urlDe(fetchMock).searchParams.get("empresa")).toBe(EMPRESA_A);
    expect(panel.dictamen).toBeNull();
  });

  it("tomar, observación, cerrar, reabrir y anular llevan su cuerpo con version", async () => {
    const ok = () => Response.json(detalle);
    const fetchMock = simularApi({
      [`POST ${BASE}/tomar`]: ok,
      [`PUT ${BASE}/observacion`]: ok,
      [`POST ${BASE}/cerrar`]: ok,
      [`POST ${BASE}/reabrir`]: ok,
      [`POST ${BASE}/anular`]: ok,
    });
    await tomarSaneamiento(SID, EMPRESA_A);
    await editarObservacion(SID, EMPRESA_A, "  ", 9);
    await cerrarSaneamiento(SID, EMPRESA_A, { resultado: "SANEADO", version: 9 });
    await cerrarSaneamiento(SID, EMPRESA_A, { resultado: "NO_PROCEDE", motivo: " No hay soporte del pago ", version: 9 });
    await reabrirSaneamiento(SID, EMPRESA_A, "Llegó un soporte nuevo", 10);
    await anularSaneamiento(SID, EMPRESA_A, "Solicitud duplicada por error", 11);
    expect((fetchMock.mock.calls[0][1] as RequestInit).body).toBeUndefined();
    expect(cuerpo(fetchMock, 1)).toEqual({ observacion: null, version: 9 });
    expect(cuerpo(fetchMock, 2)).toEqual({ resultado: "SANEADO", version: 9 });
    expect(cuerpo(fetchMock, 3)).toEqual({ resultado: "NO_PROCEDE", motivo: "No hay soporte del pago", version: 9 });
    expect(cuerpo(fetchMock, 4)).toEqual({ motivo: "Llegó un soporte nuevo", version: 10 });
    expect(cuerpo(fetchMock, 5)).toEqual({ motivo: "Solicitud duplicada por error", version: 11 });
    for (let n = 0; n < 6; n++) expect(urlDe(fetchMock, n).searchParams.get("empresa")).toBe(EMPRESA_A);
  });

  it("renglones: agregar OTRO, actualizar con nota recortada o null, quitar", async () => {
    const renglon = { id: RID, version: 46, fuente: "OTRO", hallazgo: null, descripcion: "Embargo", automatico: false, resultado: "PENDIENTE", nota: null, evidencia: null };
    const fetchMock = simularApi({
      // Contrato (D-65): el POST devuelve el saneamiento completo.
      [`POST ${BASE}/renglones`]: () => Response.json({ id: SID, version: 12, renglones: [renglon] }),
      [`PUT ${BASE}/renglones/${RID}`]: () => Response.json(renglon),
      [`DELETE ${BASE}/renglones/${RID}`]: () => new Response(null, { status: 204 }),
    });
    const tras = await agregarRenglon(SID, EMPRESA_A, "  Embargo ");
    expect(tras.renglones.map((r) => r.id)).toEqual([RID]);
    await actualizarRenglon(SID, RID, EMPRESA_A, { resultado: "SANEADO", nota: "  pagado  ", version: 45 });
    await actualizarRenglon(SID, RID, EMPRESA_A, { resultado: "PENDIENTE", nota: "   ", version: 46 });
    await quitarRenglon(SID, RID, EMPRESA_A);
    expect(cuerpo(fetchMock, 0)).toEqual({ descripcion: "Embargo" });
    expect(cuerpo(fetchMock, 1)).toEqual({ resultado: "SANEADO", nota: "pagado", version: 45 });
    expect(cuerpo(fetchMock, 2)).toEqual({ resultado: "PENDIENTE", nota: null, version: 46 });
    expect(llamadas(fetchMock, "DELETE", `${BASE}/renglones/${RID}`)).toHaveLength(1);
  });

  it("409 VERSION_DESACTUALIZADA se propaga con su code", async () => {
    simularApi({ [`PUT ${BASE}/renglones/${RID}`]: () => Response.json({ code: "VERSION_DESACTUALIZADA" }, { status: 409 }) });
    await expect(actualizarRenglon(SID, RID, EMPRESA_A, { resultado: "SANEADO", nota: null, version: 1 })).rejects.toMatchObject({
      status: 409,
      code: "VERSION_DESACTUALIZADA",
    });
  });

  it("evidencia: multipart con el campo archivo; vacío o de más de 10 MB no sale a la red", async () => {
    const fetchMock = simularApi({
      [`PUT ${BASE}/renglones/${RID}/evidencia`]: () => Response.json({ id: RID }),
      [`DELETE ${BASE}/renglones/${RID}/evidencia`]: () => new Response(null, { status: 204 }),
    });
    const pdf = new File(["%PDF-1.4"], "pago.pdf", { type: "application/pdf" });
    await subirEvidencia(SID, RID, EMPRESA_A, pdf);
    const body = (fetchMock.mock.calls[0][1] as RequestInit).body as FormData;
    expect(body).toBeInstanceOf(FormData);
    expect((body.get("archivo") as File).name).toBe("pago.pdf");
    expect(MAXIMO_EVIDENCIA_BYTES).toBe(10 * 1024 * 1024);
    await expect(subirEvidencia(SID, RID, EMPRESA_A, new File([], "vacio.pdf"))).rejects.toMatchObject({ status: 400, code: "ARCHIVO_REQUERIDO" });
    const grande = new File(["x"], "grande.pdf", { type: "application/pdf" });
    Object.defineProperty(grande, "size", { value: MAXIMO_EVIDENCIA_BYTES + 1 });
    await expect(subirEvidencia(SID, RID, EMPRESA_A, grande)).rejects.toMatchObject({ status: 413, code: "ARCHIVO_MUY_GRANDE" });
    await quitarEvidencia(SID, RID, EMPRESA_A);
    expect(fetchMock).toHaveBeenCalledTimes(2);
    expect(urlEvidenciaBackoffice(SID, RID, EMPRESA_A)).toBe(`/api/v1/diagnostico${BASE}/renglones/${RID}/evidencia?empresa=${EMPRESA_A}`);
  });
});
