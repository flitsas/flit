// Cliente SignalR + fallback REST para export jobs (HU #11113 / ADR-0037).
import {
  HubConnection,
  HubConnectionBuilder,
  HubConnectionState,
  LogLevel,
} from "@microsoft/signalr";
import { API_BASE_URL, getToken } from "@/lib/api/client";
import { getExport, type ExportJob } from "@/lib/api/reporting-v2";

export type ExportProgressEvent = {
  jobId: string;
  status: string;
  progressPct: number;
};

export type ExportJobsHandlers = {
  onProgress?: (event: ExportProgressEvent) => void;
  onCompleted?: (event: ExportProgressEvent) => void;
  onFailed?: (event: ExportProgressEvent) => void;
};

export type ExportJobsClientOptions = {
  /** Proveedor de JWT; por defecto `getToken()`. Se reinvoca en reconexión. */
  getAccessToken?: () => string | null | Promise<string | null>;
  /** URL absoluta o relativa del hub (default: `{API_BASE}/hubs/export-jobs`). */
  hubUrl?: string;
  /** Intervalo de polling REST cuando el hub no está Connected (ms). */
  pollIntervalMs?: number;
  /** Máximo de intentos de polling antes de abandonar. */
  maxPollAttempts?: number;
};

const DEFAULT_POLL_MS = 5000;
const DEFAULT_MAX_POLLS = 60;

function resolveHubUrl(explicit?: string): string {
  if (explicit) return explicit;
  const base =
    API_BASE_URL ||
    (typeof window !== "undefined" ? window.location.origin : "http://localhost:3000");
  return new URL("/hubs/export-jobs", base).toString();
}

/**
 * Crea (sin arrancar) un HubConnection con reconexión automática y token fresco
 * en cada intento (AC4 JWT expirado).
 */
export function createExportJobsConnection(
  options: ExportJobsClientOptions = {},
): HubConnection {
  const getAccessToken = options.getAccessToken ?? (() => getToken());
  return new HubConnectionBuilder()
    .withUrl(resolveHubUrl(options.hubUrl), {
      accessTokenFactory: async () => (await getAccessToken()) ?? "",
    })
    .withAutomaticReconnect([0, 2000, 5000, 10000, 30000])
    .configureLogging(LogLevel.Warning)
    .build();
}

function emitTerminal(handlers: ExportJobsHandlers, event: ExportProgressEvent): void {
  if (event.status === "failed") {
    handlers.onFailed?.(event);
  } else {
    handlers.onCompleted?.(event);
  }
}

/**
 * Suscribe a progreso/finalización de un export job.
 * - Si el hub está Connected: usa SignalR (`Subscribe` + eventos).
 * - Si no: fallback GET `/exports/{id}` cada 5 s (AC3).
 * Devuelve dispose async-safe.
 */
export async function watchExportJob(
  jobId: string,
  handlers: ExportJobsHandlers,
  options: ExportJobsClientOptions = {},
): Promise<() => void> {
  const pollIntervalMs = options.pollIntervalMs ?? DEFAULT_POLL_MS;
  const maxPollAttempts = options.maxPollAttempts ?? DEFAULT_MAX_POLLS;
  let disposed = false;
  let pollTimer: ReturnType<typeof setInterval> | null = null;
  let connection: HubConnection | null = null;

  const stopPoll = () => {
    if (pollTimer) {
      clearInterval(pollTimer);
      pollTimer = null;
    }
  };

  const startPolling = () => {
    stopPoll();
    let attempts = 0;
    pollTimer = setInterval(() => {
      void (async () => {
        if (disposed) return;
        attempts += 1;
        if (attempts > maxPollAttempts) {
          stopPoll();
          return;
        }
        try {
          const job = await getExport(jobId);
          handlers.onProgress?.({
            jobId: job.id,
            status: job.status,
            progressPct: job.progressPct,
          });
          if (job.status === "completed" || job.status === "failed") {
            stopPoll();
            emitTerminal(handlers, {
              jobId: job.id,
              status: job.status,
              progressPct: job.progressPct,
            });
          }
        } catch {
          /* reintento en el siguiente tick */
        }
      })();
    }, pollIntervalMs);
  };

  const wireHub = (hub: HubConnection) => {
    hub.on("ExportProgress", (payload: ExportProgressEvent) => {
      if (disposed || payload?.jobId !== jobId) return;
      handlers.onProgress?.(payload);
    });
    hub.on("ExportCompleted", (payload: ExportProgressEvent) => {
      if (disposed || payload?.jobId !== jobId) return;
      emitTerminal(handlers, payload);
    });
  };

  try {
    connection = createExportJobsConnection(options);
    wireHub(connection);
    if (connection.state === HubConnectionState.Disconnected) {
      await connection.start();
    }
    if (!disposed && connection.state === HubConnectionState.Connected) {
      await connection.invoke("Subscribe", jobId);
    } else if (!disposed) {
      startPolling();
    }

    connection.onreconnected(() => {
      if (disposed || !connection) return;
      stopPoll();
      void connection.invoke("Subscribe", jobId).catch(() => startPolling());
    });

    connection.onclose(() => {
      if (disposed) return;
      startPolling();
    });
  } catch {
    connection = null;
    if (!disposed) startPolling();
  }

  // Si tras start el hub no quedó Connected, activar fallback de inmediato (AC3).
  if (
    !disposed &&
    (!connection || connection.state !== HubConnectionState.Connected) &&
    !pollTimer
  ) {
    startPolling();
  }

  return () => {
    disposed = true;
    stopPoll();
    const hub = connection;
    connection = null;
    if (!hub) return;
    void (async () => {
      try {
        if (hub.state === HubConnectionState.Connected) {
          await hub.invoke("Unsubscribe", jobId);
        }
      } catch {
        /* ignore */
      }
      try {
        await hub.stop();
      } catch {
        /* ignore */
      }
    })();
  };
}

/** Helper de tests: mapea un `ExportJob` REST a evento de progreso. */
export function jobToProgressEvent(job: ExportJob): ExportProgressEvent {
  return {
    jobId: job.id,
    status: job.status,
    progressPct: job.progressPct,
  };
}
