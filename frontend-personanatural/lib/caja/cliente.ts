// Cliente tipado de la caja del organismo. Habla con el proxy del mismo host (`/api/v1/persona-natural/caja`), que adjunta
// el Bearer de la sesión OIDC del servidor: aquí no hay tokens. Errores del BFF: `{ code, message }`.
import { BASE_BFF } from "@/lib/api/cliente";
import type { CobroConfirmado, CobrosDelDia, ConfirmacionPedida, SesionCaja, TramiteParaCobrar } from "./tipos";

export const BASE_CAJA = `${BASE_BFF}/caja`;

export class CajaError extends Error {
  constructor(
    readonly code: string,
    message: string,
    readonly status: number,
    /** Segundos de `Retry-After` (429). */
    readonly reintentarEnSegundos?: number,
    /** Nuevo total cuando cambió entre la búsqueda y la confirmación (409 `total_cambio`). */
    readonly totalActualCop?: number,
  ) {
    super(message);
    this.name = "CajaError";
  }

  /** La sesión venció o no existe: hay que volver al login. */
  get sinSesion(): boolean {
    return this.status === 401;
  }

  /** El servidor no llegó a decidir (sin red o caído): reintentar con la misma llave es seguro. */
  get sinRespuestaDefinitiva(): boolean {
    return this.status === 0 || this.status >= 500;
  }
}

async function comoError(respuesta: Response): Promise<CajaError> {
  let code = `http_${respuesta.status}`;
  let message = respuesta.statusText || "Error";
  let totalActualCop: number | undefined;
  try {
    const datos = (await respuesta.json()) as { code?: unknown; message?: unknown; totalActualCop?: unknown };
    if (typeof datos.code === "string") code = datos.code;
    if (typeof datos.message === "string") message = datos.message;
    if (typeof datos.totalActualCop === "number") totalActualCop = datos.totalActualCop;
  } catch {
    // Cuerpo vacío o que no es JSON: queda el código por estado.
  }
  const espera = Number(respuesta.headers.get("retry-after"));
  return new CajaError(code, message, respuesta.status, Number.isFinite(espera) && espera > 0 ? espera : undefined, totalActualCop);
}

async function pedir<T>(ruta: string, init: RequestInit = {}): Promise<T> {
  let respuesta: Response;
  try {
    respuesta = await fetch(`${BASE_CAJA}${ruta}`, { ...init, credentials: "same-origin", cache: "no-store" });
  } catch {
    throw new CajaError("sin_conexion", "Sin conexión", 0);
  }
  if (!respuesta.ok) throw await comoError(respuesta);
  return (await respuesta.json()) as T;
}

export const cajaApi = {
  sesion: (signal?: AbortSignal) => pedir<SesionCaja>("/sesion", { signal }),

  tramite: (codigo: string, signal?: AbortSignal) => pedir<TramiteParaCobrar>(`/tramites/${encodeURIComponent(codigo)}`, { signal }),

  /** `totalVistoCop` es solo para comparar: el servidor cobra el total vigente y responde 409 `total_cambio` si difiere. */
  confirmar: (pedido: ConfirmacionPedida) => {
    const form = new FormData();
    form.set("medio", pedido.medio);
    if (pedido.medio === "otro" && pedido.medioDetalle) form.set("medioDetalle", pedido.medioDetalle);
    form.set("totalVistoCop", String(pedido.totalVistoCop));
    if (pedido.prefactura) form.set("prefactura", pedido.prefactura);
    return pedir<CobroConfirmado>(`/tramites/${encodeURIComponent(pedido.codigo)}/confirmar`, {
      method: "POST",
      body: form,
      headers: { "Idempotency-Key": pedido.llave },
    });
  },

  cobrosHoy: (pagina: number, tamano: number, signal?: AbortSignal) =>
    pedir<CobrosDelDia>(`/cobros/hoy?pagina=${pagina}&tamano=${tamano}`, { signal }),
};
