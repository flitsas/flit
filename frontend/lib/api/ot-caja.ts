// Módulo Caja de FLIT 2.0 (Feature #14453, Épica #14210) — cliente de `/api/v1/ot/caja/*`.
// El JSON es el mismo que antes consumía el portal de caja: desglose del trámite, resultado del cobro y cobros del día.
//   GET  /api/v1/ot/caja/tramites/{codigo}                → desglose a cobrar
//   POST /api/v1/ot/caja/tramites/{codigo}/confirmar      → multipart + Idempotency-Key
//   GET  /api/v1/ot/caja/cobros?date=&page=&pageSize=     → cobros del día del organismo
//   POST /api/v1/ot/caja/cobros/{cobroId}/anular          → solo Admin OT
// Los errores son problem+json con `code`; un 404 sin `code` es la caja apagada para el organismo.
import { clearToken, emitSessionExpired } from "@/lib/auth/session";
import { apiFetch, friendlyErrorMessage, getToken, resolveApiUrl, type RequestOptions } from "./client";
import type { CajaCobro, CajaCobroLista } from "./admin-ot-portal-caja";
import { ApiError } from "./types";

export type { CajaCobro, CajaCobroLista } from "./admin-ot-portal-caja";
export { MOTIVO_ANULACION_MAX, MOTIVO_ANULACION_MIN } from "./admin-ot-portal-caja";

export const OT_CAJA_BASE = "/api/v1/ot/caja";

export type MedioCaja = "efectivo" | "datafono" | "transferencia" | "otro";

export const MEDIOS_CAJA: readonly { id: MedioCaja; etiqueta: string }[] = [
  { id: "efectivo", etiqueta: "Efectivo" },
  { id: "datafono", etiqueta: "Datáfono" },
  { id: "transferencia", etiqueta: "Transferencia" },
  { id: "otro", etiqueta: "Otro" },
];

export const ETIQUETA_MEDIO: Record<string, string> = Object.fromEntries(MEDIOS_CAJA.map((m) => [m.id, m.etiqueta]));

export interface LineaCobro {
  concepto: string;
  valorCop: number;
}

export interface TramiteParaCobrar {
  codigo: string;
  tipoTramite: { codigo: string; nombre: string };
  organismo: { codigo: string; nombre: string };
  lineas: LineaCobro[];
  totalCop: number;
  versionTarifa: string;
  venceEn: string;
}

export interface CobroConfirmado {
  codigo: string;
  valorCop: number;
  medio: string;
  medioDetalle?: string | null;
  pagadoEn: string;
  mensaje: string;
}

export interface ConfirmacionPedida {
  codigo: string;
  medio: MedioCaja;
  medioDetalle?: string;
  totalVistoCop: number;
  prefactura?: File | null;
  /** Una por intento de confirmación: reintentar con la misma no cobra dos veces. */
  llave: string;
}

export interface CobrosFiltro {
  /** AAAA-MM-DD en hora de Colombia; sin él, hoy. */
  date?: string;
  page: number;
  pageSize: number;
}

/** Prefactura: PDF, JPG o PNG de hasta 5 MB (el servidor vuelve a validar por la firma del archivo). */
export const PREFACTURA_MAX_BYTES = 5 * 1024 * 1024;
export const PREFACTURA_TIPOS: readonly string[] = ["application/pdf", "image/jpeg", "image/png"];
export const DETALLE_MEDIO_MAX = 120;

/** Código público del trámite. */
export const FORMATO_CODIGO = /^PN-\d{4}-\d{6}$/;

/** Error de la caja con lo que la pantalla necesita: el `code` del problem+json, la espera de un 429 y el total nuevo. */
export class CajaError extends ApiError {
  constructor(
    status: number,
    message: string,
    readonly code: string,
    body?: unknown,
    /** Segundos de `Retry-After` (429). */
    readonly reintentarEnSegundos?: number,
    /** Nuevo total cuando cambió entre la búsqueda y la confirmación (409 `total_cambio`). */
    readonly totalActualCop?: number,
  ) {
    super(status, message, body);
    this.name = "CajaError";
  }

  /** El servidor no llegó a decidir (sin red o caído): reintentar con la misma llave es seguro. */
  get sinRespuestaDefinitiva(): boolean {
    return this.status === 0 || this.status >= 500;
  }
}

function numero(valor: unknown): number | undefined {
  return typeof valor === "number" && Number.isFinite(valor) ? valor : undefined;
}

export function comoCajaError(err: unknown, retryAfter?: string | null): CajaError {
  if (err instanceof CajaError) return err;
  if (err instanceof ApiError) {
    const cuerpo = (err.body ?? {}) as { code?: unknown; totalActualCop?: unknown; totalCop?: unknown };
    const espera = Number(retryAfter);
    return new CajaError(
      err.status,
      err.message,
      typeof cuerpo.code === "string" ? cuerpo.code : "",
      err.body,
      Number.isFinite(espera) && espera > 0 ? espera : undefined,
      numero(cuerpo.totalActualCop) ?? numero(cuerpo.totalCop),
    );
  }
  return new CajaError(0, "Sin conexión", "sin_conexion");
}

