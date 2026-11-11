// Crear trámite desde el diagnóstico (Punto 4). Diseño en docs/diagnosticos/diseno-crear-tramite-desde-diagnostico.md §5.1 y
// §6; decisión en ADR-0084. Todo lo que depende de nombres de ruta o de campos vive aquí, para ajustarlo en un solo sitio.
//
// Incremento 2 (individual), contrato real (contracts/openapi/diagnostico.v1.yaml): la fila de la grilla trae
// `derivacion: { elegible, motivo_no_elegible, estado, tramite: { id, numero, url } | null } | null` (null = derivación
// apagada) y POST …/vehiculos/{vid}/tramite responde 201 `{ tramite_id, numero, url, ya_existia }`. Se conserva, porque es
// barato, la forma plana anterior (`tramite`, `elegible_tramite`, `motivo_no_elegible` en la fila; `{ tramite: {…} }`).
//
// Incremento 3 (masivo), CONTRATO PROVISIONAL con los nombres del diseño §5.1 (el backend se hace en paralelo):
//   POST …/diagnosticos/{id}/tramites { vehiculo_ids, tipo_tramite_codigo? } → 202 { operacion_id, total, encolados, no_elegibles }
//   GET  …/diagnosticos/{id}/tramites/operaciones?ultima=true → 200 [ProgresoOperacion]
//   GET  …/diagnosticos/{id}/tramites/operaciones/{opId} → 200 { estado, total, creadas, omitidas, fallidas, pendientes, items }
//   POST …/diagnosticos/{id}/tramites/operaciones/{opId}/reintentar-fallidas → 202 { reencolados } · 409 con ítems en vuelo
import { ApiError, apiFetch } from "@/lib/api";
import { MOTIVOS_DERIVACION, pareceCodigo, rotular } from "@/lib/rotulos";
import type { FilaVehiculo, Grilla, TramiteFila } from "@/lib/tablero/api";

const lote = (diagnosticoId: string) => `/diagnosticos/${encodeURIComponent(diagnosticoId)}`;

/** POST /diagnosticos/{id}/vehiculos/{vid}/tramite — individual y síncrono. */
export const rutaCrearTramite = (diagnosticoId: string, vehiculoId: string) =>
  `${lote(diagnosticoId)}/vehiculos/${encodeURIComponent(vehiculoId)}/tramite`;

/** Código de la API cuando el lote es Simple y el cuerpo no trae el tipo (se abre el modal del tipo). */
export const CODIGOS_TIPO_REQUERIDO: ReadonlySet<string> = new Set(["TIPO_TRAMITE_REQUERIDO", "TIPO_TRAMITE_OBLIGATORIO"]);

/** El error pide el tipo de trámite (lote Simple sin tipo en el cuerpo). */
export const pideTipo = (e: unknown) =>
  e instanceof ApiError && (e.status === 400 || e.status === 422) && !!e.code && CODIGOS_TIPO_REQUERIDO.has(e.code);

export interface ResultadoCrearTramite {
  tramite: TramiteFila;
  ya_existia: boolean;
}

type RespuestaCrear = {
  tramite?: { id?: string; numero?: string | number; url?: string } | null;
  tramite_id?: string;
  numero?: string | number;
  url?: string;
  ya_existia?: boolean;
} | null;

/** Crea (o recupera, si ya existía por la idempotencia de Trámites) el borrador del vehículo. */
export async function crearTramite(diagnosticoId: string, vehiculoId: string, tipoTramiteCodigo?: string): Promise<ResultadoCrearTramite> {
  const respuesta = await apiFetch<RespuestaCrear>(rutaCrearTramite(diagnosticoId, vehiculoId), {
    method: "POST",
    body: tipoTramiteCodigo ? { tipo_tramite_codigo: tipoTramiteCodigo } : undefined,
  });
  const id = respuesta?.tramite?.id ?? respuesta?.tramite_id;
  const numero = respuesta?.tramite?.numero ?? respuesta?.numero;
  const url = respuesta?.tramite?.url ?? respuesta?.url;
  if (!id) throw new Error("Trámites no devolvió el borrador creado.");
  return { tramite: { id, numero: numero ?? "", ...(url ? { url } : {}) }, ya_existia: respuesta?.ya_existia === true };
}

/** Estado del último intento de crear el trámite del vehículo. */
export type EstadoDerivacion = "PENDIENTE" | "ENVIADO" | "CREADO" | "OMITIDO" | "FALLIDO";

