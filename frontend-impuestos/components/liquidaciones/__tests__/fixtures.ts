// Datos de prueba de liquidaciones con placeholders (Ley 1581): ABC123, «Propietario Demo», CC 0000000, «Compañía Demo».
// Forma literal de contracts/openapi/impuestos.v1.yaml 1.0.0 (ResumenLiquidacion, Liquidacion, Item, ItemDetalle).
import { vi } from "vitest";

export const EMPRESA = { id: "00000000-0000-7000-8000-0000000000e1", nombre: "Compañía Demo" };
export const USUARIO = { id: "00000000-0000-7000-8000-000000000001", nombre: "Usuaria Demo" };

export const CONTADORES_CERO = {
  exitosas: 0,
  conDeuda: 0,
  pazYSalvo: 0,
  error: 0,
  datosIncompletos: 0,
  departamentoNoHabilitado: 0,
  incierto: 0,
  sinResultado: 0,
  pendientes: 0,
};

export function resumen(parcial: Record<string, unknown> = {}) {
  return {
    id: "0193aaaa-0000-7000-8000-000000000001",
    tipo: "MASIVA",
    estado: "EN_PROCESO",
    totalItems: 10,
    contadores: { ...CONTADORES_CERO, exitosas: 3, pendientes: 7 },
    creadaEn: "2026-11-24T19:05:00Z",
    creadaPor: USUARIO,
    empresa: EMPRESA,
    nombreArchivo: "placas.xlsx",
    placa: null,
    ...parcial,
  };
}

export function lote(parcial: Record<string, unknown> = {}) {
  return {
    ...resumen(),
    terminadaEn: null,
    cerradaPor: null,
    cerradaEn: null,
    configCopia: { maxRegistrosLote: 500, departamentosHabilitados: null, concurrencia: 2 },
    ...parcial,
  };
}

export function item(parcial: Record<string, unknown> = {}) {
  return {
    id: "0193bbbb-0000-7000-8000-000000000001",
    liquidacionId: "0193aaaa-0000-7000-8000-000000000001",
    fila: 2,
    placa: "ABC123",
    estado: "EXITOSO",
    errorCodigo: null,
    errorClase: null,
    errorMensaje: null,
    campoFaltante: null,
    departamento: { codigo: "25", nombre: "Cundinamarca" },
    propietario: { nombre: "Propietario Demo", documentoTipo: "CC", documentoNumero: "0000000" },
    totalAPagar: 150000,
    resultadoStatus: "con_deuda",
    parcial: false,
    posibleDuplicado: false,
    resultadoTardio: false,
    relanzadoDesde: null,
    ...parcial,
  };
}

export function periodo(parcial: Record<string, unknown> = {}) {
  return {
    periodo: "2025",
    avaluo: 30000000,
    impuesto: 0,
    sancion: null,
    intereses: 1200,
    descuentos: null,
    pagosPrevios: 5000,
    totalAPagar: 150000,
    otrosCargos: [],
    vencimiento: "2026-03-01",
    referenciaPago: "REF-0001",
    noLiquidable: false,
    reciboId: null,
    ...parcial,
  };
}

export function detalle(parcial: Record<string, unknown> = {}) {
  return {
    ...item(),
    empresa: EMPRESA,
    creadaPor: USUARIO,
    creadaEn: "2026-11-24T19:05:00Z",
    consultaStatus: "succeeded",
    resultado: {},
    periodos: [periodo()],
    recibos: [],
    ...parcial,
  };
}

/** Respuesta de error de regla del contrato: `{ code, error }` (+ campos como `tope`). */
export function errorRegla(status: number, code: string, error: string, extra: Record<string, unknown> = {}) {
  return Response.json({ code, error, ...extra }, { status });
}

/** fetch simulado: cada llamada se resuelve con la primera regla cuyo patrón casa con «MÉTODO url». */
export function fetchPorRuta(reglas: Array<[RegExp, (url: URL, init?: RequestInit) => Response | Promise<Response>]>) {
  return vi.fn(async (u: RequestInfo | URL, init?: RequestInit) => {
    const url = new URL(String(u), "http://app");
    const clave = `${init?.method ?? "GET"} ${url.pathname}${url.search}`;
    const regla = reglas.find(([patron]) => patron.test(clave));
    if (!regla) throw new Error(`Ruta sin simular: ${clave}`);
    return regla[1](url, init);
  });
}
