import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import { simularApi } from "@/lib/tablero/__tests__/datos";
import {
  TAMANOS_VEHICULOS,
  TAMANO_VEHICULOS_POR_DEFECTO,
  consultaVehiculos,
  filtrosVehiculosDesdeUrl,
  hayFiltrosVehiculos,
  listarVehiculos,
  traducirErrorVehiculos,
  urlVehiculos,
  type FilaVehiculoListado,
  type FiltrosVehiculos,
} from "@/lib/vehiculos/listado";

// Uso de ejemplo: const f = filtrosVehiculosDesdeUrl(searchParams); await listarVehiculos(f) → GET /vehiculos?…;
// router.replace(urlVehiculos({ ...f, pagina: 1 })) → "/?vista=vehiculos&…" (pestaña «Vehículos» del inicio, PS-3).

afterEach(() => vi.unstubAllGlobals());

const vacios = (): FiltrosVehiculos => filtrosVehiculosDesdeUrl(new URLSearchParams());

const fila: FilaVehiculoListado = {
  vehiculo_id: "v1",
  placa: "ABC123",
  diagnostico_id: "d1",
  diagnostico_nombre: "Lote de prueba",
  diagnostico_creado_en: "2026-11-14T15:00:00Z",
  modo: "CON_ANALISIS",
  estado: "TERMINADO",
  veredicto: "BLOQUEADO",
  saneamiento: { estado: null, elegible: true, motivo_no_elegible: null },
  tramite: null,
};

describe("filtrosVehiculosDesdeUrl", () => {
  it("sin parámetros: todo vacío, página 1 y tamaño 25 (opciones 25/50/100)", () => {
    expect(TAMANO_VEHICULOS_POR_DEFECTO).toBe(25);
    expect(TAMANOS_VEHICULOS).toEqual([25, 50, 100]);
    expect(vacios()).toEqual({
      placa: "",
      estado: null,
      veredicto: null,
      saneamiento: null,
      saneamientoEstado: null,
      modo: null,
      conTramite: null,
      desde: "",
      hasta: "",
      pagina: 1,
      tamano: 25,
    });
  });

  it("lee los filtros válidos del contrato (estado C1, modo C2)", () => {
    const f = filtrosVehiculosDesdeUrl(
      new URLSearchParams(
        "placa=abc&estado=TERMINADO&veredicto=SIN_DICTAMEN&saneamiento=SIN&modo=CON_ANALISIS&con_tramite=false&desde=2026-11-01&hasta=2026-11-14&pagina=3&tamano=50",
      ),
    );
    expect(f).toEqual({
      placa: "abc",
      estado: "TERMINADO",
      veredicto: "SIN_DICTAMEN",
      saneamiento: "SIN",
      saneamientoEstado: null,
      modo: "CON_ANALISIS",
      conTramite: false,
      desde: "2026-11-01",
      hasta: "2026-11-14",
      pagina: 3,
      tamano: 50,
    });
  });

  it("descarta lo que la API no admite (PENDIENTE, CON_ANALISIS_IA, ANULADO, fechas y tamaños inválidos)", () => {
    const f = filtrosVehiculosDesdeUrl(
      new URLSearchParams("estado=PENDIENTE&modo=CON_ANALISIS_IA&saneamiento_estado=ANULADO&saneamiento=talvez&con_tramite=si&desde=14/11/2026&pagina=0&tamano=10&veredicto=viable"),
    );
    expect(f).toEqual(vacios());
  });

  it("saneamiento_estado implica CON aunque la URL diga SIN", () => {
    const f = filtrosVehiculosDesdeUrl(new URLSearchParams("saneamiento=SIN&saneamiento_estado=EN_GESTION"));
    expect(f.saneamiento).toBe("CON");
    expect(f.saneamientoEstado).toBe("EN_GESTION");
  });
});