/** Fila `derivacion` del contrato (incremento 2). */
export interface DerivacionContrato {
  elegible: boolean;
  motivo_no_elegible: string | null;
  estado: EstadoDerivacion | null;
  tramite: TramiteFila | null;
}

export interface DerivacionFila {
  /** Hay datos de derivación en la fila (backend con Punto 4 y derivación encendida). Sin ellos no se pinta nada. */
  conocida: boolean;
  elegible: boolean;
  motivo: string | null;
  estado: EstadoDerivacion | null;
  tramite: TramiteFila | null;
}

type FilaConDerivacion = FilaVehiculo & { derivacion?: Partial<DerivacionContrato> | null };

/** Lee la derivación de la fila: la forma anidada del contrato manda; si la fila no la trae, la forma plana anterior. */
export function derivacionDeFila(fila: FilaVehiculo): DerivacionFila {
  const f = fila as FilaConDerivacion;
  if (f.derivacion !== undefined) {
    const d = f.derivacion;
    if (!d) return { conocida: false, elegible: false, motivo: null, estado: null, tramite: null };
    const tramite = d.tramite ?? null;
    return { conocida: true, elegible: tramite == null && d.elegible === true, motivo: d.motivo_no_elegible ?? null, estado: d.estado ?? null, tramite };
  }
  const tramite = fila.tramite ?? null;
  const elegible = fila.elegible_tramite;
  return {
    conocida: tramite != null || elegible !== undefined,
    elegible: tramite == null && elegible === true,
    motivo: fila.motivo_no_elegible ?? null,
    estado: null,
    tramite,
  };
}

/** Cambia la derivación de una fila en la forma que la fila ya usa (anidada o plana). */
function conDerivacion(fila: FilaVehiculo, cambio: Partial<DerivacionContrato>): FilaVehiculo {
  const f = fila as FilaConDerivacion;
  if (f.derivacion) return { ...f, derivacion: { ...f.derivacion, ...cambio } } as FilaVehiculo;
  return {
    ...fila,
    ...(cambio.tramite !== undefined ? { tramite: cambio.tramite } : {}),
    ...(cambio.elegible !== undefined ? { elegible_tramite: cambio.elegible } : {}),
    ...(cambio.motivo_no_elegible !== undefined ? { motivo_no_elegible: cambio.motivo_no_elegible } : {}),
  };
}

/** Marca la fila con el trámite creado, sin esperar al sondeo (que solo reenvía filas con celdas cambiadas). */
export function marcarConTramite(grilla: Grilla, vehiculoId: string, tramite: TramiteFila): Grilla {
  return {
    ...grilla,
    vehiculos: grilla.vehiculos.map((v) =>
      v.id === vehiculoId ? conDerivacion(v, { tramite, elegible: false, motivo_no_elegible: "YA_TIENE_TRAMITE", estado: "CREADO" }) : v,
    ),
  };
}

/** Tras encolar el masivo, las placas encoladas quedan en curso (no seleccionables) hasta que la grilla se vuelva a leer. */
export function marcarEncoladas(grilla: Grilla, vehiculoIds: readonly string[]): Grilla {
  const ids = new Set(vehiculoIds);
  return {
    ...grilla,
    vehiculos: grilla.vehiculos.map((v) =>
      ids.has(v.id) ? conDerivacion(v, { elegible: false, motivo_no_elegible: "EN_CURSO", estado: "PENDIENTE" }) : v,
    ),
  };
}

/**
 * Enlace al borrador en Trámites (`frontend/app/tramites/[instanceId]`). Si el backend manda una `url` absoluta, esa; si es
 * relativa, sobre `tramitesUrl`; si no hay, `${tramitesUrl}/tramites/{id}?t={empresa}`. `tramitesUrl` es el origen del shell.
 */
