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
  | 'descarga_expirada'
  | 'auditoria_no_registrada'
  | 'motor_inactivo';

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
  seleccion: ModeloSeleccionLote<TFiltro>;
}

/** Cuerpo del 409: ya hay un lote activo del usuario. */
export interface LoteActivoConflict {
  error: 'lote_activo';
  loteActivoId: string;
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
