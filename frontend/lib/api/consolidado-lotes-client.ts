import { apiUrl, tenantHeader } from './tramites-client';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import { TOPE_SELECCION_LOTE } from '@/hooks/useSeleccionLote';
import type {
  CodigoErrorLote,
  CrearLoteConsolidadosRequest,
  LoteConsolidados,
} from './types-consolidado-lotes';

/**
 * HU #13381 (épica #13216) — cliente del motor de lotes de descarga masiva de consolidados
 * (ADR-0070, contrato §5 del diseño de la Feature #13306).
 *
 * <p>Crear vive bajo `/api/v1/tramites/consolidados/lotes` (lo hace el Gestor/Radicador desde el
 * listado); leer, descargar y cancelar viven en el prefijo neutro `/api/v1/consolidados/lotes`.
 * Las partes (descarga del ZIP) y la cancelación las consume el seguimiento (#13382 / FA2).</p>
 */

const RUTA_CREAR = '/api/v1/tramites/consolidados/lotes';
const RUTA_LOTES = '/api/v1/consolidados/lotes';

const formatoMiles = (n: number) => n.toLocaleString('es-CO');

/** AC4 — 503 (motor apagado o auditoría no registrada) y falla de red. Texto aprobado. */
export const MENSAJE_REINTENTAR_DESCARGA = 'No se pudo completar la descarga, intente de nuevo';

/** AC4 — 422: la selección (o la búsqueda rápida) supera lo que el motor admite. */
export const MENSAJE_TOPE_DESCARGA = `La selección supera el tope de ${formatoMiles(
  TOPE_SELECCION_LOTE,
)} trámites para una descarga. Ajusta el filtro para acotarla e intenta de nuevo.`;

/** AC3 — 409 `lote_activo`. */
export const MENSAJE_LOTE_ACTIVO =
  'Ya hay una descarga en curso. Espera a que termine para crear otra; tu selección se conserva.';

const MENSAJE_SIN_PERMISO = 'No tienes permiso para la descarga masiva de consolidados.';

/**
 * Error de una llamada al motor de lotes. `status` 0 = falla de red (no hubo respuesta HTTP).
 * `codigo` es el código estable del motor (`error` en la raíz o en `extensions`), o `null`.
 */
export class ConsolidadoLotesApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly codigo: CodigoErrorLote | string | null,
    public readonly loteActivoId: string | null = null,
  ) {
    super(status === 0 ? 'Sin conexión con el servidor' : `Error ${status} del motor de lotes`);
    this.name = 'ConsolidadoLotesApiError';
  }
}

/** Lee `error` y `loteActivoId` de la raíz o de `extensions` (ProblemDetails). */
function leerProblema(texto: string): { codigo: string | null; loteActivoId: string | null } {
  if (!texto) return { codigo: null, loteActivoId: null };
  try {
    const raw = JSON.parse(texto) as Record<string, unknown> | null;
    if (!raw || typeof raw !== 'object') return { codigo: null, loteActivoId: null };
    const ext =
      raw.extensions && typeof raw.extensions === 'object'
        ? (raw.extensions as Record<string, unknown>)
        : {};
    const codigo = raw.error ?? ext.error;
    const lote = raw.loteActivoId ?? ext.loteActivoId;
    return {
      codigo: typeof codigo === 'string' ? codigo : null,
      loteActivoId: typeof lote === 'string' ? lote : null,
    };
  } catch {
    // Cuerpo no JSON (HTML de un gateway ante 502/503/504): nunca se vuelca al usuario.
    return { codigo: null, loteActivoId: null };
  }
}

async function llamar(path: string, init: RequestInit = {}): Promise<Response> {
  let res: Response;
  try {
    res = await fetch(apiUrl(path), {
      ...init,
      headers: { ...(tenantHeader() as Record<string, string>), ...(init.headers as Record<string, string>) },
    });
  } catch {
    throw new ConsolidadoLotesApiError(0, null);
  }
  if (!res.ok) {
    const { codigo, loteActivoId } = leerProblema(await res.text().catch(() => ''));
    throw new ConsolidadoLotesApiError(res.status, codigo, loteActivoId);
  }
  return res;
}

/** Cómo debe reaccionar la UI ante un error al crear el lote. */
export type ResultadoErrorCrearLote =
  | { tipo: 'lote_activo'; mensaje: string; loteActivoId: string | null }
  | { tipo: 'tope'; mensaje: string }
  | { tipo: 'permiso'; mensaje: string }
  | { tipo: 'reintentar'; mensaje: string };

export function interpretarErrorCrearLote(err: unknown): ResultadoErrorCrearLote {
  if (err instanceof ConsolidadoLotesApiError) {
    if (err.status === 409 && (err.codigo === 'lote_activo' || err.loteActivoId)) {
      return { tipo: 'lote_activo', mensaje: MENSAJE_LOTE_ACTIVO, loteActivoId: err.loteActivoId };
    }
    if (err.status === 422) return { tipo: 'tope', mensaje: MENSAJE_TOPE_DESCARGA };
    if (err.status === 403) return { tipo: 'permiso', mensaje: MENSAJE_SIN_PERMISO };
  }
  // 503 (motor_inactivo / auditoria_no_registrada), red y cualquier otro fallo: reintento.
  return { tipo: 'reintentar', mensaje: MENSAJE_REINTENTAR_DESCARGA };
}

export const consolidadoLotesClient = {
  /**
   * Crea el lote (202). Siempre con `confirmaEfectos: true`: solo se llama tras la confirmación del
   * modal (AC1/AC2). Lanza {@link ConsolidadoLotesApiError} en 409/422/503/red.
   */
  crearLote: async <TFiltro>(params: {
    seleccion: ModeloSeleccionLote<TFiltro>;
    tipoDocumento?: CrearLoteConsolidadosRequest['tipoDocumento'];
  }): Promise<LoteConsolidados> => {
    const cuerpo: CrearLoteConsolidadosRequest<TFiltro> = {
      tipoDocumento: params.tipoDocumento ?? 'consolidado',
      confirmaEfectos: true,
      seleccion: params.seleccion,
    };
    const res = await llamar(RUTA_CREAR, {
      method: 'POST',
      headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify(cuerpo),
    });
    return (await res.json()) as LoteConsolidados;
  },

  /** Lote por id (solo del dueño). 404 si no existe o no es del usuario. */
  obtenerLote: async (loteId: string): Promise<LoteConsolidados> => {
    const res = await llamar(`${RUTA_LOTES}/${encodeURIComponent(loteId)}`);
    return (await res.json()) as LoteConsolidados;
  },

  /** Lote activo o último terminal retenido; `null` en 204. Lo usa el seguimiento (#13382). */
  obtenerLoteActual: async (): Promise<LoteConsolidados | null> => {
    const res = await llamar(`${RUTA_LOTES}/actual`);
    if (res.status === 204) return null;
    return (await res.json()) as LoteConsolidados;
  },
};