export function urlTramite(tramitesUrl: string, tramite: TramiteFila, empresaId?: string | null): string {
  if (tramite.url && /^https?:\/\//i.test(tramite.url)) return tramite.url;
  const base = tramitesUrl.replace(/\/+$/, "");
  if (tramite.url && tramite.url.startsWith("/")) return `${base}${tramite.url}`;
  const consulta = empresaId ? `?t=${encodeURIComponent(empresaId)}` : "";
  return `${base}/tramites/${encodeURIComponent(tramite.id)}${consulta}`;
}

/** «#57» (el número tal cual; sin número, sin almohadilla). */
export const numeroVisible = (tramite: TramiteFila) => (tramite.numero === "" || tramite.numero == null ? "" : `#${tramite.numero}`);

/**
 * Rótulo de negocio de un motivo (Punto 5, helper lib/rotulos): la etiqueta de la API manda; si falta, el diccionario
 * `MOTIVOS_DERIVACION`; un código desconocido sale como «Novedad sin clasificar». Un texto libre (no código) se muestra tal cual.
 */
export function rotuloMotivo(motivo: string | null | undefined, etiqueta?: string | null): string {
  if (!motivo && !etiqueta) return "No cumple las condiciones para crear el trámite";
  if (motivo && !etiqueta && !pareceCodigo(motivo) && !Object.prototype.hasOwnProperty.call(MOTIVOS_DERIVACION, motivo)) return motivo;
  return rotular({ contexto: "motivo de crear trámite", codigo: motivo ?? "", etiqueta, respaldo: MOTIVOS_DERIVACION });
}

/** Mensaje claro de un fallo de POST …/tramite. El 409 lo trata quien llama (aviso «ya tenía trámite» y refresco). */
export function mensajeCrearTramite(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) return "No tienes permiso para crear trámites.";
    if (error.status === 409) return "Esta placa ya tenía trámite: no se creó otro.";
    if ((error.status === 422 || error.status === 400) && error.code && MOTIVOS_DERIVACION[error.code] && error.message === "No se pudo completar la acción.")
      return MOTIVOS_DERIVACION[error.code];
    if (error.status === 503 && error.retryAfter) return `Trámites no está disponible. Intenta de nuevo en ${error.retryAfter} segundos.`;
  }
  return error instanceof Error && error.message ? error.message : "No se pudo crear el trámite.";
}

// ---------------------------------------------------------------------------------------------------------------------------
// Masivo (incremento 3)

/** POST /diagnosticos/{id}/tramites — encola la operación masiva. */
export const rutaMasivo = (diagnosticoId: string) => `${lote(diagnosticoId)}/tramites`;
/** GET /diagnosticos/{id}/tramites/operaciones[/{opId}] — progreso (solo lee tablas de Diagnóstico). */
export const rutaOperaciones = (diagnosticoId: string) => `${rutaMasivo(diagnosticoId)}/operaciones`;
export const rutaOperacion = (diagnosticoId: string, operacionId: string) => `${rutaOperaciones(diagnosticoId)}/${encodeURIComponent(operacionId)}`;

export interface NoEncolada {
  vehiculoId: string;
  motivoCodigo: string | null;
}

export interface OperacionEncolada {
  operacionId: string;
  total: number;
  encolados: number;
  noElegibles: NoEncolada[];
}

type RespuestaEncolar = {
  operacion_id?: string;
  id?: string;
  total?: number;
  encolados?: number;
  no_elegibles?: { vehiculo_id: string; motivo_codigo?: string | null }[];
} | null;

/** Encola la creación de los borradores de las placas seleccionadas (en lote Simple, con el tipo del modal). */
export async function encolarTramites(diagnosticoId: string, vehiculoIds: readonly string[], tipoTramiteCodigo?: string): Promise<OperacionEncolada> {
  const r = await apiFetch<RespuestaEncolar>(rutaMasivo(diagnosticoId), {
    method: "POST",
    body: { vehiculo_ids: [...vehiculoIds], ...(tipoTramiteCodigo ? { tipo_tramite_codigo: tipoTramiteCodigo } : {}) },
  });
  const operacionId = r?.operacion_id ?? r?.id;
  if (!operacionId) throw new Error("Diagnóstico no devolvió la operación encolada.");
  return {
    operacionId,
    total: r?.total ?? vehiculoIds.length,
    encolados: r?.encolados ?? vehiculoIds.length,
    noElegibles: (r?.no_elegibles ?? []).map((x) => ({ vehiculoId: x.vehiculo_id, motivoCodigo: x.motivo_codigo ?? null })),
  };
}

export interface ItemOperacion {
  vehiculoId: string;
  placa: string | null;
  estado: string;
  motivoCodigo: string | null;
  /** Motivo en lenguaje claro de la API (manda sobre el diccionario). */
  motivo: string | null;
  tramiteNumero: string | null;
}

export interface ProgresoOperacion {
  id: string;
  estado: string;
  total: number;
  creadas: number;
  omitidas: number;
  fallidas: number;
  /** Ítems todavía pendientes o enviados (el «en curso» de la pantalla). */
  enCurso: number;
  terminada: boolean;
  items: ItemOperacion[];
}

