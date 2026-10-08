import { apiUrl, tenantHeader } from './tramites-client';
import { getToken } from './client';
import type { ModeloSeleccionLote } from '@/hooks/useSeleccionLote';
import { downloadFile } from './download';
import { ApiError, ApiValidationError } from './types';
import type {
  CodigoErrorLote,
  CrearLoteConsolidadosRequest,
  LoteConsolidados,
  ParteLoteConsolidados,
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

const AJUSTAR_FILTRO = 'Ajusta el filtro para acotarla e intenta de nuevo.';

/**
 * AC4 — 422 sin cifras en el cuerpo (p. ej. más ids o excluidos de los que admite una petición):
 * la selección supera lo que el motor admite. Sin número: el tope total lo edita el Super Admin
 * (HU #13420) y el frontend no lo conoce.
 */
export const MENSAJE_TOPE_DESCARGA = `La selección supera el tope de trámites para una descarga. ${AJUSTAR_FILTRO}`;

/**
 * HU #13420 — 422 `seleccion_excede_tope`: el servidor manda `total` (trámites de la selección
 * resuelta) y `tope` (`maxItemsPerBatch` vigente). El mensaje usa esas cifras; sin ellas cae a
 * {@link MENSAJE_TOPE_DESCARGA}.
 */
export function mensajeTopeDescarga(total: number | null, tope: number | null): string {
  if (tope === null) return MENSAJE_TOPE_DESCARGA;
  const prefijo =
    total === null
      ? 'La selección supera'
      : `La selección tiene ${formatoMiles(total)} trámites y supera`;
  return `${prefijo} el tope de ${formatoMiles(tope)} trámites para una descarga. ${AJUSTAR_FILTRO}`;
}

/** AC3 — 409 `lote_activo`. */
export const MENSAJE_LOTE_ACTIVO =
  'Ya hay una descarga en curso. Espera a que termine para crear otra; tu selección se conserva.';

/** HU #13388 AC6 — 404/403 al cancelar el lote. Texto aprobado. */
export const MENSAJE_NO_SE_PUDO_CANCELAR = 'No se pudo cancelar la descarga';

const MENSAJE_SIN_PERMISO = 'No tienes permiso para la descarga masiva de consolidados.';

/**
 * Error de una llamada al motor de lotes. `status` 0 = falla de red (no hubo respuesta HTTP).
 * `codigo` es el código estable del motor (`error` en la raíz o en `extensions`), o `null`.
 * `total` y `tope` solo vienen en el 422 `seleccion_excede_tope` por tope total (HU #13420).
 */
export class ConsolidadoLotesApiError extends Error {
  constructor(
    public readonly status: number,
    public readonly codigo: CodigoErrorLote | string | null,
    public readonly loteActivoId: string | null = null,
    public readonly total: number | null = null,
    public readonly tope: number | null = null,
  ) {
    super(status === 0 ? 'Sin conexión con el servidor' : `Error ${status} del motor de lotes`);
    this.name = 'ConsolidadoLotesApiError';
  }
}

interface ProblemaLote {
  codigo: string | null;
  loteActivoId: string | null;
  total: number | null;
  tope: number | null;
}

const PROBLEMA_VACIO: ProblemaLote = { codigo: null, loteActivoId: null, total: null, tope: null };

/** Lee `error`, `loteActivoId`, `total` y `tope` de la raíz o de `extensions` (ProblemDetails). */
function leerProblema(texto: string): ProblemaLote {
  if (!texto) return PROBLEMA_VACIO;
  try {
    return leerProblemaObjeto(JSON.parse(texto));
  } catch {
    // Cuerpo no JSON (HTML de un gateway ante 502/503/504): nunca se vuelca al usuario.
    return PROBLEMA_VACIO;
  }
}

const entero = (v: unknown): number | null =>
  typeof v === 'number' && Number.isInteger(v) && v >= 0 ? v : null;

/** Igual que {@link leerProblema}, sobre un cuerpo ya parseado (p. ej. `ApiError.body` de `downloadFile`). */
function leerProblemaObjeto(cuerpo: unknown): ProblemaLote {
  if (!cuerpo || typeof cuerpo !== 'object') return PROBLEMA_VACIO;
  const raw = cuerpo as Record<string, unknown>;
  const ext =
    raw.extensions && typeof raw.extensions === 'object'
      ? (raw.extensions as Record<string, unknown>)
      : {};
  const codigo = raw.error ?? ext.error;
  const lote = raw.loteActivoId ?? ext.loteActivoId;
  return {
    codigo: typeof codigo === 'string' ? codigo : null,
    loteActivoId: typeof lote === 'string' ? lote : null,
    total: entero(raw.total ?? ext.total),
    tope: entero(raw.tope ?? ext.tope),
  };
}

/**
 * HU #13394 — traduce el error de un cliente que no pasa por {@link llamar} (p. ej. `apiFetch` de
 * la bandeja OT) a {@link ConsolidadoLotesApiError}, para que {@link interpretarErrorCrearLote}
 * lo lea igual. Una cancelación (`AbortError`) se devuelve tal cual: no es una falla de red.
 */
export function aErrorDeLote(err: unknown): unknown {
  if (err instanceof ConsolidadoLotesApiError) return err;
  if ((err as { name?: unknown } | null)?.name === 'AbortError') return err;
  if (err instanceof ApiError) {
    const { codigo, loteActivoId, total, tope } = leerProblemaObjeto(err.body);
    return new ConsolidadoLotesApiError(err.status, codigo, loteActivoId, total, tope);
  }
  if (err instanceof ApiValidationError) return new ConsolidadoLotesApiError(err.status, null);
  return new ConsolidadoLotesApiError(0, null);
}

/**
 * HU #13387 (W-f) — cabeceras con las que se pidió el listado (`searchPayload` de
 * `tramites-client.ts`): Bearer y `X-Tenant-Id` SOLO si el listado llevaba `filterTenantId`. Nunca
 * se cae al tenant activo ni al del JWT: un Super Admin que listó «todas» crea el lote igual, y el
 * servidor no lo acota a su compañía interna.
 */
export interface CabecerasDelListado {
  filterTenantId?: string;
}

function cabecerasComoListado({ filterTenantId }: CabecerasDelListado): Record<string, string> {
  const headers: Record<string, string> = {};
  const token = getToken();
  if (token) headers.Authorization = `Bearer ${token}`;
  if (filterTenantId) headers['X-Tenant-Id'] = filterTenantId;
  return headers;
}

async function llamar(
  path: string,
  init: RequestInit = {},
  base: Record<string, string> = tenantHeader() as Record<string, string>,
): Promise<Response> {
  let res: Response;
  try {
    res = await fetch(apiUrl(path), {
      ...init,
      headers: { ...base, ...(init.headers as Record<string, string>) },
    });
  } catch {
    throw new ConsolidadoLotesApiError(0, null);
  }
  if (!res.ok) {
    const { codigo, loteActivoId, total, tope } = leerProblema(await res.text().catch(() => ''));
    throw new ConsolidadoLotesApiError(res.status, codigo, loteActivoId, total, tope);
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
    if (err.status === 422) return { tipo: 'tope', mensaje: mensajeTopeDescarga(err.total, err.tope) };
    if (err.status === 403) return { tipo: 'permiso', mensaje: MENSAJE_SIN_PERMISO };
  }
  // 503 (motor_inactivo / auditoria_no_registrada), red y cualquier otro fallo: reintento.
  return { tipo: 'reintentar', mensaje: MENSAJE_REINTENTAR_DESCARGA };
}

export const consolidadoLotesClient = {
  /**
   * Crea el lote (202). Siempre con `confirmaEfectos: true`: solo se llama tras la confirmación del
   * modal (AC1/AC2). Lanza {@link ConsolidadoLotesApiError} en 409/422/503/red.
   *
   * HU #13387 — `cabecerasDelListado` (Super Admin): la creación viaja con las mismas cabeceras
   * que el listado, no con el tenant activo/JWT. Sin él (Gestor) las cabeceras no cambian.
   */
  crearLote: async <TFiltro>(params: {
    seleccion: ModeloSeleccionLote<TFiltro>;
    tipoDocumento?: CrearLoteConsolidadosRequest['tipoDocumento'];
    cabecerasDelListado?: CabecerasDelListado;
  }): Promise<LoteConsolidados> => {
    const cuerpo: CrearLoteConsolidadosRequest<TFiltro> = {
      tipoDocumento: params.tipoDocumento ?? 'consolidado',
      confirmaEfectos: true,
      seleccion: params.seleccion,
    };
    const res = await llamar(
      RUTA_CREAR,
      {
        method: 'POST',
        headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify(cuerpo),
      },
      params.cabecerasDelListado ? cabecerasComoListado(params.cabecerasDelListado) : undefined,
    );
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

  /**
   * HU #13388 — cancela el lote en curso del usuario (`POST …/{loteId}/cancelacion`, un solo clic,
   * sin cuerpo). 202 devuelve el lote `cancelado` (también si ya lo estaba: idempotente). Mismas
   * cabeceras que {@link consolidadoLotesClient.obtenerLote}: la ruta neutra identifica al dueño por
   * el `sub` del JWT. Lanza {@link ConsolidadoLotesApiError}: 409 `lote_terminado`
   * (cuerpo `LoteTerminadoConflict`), 404, 403, 503 `auditoria_no_registrada` o 0 (red).
   */
  cancelar: async (loteId: string): Promise<LoteConsolidados> => {
    const res = await llamar(`${RUTA_LOTES}/${encodeURIComponent(loteId)}/cancelacion`, { method: 'POST' });
    return (await res.json()) as LoteConsolidados;
  },

  /**
   * HU #13382 — descarga una parte del lote terminado (`GET …/{loteId}/partes/{numero}`, ZIP en
   * streaming; el nombre real llega en `Content-Disposition`). Lanza {@link ConsolidadoLotesApiError}:
   * 410 `descarga_expirada`, 409 `lote_no_terminado`, 404, 503 `auditoria_no_registrada` o 0 (red).
   */
  descargarParte: async (
    loteId: string,
    parte: Pick<ParteLoteConsolidados, 'numero' | 'nombreArchivo'>,
  ): Promise<void> => {
    try {
      await downloadFile(`${RUTA_LOTES}/${encodeURIComponent(loteId)}/partes/${parte.numero}`, {
        fallbackFilename: parte.nombreArchivo,
      });
    } catch (err) {
      if (err instanceof ApiError) {
        throw new ConsolidadoLotesApiError(err.status, leerProblemaObjeto(err.body).codigo);
      }
      throw new ConsolidadoLotesApiError(0, null);
    }
  },
};
