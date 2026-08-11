// HU #11371 (AC3/AC4) — traduce el resultado de `sendNotificationTest` a mensajes de producto.
// El backend devuelve causas de lista cerrada (`buzon_no_configurado`, `limite_frecuencia`,
// `fallo_render`, `configuracion_incompleta`, `canal_invalido`) y, en un 200, un resultado con
// `success: false` / `outcome: "TransportFailed"`. Esta capa es la ÚNICA que decide qué texto ve
// la persona: nunca se muestra `message`/`error` crudo del backend (puede traer detalle del
// proveedor SMTP), salvo `retryAfterSeconds`, que sí es información honesta y útil.
import { ApiError } from "@/lib/api/types";
import type { NotificationTestSendResult } from "@/lib/api/admin-plataforma-notificaciones";

/** Causas de lista cerrada que puede devolver el envío de prueba (AC4). */
export type SendTestFailureCause =
  | "buzon_no_configurado"
  | "canal_invalido"
  | "configuracion_incompleta"
  | "plantilla_no_encontrada"
  | "limite_frecuencia"
  | "fallo_render"
  | "transporte_fallido"
  | "desconocida";

export interface SendTestFailure {
  cause: SendTestFailureCause;
  /** Mensaje de producto — nunca el `message` crudo del backend. */
  productMessage: string;
  /** Solo presente cuando `cause === "limite_frecuencia"`. */
  retryAfterSeconds?: number;
}

const FAILURE_MESSAGES: Record<Exclude<SendTestFailureCause, "limite_frecuencia">, string> = {
  buzon_no_configurado:
    "No hay un buzón de pruebas configurado. Configúralo abajo para poder enviar correos de prueba.",
  canal_invalido: "El canal seleccionado no es válido para enviar correos de prueba.",
  configuracion_incompleta:
    "El canal seleccionado no tiene su configuración completa (remitente o credenciales) en este ambiente.",
  plantilla_no_encontrada: "La plantilla ya no existe en el catálogo de notificaciones.",
  fallo_render: "No fue posible generar el contenido del correo de prueba.",
  transporte_fallido:
    "El transporte de correo rechazó el envío. Es un fallo del proveedor, no de la plantilla ni del buzón.",
  desconocida: "No fue posible completar el envío de la prueba.",
};

function limiteFrecuenciaMensaje(retryAfterSeconds?: number): string {
  if (typeof retryAfterSeconds === "number" && retryAfterSeconds > 0) {
    return `Se alcanzó el límite de frecuencia de envíos de prueba. Vuelve a intentarlo en ${retryAfterSeconds} segundos.`;
  }
  return "Se alcanzó el límite de frecuencia de envíos de prueba. Vuelve a intentarlo más tarde.";
}

const KNOWN_CAUSES = new Set<string>([
  "buzon_no_configurado",
  "canal_invalido",
  "configuracion_incompleta",
  "plantilla_no_encontrada",
  "limite_frecuencia",
  "fallo_render",
]);

/**
 * Mapea un `ApiError` lanzado por `sendNotificationTest` (400/404/429/500) a un mensaje de
 * producto de lista cerrada.
 *
 * Uso de ejemplo:
 * try { await sendNotificationTest(id, channel); }
 * catch (e) { const failure = mapSendTestApiError(e); }
 */
export function mapSendTestApiError(error: unknown): SendTestFailure {
  if (error instanceof ApiError) {
    const body = error.body as { error?: string; retryAfterSeconds?: number } | null;
    const rawCause = body?.error;
    const cause: SendTestFailureCause =
      rawCause && KNOWN_CAUSES.has(rawCause) ? (rawCause as SendTestFailureCause) : "desconocida";

    if (cause === "limite_frecuencia") {
      return {
        cause,
        productMessage: limiteFrecuenciaMensaje(body?.retryAfterSeconds),
        retryAfterSeconds: body?.retryAfterSeconds,
      };
    }

    return { cause, productMessage: FAILURE_MESSAGES[cause] };
  }

  return { cause: "desconocida", productMessage: FAILURE_MESSAGES.desconocida };
}

/** Resultado ya "honesto" de un envío 200 (éxito real o fallo de transporte). */
export interface SendTestOutcome {
  kind: "sent" | "transport-failed";
  /** Mensaje principal a mostrar. */
  productMessage: string;
  /** AC3 — siempre presente: la aceptación del transporte no garantiza la entrega. */
  disclaimer: string;
  /** AC8 (HU #11368) — declara honestamente que no hubo correo real, cuando aplica. */
  consoleTransportNotice?: string;
}

const DELIVERY_DISCLAIMER =
  "Que el transporte haya aceptado el correo no garantiza que llegue a la bandeja de entrada.";

const CONSOLE_TRANSPORT_NOTICE =
  "No se envió un correo real: en este ambiente el transporte es de consola (sin conexión SMTP real).";

/**
 * Traduce un `NotificationTestSendResult` (200 OK, éxito o fallo de transporte) a un mensaje
 * honesto de producto (AC3, AC4 causa "transporte_fallido").
 *
 * Uso de ejemplo:
 * const result = await sendNotificationTest(id, channel);
 * const outcome = mapSendTestOutcome(result);
 */
export function mapSendTestOutcome(result: NotificationTestSendResult): SendTestOutcome {
  const base: SendTestOutcome = result.success
    ? {
        kind: "sent",
        productMessage: "El correo de prueba se envió y el transporte lo aceptó.",
        disclaimer: DELIVERY_DISCLAIMER,
      }
    : {
        kind: "transport-failed",
        productMessage: FAILURE_MESSAGES.transporte_fallido,
        disclaimer: DELIVERY_DISCLAIMER,
      };

  if (result.isConsoleTransport) {
    base.consoleTransportNotice = CONSOLE_TRANSPORT_NOTICE;
  }

  return base;
}