type ItemCrudo = {
  vehiculo_id?: string;
  placa?: string | null;
  estado?: string;
  motivo_codigo?: string | null;
  motivo?: string | null;
  tramite_numero?: string | number | null;
};

type ProgresoCrudo = {
  operacion_id?: string;
  id?: string;
  estado?: string;
  total?: number;
  creadas?: number;
  omitidas?: number;
  fallidas?: number;
  pendientes?: number;
  en_curso?: number;
  items?: ItemCrudo[];
};

const cuenta = (v: unknown) => (typeof v === "number" && Number.isFinite(v) && v >= 0 ? v : 0);

/** Normaliza el progreso de la operación (contadores ausentes = 0; TERMINADA o todo procesado = terminada). */
export function leerProgreso(crudo: ProgresoCrudo, operacionId?: string): ProgresoOperacion {
  const total = cuenta(crudo.total);
  const creadas = cuenta(crudo.creadas);
  const omitidas = cuenta(crudo.omitidas);
  const fallidas = cuenta(crudo.fallidas);
  const procesadas = creadas + omitidas + fallidas;
  const enCurso = cuenta(crudo.pendientes ?? crudo.en_curso ?? Math.max(0, total - procesadas));
  const estado = crudo.estado ?? (total > 0 && procesadas >= total ? "TERMINADA" : "EN_CURSO");
  return {
    id: crudo.operacion_id ?? crudo.id ?? operacionId ?? "",
    estado,
    total,
    creadas,
    omitidas,
    fallidas,
    enCurso,
    terminada: estado === "TERMINADA" || (total > 0 && procesadas >= total && enCurso === 0),
    items: (crudo.items ?? []).map((i) => ({
      vehiculoId: i.vehiculo_id ?? "",
      placa: i.placa ?? null,
      estado: i.estado ?? "",
      motivoCodigo: i.motivo_codigo ?? null,
      motivo: i.motivo ?? null,
      tramiteNumero: i.tramite_numero == null ? null : String(i.tramite_numero),
    })),
  };
}

/** Progreso de una operación. */
export async function obtenerProgreso(diagnosticoId: string, operacionId: string, signal?: AbortSignal): Promise<ProgresoOperacion> {
  return leerProgreso(await apiFetch<ProgresoCrudo>(rutaOperacion(diagnosticoId, operacionId), { signal }), operacionId);
}

/** Operación activa del lote, para retomarla al volver a la pantalla; null si no hay ninguna en curso. */
export async function operacionActiva(diagnosticoId: string, signal?: AbortSignal): Promise<ProgresoOperacion | null> {
  const r = await apiFetch<ProgresoCrudo[] | { operaciones?: ProgresoCrudo[] } | ProgresoCrudo | null>(`${rutaOperaciones(diagnosticoId)}?ultima=true`, {
    signal,
  });
  const lista: ProgresoCrudo[] = Array.isArray(r) ? r : r && "operaciones" in r && Array.isArray(r.operaciones) ? r.operaciones : r ? [r as ProgresoCrudo] : [];
  return lista.map((p) => leerProgreso(p)).find((p) => p.id && !p.terminada) ?? null;
}

/** Reencola los ítems FALLIDOS de la operación; devuelve cuántos. */
export async function reintentarFallidas(diagnosticoId: string, operacionId: string): Promise<number> {
  const r = await apiFetch<{ reencolados?: number } | null>(`${rutaOperacion(diagnosticoId, operacionId)}/reintentar-fallidas`, { method: "POST" });
  return cuenta(r?.reencolados);
}

/** Mensaje claro de un fallo al encolar o al reintentar (nunca el código). */
export function mensajeMasivo(error: unknown): string {
  if (error instanceof ApiError) {
    if (error.status === 403) return "No tienes permiso para crear trámites.";
    if (error.status === 409) return "La operación todavía tiene placas en curso. Intenta de nuevo cuando terminen.";
    if (error.status === 503)
      return error.retryAfter ? `Trámites no está disponible. Intenta de nuevo en ${error.retryAfter} segundos.` : "Trámites no está disponible en este momento.";
    if (error.code && MOTIVOS_DERIVACION[error.code] && error.message === "No se pudo completar la acción.") return MOTIVOS_DERIVACION[error.code];
  }
  return error instanceof Error && error.message ? error.message : "No se pudieron crear los trámites.";
}
