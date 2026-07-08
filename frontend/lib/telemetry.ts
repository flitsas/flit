// Telemetría de uso del frontend (Reportes 2.0 · HU-A). Cliente fire-and-forget:
// cola en memoria + flush por lotes contra POST /api/v1/analytics/events.
// REGLAS DURAS (contrato docs/contratos-reportes-v2.md §4.6/§7/§9):
//   · La telemetría JAMÁS rompe el flujo de la app: todo va en try/catch y ante
//     cualquier fallo se degrada en silencio (console.debug como mucho).
//   · Taxonomía cerrada: eventos fuera de la lista se descartan al encolar.
//   · SIN PII en metadata (prohibido: nombres, documentos, emails, placas, VIN).
//   · El backend resuelve tenant/usuario desde el JWT: aquí nunca se envían.
// Transporte: fetch(..., { keepalive: true }) SIEMPRE (también en pagehide /
// visibilitychange→hidden): sendBeacon no permite el header Authorization y el
// endpoint exige el JWT, así que se usa solo fetch keepalive (decisión del contrato).
import { API_BASE_URL, getToken } from "@/lib/api/client";

/** Taxonomía de eventos emitibles desde el front (contrato §7). */
const FRONT_EVENT_TYPES = new Set([
  "module_view",
  "wizard_step_view",
  "wizard_step_complete",
  "wizard_step_exit",
  "wizard_abandon",
  "wizard_complete",
]);

export interface UsageEvent {
  eventType: string;
  module?: string;
  stepKey?: string;
  procedureInstanceId?: string | null;
  /** Milisegundos (>= 0). Negativos se descartan al encolar. */
  durationMs?: number;
  /** ISO-8601; default: instante de encolado. */
  occurredAt?: string;
  /** Contexto extra (jsonb). SIN PII. */
  metadata?: Record<string, unknown>;
}

/** Flush al alcanzar este tamaño de cola (el endpoint admite hasta 50 por lote). */
const BATCH_SIZE = 20;
/** Flush periódico si hay eventos pendientes. */
const FLUSH_INTERVAL_MS = 10_000;
/** Tope duro de la cola en memoria: por encima se descartan eventos (best-effort). */
const MAX_QUEUE = 200;

let queue: UsageEvent[] = [];
let flushTimer: ReturnType<typeof setTimeout> | null = null;
let listenersInstalled = false;
/** Módulos ya reportados en esta sesión de página (module_view: 1 por sesión/módulo). */
let seenModules = new Set<string>();

/**
 * Encola un evento de uso. Valida la taxonomía y nunca lanza. El envío ocurre
 * en lotes (20 eventos o cada 10 s) o al ocultarse la página.
 */
export function trackEvent(evt: UsageEvent): void {
  try {
    if (!evt || !FRONT_EVENT_TYPES.has(evt.eventType)) return;
    if (queue.length >= MAX_QUEUE) return;

    queue.push({
      ...evt,
      durationMs:
        typeof evt.durationMs === "number" && evt.durationMs >= 0
          ? Math.round(evt.durationMs)
          : undefined,
      occurredAt: evt.occurredAt ?? new Date().toISOString(),
    });

    installLifecycleListeners();
    if (queue.length >= BATCH_SIZE) {
      void flushTelemetry();
    } else {
      scheduleFlush();
    }
  } catch (err) {
    debugLog("trackEvent falló", err);
  }
}

/**
 * `module_view`: una sola vez por sesión de página y módulo (contrato §7).
 * Pensado para el dock de módulos (?m=) — idempotente por módulo.
 */
export function trackModuleView(module: string): void {
  try {
    if (!module || seenModules.has(module)) return;
    seenModules.add(module);
    trackEvent({ eventType: "module_view", module });
  } catch (err) {
    debugLog("trackModuleView falló", err);
  }
}

/**
 * Envía los eventos pendientes (hasta 50 por request, límite del endpoint).
 * Fire-and-forget: sin token no envía (y descarta el lote); un fallo de red
 * descarta el lote sin reintentos ni ruido (console.debug como mucho).
 */
export async function flushTelemetry(): Promise<void> {
  try {
    if (flushTimer) {
      clearTimeout(flushTimer);
      flushTimer = null;
    }
    if (queue.length === 0) return;

    const events = queue.splice(0, 50);
    if (queue.length > 0) scheduleFlush();

    const token = getToken();
    if (!token) return; // sin sesión no hay a quién atribuir: se descarta.

    const base =
      API_BASE_URL ||
      (typeof window !== "undefined" ? window.location.origin : "");
    if (!base) return;

    await fetch(`${base}/api/v1/analytics/events`, {
      method: "POST",
      // keepalive: el request sobrevive a la navegación/cierre de pestaña
      // (sustituto de sendBeacon compatible con el header Authorization).
      keepalive: true,
      headers: {
        "Content-Type": "application/json",
        Authorization: `Bearer ${token}`,
      },
      body: JSON.stringify({ events }),
    });
  } catch (err) {
    debugLog("flush falló (lote descartado)", err);
  }
}

function scheduleFlush(): void {
  if (flushTimer || queue.length === 0) return;
  flushTimer = setTimeout(() => {
    flushTimer = null;
    void flushTelemetry();
  }, FLUSH_INTERVAL_MS);
}

/** Flush de última oportunidad al ocultarse/cerrarse la página (fetch keepalive). */
function installLifecycleListeners(): void {
  if (listenersInstalled || typeof window === "undefined") return;
  listenersInstalled = true;
  try {
    window.addEventListener("pagehide", () => void flushTelemetry());
    document.addEventListener("visibilitychange", () => {
      if (document.visibilityState === "hidden") void flushTelemetry();
    });
  } catch (err) {
    debugLog("no se pudieron instalar los listeners", err);
  }
}

function debugLog(message: string, err: unknown): void {
  try {
    // eslint-disable-next-line no-console
    console.debug(`[telemetry] ${message}`, err);
  } catch {
    // ni el log puede romper nada
  }
}

/** SOLO para tests: vacía cola, timers y módulos vistos. */
export function __resetTelemetryForTests(): void {
  queue = [];
  seenModules = new Set<string>();
  if (flushTimer) {
    clearTimeout(flushTimer);
    flushTimer = null;
  }
}

/** SOLO para tests: tamaño actual de la cola. */
export function __telemetryQueueSize(): number {
  return queue.length;
}