/** Llamada JSON con el cliente de FLIT 2.0; rescata `Retry-After` de la respuesta cruda. */
async function pedir<T>(path: string, options: RequestOptions = {}): Promise<T> {
  let retryAfter: string | null = null;
  try {
    return await apiFetch<T>(path, {
      ...options,
      onResponse: (r) => {
        retryAfter = r.headers.get("retry-after");
      },
    });
  } catch (err) {
    throw comoCajaError(err, retryAfter);
  }
}

type CuerpoError = { code?: unknown; error?: unknown; detail?: unknown; title?: unknown; totalActualCop?: unknown; totalCop?: unknown };

/** La confirmación va en multipart (prefactura) y con `Idempotency-Key`, que `apiFetch` no maneja. */
async function confirmarCobro(pedido: ConfirmacionPedida): Promise<CobroConfirmado> {
  const form = new FormData();
  form.set("medioCaja", pedido.medio);
  if (pedido.medio === "otro" && pedido.medioDetalle) form.set("detalle", pedido.medioDetalle);
  form.set("totalVisto", String(pedido.totalVistoCop));
  if (pedido.prefactura) form.set("prefactura", pedido.prefactura);

  const token = getToken();
  let respuesta: Response;
  try {
    respuesta = await fetch(resolveApiUrl(`${OT_CAJA_BASE}/tramites/${encodeURIComponent(pedido.codigo)}/confirmar`), {
      method: "POST",
      headers: { "Idempotency-Key": pedido.llave, ...(token ? { Authorization: `Bearer ${token}` } : {}) },
      body: form,
    });
  } catch {
    throw new CajaError(0, "Sin conexión", "sin_conexion");
  }
  if (!respuesta.ok) {
    let cuerpo: CuerpoError | null = null;
    try {
      cuerpo = (await respuesta.json()) as CuerpoError;
    } catch {
      cuerpo = null;
    }
    if (respuesta.status === 401 && cuerpo?.code === "SESSION_EXPIRED") {
      clearToken();
      emitSessionExpired();
    }
    throw new CajaError(
      respuesta.status,
      friendlyErrorMessage(cuerpo),
      typeof cuerpo?.code === "string" ? cuerpo.code : "",
      cuerpo,
      Number(respuesta.headers.get("retry-after")) || undefined,
      numero(cuerpo?.totalActualCop) ?? numero(cuerpo?.totalCop),
    );
  }
  return (await respuesta.json()) as CobroConfirmado;
}

/** Cliente de la caja; los componentes lo reciben por props para poder probarlos sin red. */
export interface OtCajaApi {
  tramite: (codigo: string, signal?: AbortSignal) => Promise<TramiteParaCobrar>;
  confirmar: (pedido: ConfirmacionPedida) => Promise<CobroConfirmado>;
  cobros: (filtro: CobrosFiltro, signal?: AbortSignal) => Promise<CajaCobroLista>;
  anular: (cobro: Pick<CajaCobro, "cobroId" | "rowVersion">, motivo: string) => Promise<unknown>;
}

export const otCajaApi: OtCajaApi = {
  tramite: (codigo, signal) => pedir<TramiteParaCobrar>(`${OT_CAJA_BASE}/tramites/${encodeURIComponent(codigo)}`, { signal }),
  confirmar: confirmarCobro,
  cobros: async (f, signal) => {
    const lista = await pedir<CajaCobroLista>(`${OT_CAJA_BASE}/cobros`, {
      query: { date: f.date, page: f.page, pageSize: f.pageSize },
      signal,
    });
    // /api/v1/ot/caja/cobros manda quién cobró en `usuario`; la tabla compartida con la administración lee `pagadoPorNombre`.
    return {
      ...lista,
      items: lista.items.map((c) => {
        const usuario = (c as CajaCobro & { usuario?: string | null }).usuario;
        return { ...c, pagadoPorNombre: c.pagadoPorNombre ?? usuario ?? null };
      }),
    };
  },
  anular: (c, motivo) =>
    pedir<unknown>(`${OT_CAJA_BASE}/cobros/${encodeURIComponent(c.cobroId)}/anular`, {
      method: "POST",
      body: { motivo: motivo.trim(), rowVersion: c.rowVersion },
    }),
};

/** Una llave de idempotencia por intento de confirmación. */
export function nuevaLlave(): string {
  return globalThis.crypto?.randomUUID?.() ?? `${Date.now()}-${Math.random().toString(16).slice(2)}`;
}
