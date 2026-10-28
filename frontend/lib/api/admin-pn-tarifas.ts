// HU #14074 (Feature #14070, Épica #14021) — cliente tipado de las TARIFAS del portal de Persona Natural en
// Plataforma. Contrato real leído de `AdminPlataformaPersonaNaturalEndpoints.cs` (HU #14071), solo SuperAdmin:
//   GET  /api/v1/admin/plataforma/persona-natural/tarifas?soloVigentes&tipoTramiteId&organismoId&concepto&fecha
//   GET  …/tarifas/{id}/historial
//   POST …/tarifas   (lote por concepto con una misma vigencia; 201 con las versiones nuevas)
// Las versiones no se editan ni se borran: cambiar un valor es registrar una versión nueva. NO hay datos bancarios
// ni de pago en este módulo (decisión del PO: viven en otra épica).
import { apiFetch } from "./client";

export const PN_BASE = "/api/v1/admin/plataforma/persona-natural";

/** Conceptos de la tarifa (los mismos del CHECK de `admin.persona_natural_tariffs`). */
export const CONCEPTOS_TARIFA = [
  "DERECHOS_TRANSITO",
  "SERVICIO_FLIT",
  "CAMBIO_COLOR",
  "CONVERSION_COMBUSTIBLE",
  "CAMBIO_CARROCERIA",
  "ENVIO_DOCUMENTO",
] as const;
export type ConceptoTarifa = (typeof CONCEPTOS_TARIFA)[number];

export const CONCEPTO_LABEL: Record<ConceptoTarifa, string> = {
  DERECHOS_TRANSITO: "Derechos de tránsito",
  SERVICIO_FLIT: "Servicio digital",
  CAMBIO_COLOR: "Cambio de color",
  CONVERSION_COMBUSTIBLE: "Conversión de combustible",
  CAMBIO_CARROCERIA: "Cambio de carrocería",
  ENVIO_DOCUMENTO: "Envío del documento",
};

/** Derechos de tránsito y servicio digital siempre se registran para un tipo de trámite; los demás pueden ser generales. */
export const CONCEPTOS_QUE_EXIGEN_TIPO: readonly ConceptoTarifa[] = ["DERECHOS_TRANSITO", "SERVICIO_FLIT"];

/** Tope de cordura del backend (mil millones de pesos). */
export const VALOR_MAXIMO_COP = 1_000_000_000;

export type EstadoTarifa = "vigente" | "futura" | "cerrada";

/** `TarifaPnView`: `null` en tipo u organismo = valor general. Las fechas son de calendario (`AAAA-MM-DD`). */
export interface TarifaPn {
  id: string;
  concepto: ConceptoTarifa;
  tipoTramiteId: string | null;
  tipoTramiteCodigo: string | null;
  tipoTramiteNombre: string | null;
  organismoId: string | null;
  organismoCodigo: string | null;
  organismoNombre: string | null;
  valorCop: number;
  vigenteDesde: string;
  vigenteHasta: string | null;
  estado: EstadoTarifa;
  creadaEn: string;
  creadaPor: string | null;
  actualizadaEn: string | null;
  actualizadaPor: string | null;
}

export interface TarifasFiltro {
  soloVigentes?: boolean;
  tipoTramiteId?: string;
  organismoId?: string;
  concepto?: ConceptoTarifa;
  fecha?: string;
}

export interface NuevasTarifas {
  tipoTramiteId?: string | null;
  organismoId?: string | null;
  /** `AAAA-MM-DD`; no puede ser anterior a hoy (hora de Colombia). */
  vigenteDesde: string;
  valores: { concepto: ConceptoTarifa; valorCop: number }[];
}

export function fetchTarifasPn(filtro: TarifasFiltro = {}, signal?: AbortSignal): Promise<TarifaPn[]> {
  return apiFetch<TarifaPn[]>(`${PN_BASE}/tarifas`, {
    query: { ...filtro },
    signal,
  });
}

export function fetchHistorialTarifaPn(id: string, signal?: AbortSignal): Promise<TarifaPn[]> {
  return apiFetch<TarifaPn[]>(`${PN_BASE}/tarifas/${id}/historial`, { signal });
}

