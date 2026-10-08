/**
 * HU #13381 (épica #13216, Feature #13306) — tipos del motor de lotes de descarga masiva de
 * consolidados. Espejo del fragmento OpenAPI propuesto en el diseño (§5, ADR-0070); el backend
 * (#13374) es quien lo publica en `contracts/openapi/`. Cualquier cambio de forma se refleja aquí.
 */
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';

/** FA1 solo crea `consolidado`; `consolidado_maestro` lo habilitan FA2/FB. */
export type TipoDocumentoLote = 'consolidado' | 'consolidado_maestro';

export type EstadoLoteConsolidados =
  | 'en_cola'
  | 'en_proceso'
  | 'empaquetando'
  | 'completado'
  | 'completado_con_omitidos'
  | 'fallido'
  | 'cancelado'
  | 'expirado';

/** Códigos estables (`error` / `extensions.error`) que devuelve el motor. */
export type CodigoErrorLote =
  | 'lote_activo'
  | 'confirmacion_requerida'
  | 'seleccion_invalida'
  | 'tipo_no_permitido'
  | 'lote_no_creado'
  | 'lote_no_terminado'
  | 'lote_terminado'
  | 'descarga_expirada'
  | 'auditoria_no_registrada'
  | 'motor_inactivo'
  | CodigoRechazoRedLote;

/**
 * HU #13419 — 403 de la vista de red al crear el lote (HU #13417, ADR-0070 adenda v7 A7.2). Ninguno
 * crea el lote.
 */
export type CodigoRechazoRedLote =
  | 'network_scope_required'
  | 'network_role_required'
  | 'network_child_out_of_scope'
  | 'network_documents_disabled';

/**
 * HU #13419 — alcance que se PIDE al crear el lote: `null` = propio, `'red'` = toda la red de la
 * cabeza, o el uuid de una compañía hija. El servidor lo resuelve desde la BD.
 */
export type AlcanceRedSolicitado = 'red' | (string & {}) | null;

/**
 * HU #13419 AC5 — `GET /api/v1/tramites/network/documentos`: `documentosRed=false` = cabeza CONCESIÓN
 * con los documentos de red apagados (no se ofrece «Descargar ZIP» en la vista de red).
 */
export interface DocumentosRedRespuesta {
  documentosRed: boolean;
}

export interface ParteLoteConsolidados {
  numero: number;
  nombreArchivo: string;
  pdfs: number;
  omitidos: number;
  bytes: number;
}

/** `LoteConsolidados` del contrato: respuesta de crear (202), de `/actual` y de `/{loteId}`. */
export interface LoteConsolidados {
  id: string;
  estado: EstadoLoteConsolidados;
  tipoDocumento: TipoDocumentoLote;
  /** Congelado al crear (CF-06). */
  total: number;
  /** incluidos + omitidos. */
  procesados: number;
  incluidos: number;
  omitidos: number;
  /** De los incluidos, cuántos no tenían consolidado y se generaron. */
  generados?: number;
  creadoEn: string;
  terminadoEn?: string | null;
  expiraEn?: string | null;
  nombreBase?: string;
  /**
   * HU #13417/#13419 — lote creado desde la vista de red: `red` (toda la red) o `hija` (acotado a
   * una compañía). Ausente en el lote propio.
   */
  alcanceRed?: 'red' | 'hija' | null;
  /**
   * HU #13419 AC6 — uuid de la compañía hija cuando `alcanceRed === 'hija'`; `null`/ausente en
   * `'red'` y en el lote propio. El nombre NO viaja: el aviso lo resuelve contra `/network/children`.
   */
  alcanceHijaId?: string | null;
  /** Vacío hasta que el lote termina. */
  partes: ParteLoteConsolidados[];
}

/**
 * Cuerpo de `POST /api/v1/tramites/consolidados/lotes`. La selección viaja TAL CUAL la produce
 * `useSeleccionLote` (#13380): `{ modo, ids, excluidos, filtro }`. En modo `ids` el servidor ignora
 * `excluidos`/`filtro`; en modo `filtro` ignora `ids`.
 */
export interface CrearLoteConsolidadosRequest<TFiltro = unknown> {
  tipoDocumento: TipoDocumentoLote;
  /** CF-08: el usuario vio y aceptó el efecto declarado. Siempre `true`. */
  confirmaEfectos: true;
  /**
   * HU #13419 — alcance de la tabla en la RAÍZ, para los modos `ids` y `filtro`. Ausente = el
   * servidor asume el propio (bandeja OT, Super Admin). `seleccion.filtro.alcanceRed` está
   * deprecado en el contrato y el cliente nunca lo envía.
   */
  alcanceRed?: AlcanceRedSolicitado;
  seleccion: ModeloSeleccionLote<TFiltro>;
}

/** Cuerpo del 409: ya hay un lote activo del usuario. */
export interface LoteActivoConflict {
  error: 'lote_activo';
  loteActivoId: string;
  detail?: string;
}

/**
 * HU #13388 — cuerpo del 409 de `POST /api/v1/consolidados/lotes/{loteId}/cancelacion`: el lote ya
 * terminó antes de cancelarlo (contrato §4 del diseño #13307).
 */
export interface LoteTerminadoConflict {
  error: 'lote_terminado';
  estado: Extract<EstadoLoteConsolidados, 'completado' | 'completado_con_omitidos' | 'fallido' | 'expirado'>;
  detail?: string;
}

/** Etiqueta en español del estado del lote (sin jerga del backend en pantalla). */
export const ETIQUETA_ESTADO_LOTE: Record<EstadoLoteConsolidados, string> = {
  en_cola: 'en cola',
  en_proceso: 'en proceso',
  empaquetando: 'empaquetando',
  completado: 'completada',
  completado_con_omitidos: 'completada con omitidos',
  fallido: 'fallida',
  cancelado: 'cancelada',
  expirado: 'expirada',
};