describe("consultaVehiculos y urlVehiculos", () => {
  it("la consulta omite vacíos, normaliza la placa y siempre manda página y tamaño", () => {
    const q = new URLSearchParams(consultaVehiculos({ ...vacios(), placa: "  abc1 ", conTramite: true, saneamientoEstado: "SOLICITADO", saneamiento: "CON" }));
    expect(Object.fromEntries(q)).toEqual({ placa: "ABC1", con_tramite: "true", saneamiento: "CON", saneamiento_estado: "SOLICITADO", pagina: "1", tamano: "25" });
  });

  it("la URL va a la pestaña Vehículos y omite la página 1 y el tamaño por defecto", () => {
    expect(urlVehiculos(vacios())).toBe("/?vista=vehiculos");
    expect(urlVehiculos({ ...vacios(), estado: "EN_PROCESO", pagina: 2, tamano: 100 })).toBe("/?vista=vehiculos&estado=EN_PROCESO&pagina=2&tamano=100");
  });

  it("ida y vuelta: la URL se vuelve a leer igual", () => {
    const f: FiltrosVehiculos = { ...vacios(), placa: "XYZ", veredicto: "VIABLE", saneamiento: "CON", saneamientoEstado: "SANEADO", modo: "SIMPLE", conTramite: false, desde: "2026-01-01", pagina: 4 };
    const url = urlVehiculos(f);
    expect(filtrosVehiculosDesdeUrl(new URLSearchParams(url.slice(2)))).toEqual(f);
  });

  it("hayFiltrosVehiculos ignora página y tamaño", () => {
    expect(hayFiltrosVehiculos({ ...vacios(), pagina: 5, tamano: 100 })).toBe(false);
    expect(hayFiltrosVehiculos({ ...vacios(), placa: "  " })).toBe(false);
    expect(hayFiltrosVehiculos({ ...vacios(), conTramite: false })).toBe(true);
    expect(hayFiltrosVehiculos({ ...vacios(), saneamiento: "SIN" })).toBe(true);
  });
});

describe("listarVehiculos (GET /vehiculos, §6.6)", () => {
  it("pide con la consulta y devuelve la lista con total", async () => {
    const fetchMock = simularApi({ "GET /vehiculos": () => Response.json({ pagina: 1, tamano: 25, total: 1, vehiculos: [fila] }) });
    const lista = await listarVehiculos({ ...vacios(), estado: "TERMINADO" });
    const url = new URL(String(fetchMock.mock.calls[0][0]), "http://localhost");
    expect(url.pathname).toBe("/api/v1/diagnostico/vehiculos");
    expect(url.searchParams.get("estado")).toBe("TERMINADO");
    expect(lista.total).toBe(1);
    expect(lista.vehiculos[0].saneamiento.elegible).toBe(true);
  });

  it("una respuesta sin la lista lanza un error legible", async () => {
    simularApi({ "GET /vehiculos": () => Response.json({ total: 0 }) });
    await expect(listarVehiculos(vacios())).rejects.toMatchObject({ message: expect.stringContaining("respondió algo inesperado") });
  });
});

describe("traducirErrorVehiculos", () => {
  it("403 → acceso; 400 con campo de filtro → ese control; PAGINACION_INVALIDA → reiniciar", () => {
    expect(traducirErrorVehiculos(new ApiError(403, "x", "SIN_PERMISO"))).toEqual({ tipo: "ACCESO", code: "SIN_PERMISO" });
    expect(traducirErrorVehiculos(new ApiError(400, "x", "FILTRO_INVALIDO", "placa"))).toEqual({ tipo: "CAMPO", campo: "placa", mensaje: "Revisa la placa." });
    expect(traducirErrorVehiculos(new ApiError(400, "x", "RANGO_INVALIDO", "hasta"))).toEqual({
      tipo: "CAMPO",
      campo: "hasta",
      mensaje: "La fecha final no puede ser anterior a la inicial.",
    });
    expect(traducirErrorVehiculos(new ApiError(400, "x", "PAGINACION_INVALIDA", "tamano"))).toMatchObject({ tipo: "AVISO", reiniciarPagina: true });
  });

  it("el resto → aviso con su mensaje", () => {
    expect(traducirErrorVehiculos(new ApiError(500, "Diagnóstico no respondió."))).toEqual({ tipo: "AVISO", mensaje: "Diagnóstico no respondió.", reiniciarPagina: false });
    expect(traducirErrorVehiculos("raro")).toEqual({ tipo: "AVISO", mensaje: "No pudimos cargar los vehículos.", reiniciarPagina: false });
  });
});