export function registrarTarifasPn(body: NuevasTarifas): Promise<TarifaPn[]> {
  return apiFetch<TarifaPn[]>(`${PN_BASE}/tarifas`, { method: "POST", body });
}

/** Hoy en Colombia (UTC−5, sin horario de verano) como `AAAA-MM-DD`: la vigencia se mide en días. */
export function hoyColombiaIso(now: Date = new Date()): string {
  return new Date(now.getTime() - 5 * 3_600_000).toISOString().slice(0, 10);
}

/** Pesos enteros con separador de miles colombiano escrito a mano: `1250000 → "$ 1.250.000"`. */
export function formatPesos(valor: number | null | undefined): string {
  if (valor === null || valor === undefined || !Number.isFinite(valor)) return "—";
  return `$ ${String(Math.trunc(valor)).replace(/\B(?=(\d{3})+(?!\d))/g, ".")}`;
}

export type ValorPesos = { ok: true; valor: number } | { ok: false; mensaje: string };

/**
 * Interpreta lo que escribió la persona: pesos enteros, con o sin puntos de miles y `$`. Rechaza negativos,
 * decimales, letras y valores por encima del tope, con un mensaje claro para mostrar junto al campo.
 */
export function parsePesos(texto: string): ValorPesos {
  const limpio = texto.trim().replace(/^\$\s*/, "").replace(/\s/g, "");
  if (limpio === "") return { ok: false, mensaje: "Escriba el valor en pesos." };
  if (limpio.startsWith("-")) return { ok: false, mensaje: "El valor no puede ser negativo." };
  if (!/^\d{1,3}(\.\d{3})+$|^\d+$/.test(limpio)) {
    return { ok: false, mensaje: "Escriba solo números enteros en pesos, sin letras ni decimales." };
  }
  const valor = Number(limpio.replace(/\./g, ""));
  if (valor > VALOR_MAXIMO_COP) {
    return { ok: false, mensaje: `El valor no puede superar ${formatPesos(VALOR_MAXIMO_COP)}.` };
  }
  return { ok: true, valor };
}

/** Una fila de la tabla agrupa los conceptos de un mismo ámbito (tipo + organismo) y una misma fecha de inicio. */
export interface GrupoTarifa {
  clave: string;
  tipoTramiteId: string | null;
  tipoTramiteNombre: string | null;
  organismoId: string | null;
  organismoNombre: string | null;
  vigenteDesde: string;
  estado: EstadoTarifa;
  porConcepto: Partial<Record<ConceptoTarifa, TarifaPn>>;
}

const PESO_ESTADO: Record<EstadoTarifa, number> = { vigente: 0, futura: 1, cerrada: 2 };

export function agruparTarifas(tarifas: readonly TarifaPn[]): GrupoTarifa[] {
  const grupos = new Map<string, GrupoTarifa>();
  for (const t of tarifas) {
    const clave = `${t.tipoTramiteId ?? "*"}|${t.organismoId ?? "*"}|${t.vigenteDesde}`;
    let g = grupos.get(clave);
    if (!g) {
      g = {
        clave,
        tipoTramiteId: t.tipoTramiteId,
        tipoTramiteNombre: t.tipoTramiteNombre,
        organismoId: t.organismoId,
        organismoNombre: t.organismoNombre,
        vigenteDesde: t.vigenteDesde,
        estado: t.estado,
        porConcepto: {},
      };
      grupos.set(clave, g);
    }
    g.porConcepto[t.concepto] = t;
    if (PESO_ESTADO[t.estado] < PESO_ESTADO[g.estado]) g.estado = t.estado;
  }
  return [...grupos.values()].sort(
    (a, b) =>
      (a.tipoTramiteNombre ?? "").localeCompare(b.tipoTramiteNombre ?? "", "es") ||
      (a.organismoNombre ?? "").localeCompare(b.organismoNombre ?? "", "es") ||
      b.vigenteDesde.localeCompare(a.vigenteDesde),
  );
}
