"use client";

import { useCallback, useEffect, useState } from "react";
import { Check, RefreshCw } from "lucide-react";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  ackAiAlert,
  listAiAlerts,
  type AiFailoverAlertView,
} from "@/lib/api/admin-plataforma-ai";
import { formatFechaHora } from "@/lib/format/date";
import { etiquetaHerramienta, etiquetaProveedor } from "./labels";

type Estado = "loading" | "error" | "empty" | "ready";

/**
 * HU #13512 AC3 — alertas in-app de failover + ack.
 * Si el endpoint aún no existe (404), se muestra vacío sin romper la pantalla.
 */
export function AiAlertsSection({ onAcked }: { onAcked?: () => void }) {
  const [estado, setEstado] = useState<Estado>("loading");
  const [alertas, setAlertas] = useState<AiFailoverAlertView[]>([]);
  const [ackId, setAckId] = useState<string | null>(null);
  const [errorAck, setErrorAck] = useState<string | null>(null);
  const [recarga, setRecarga] = useState(0);

  useEffect(() => {
    const controller = new AbortController();
    setEstado("loading");
    listAiAlerts(controller.signal)
      .then((items) => {
        if (controller.signal.aborted) return;
        const pendientes = items.filter((a) => !a.acked);
        setAlertas(pendientes.length ? pendientes : items);
        setEstado(items.length === 0 ? "empty" : "ready");
      })
      .catch((err: unknown) => {
        if (controller.signal.aborted || (err instanceof DOMException && err.name === "AbortError")) return;
        // Endpoint aún no desplegado: degradar a vacío (backend paralelo).
        const status = err && typeof err === "object" && "status" in err ? Number((err as { status: number }).status) : 0;
        if (status === 404 || status === 501) {
          setAlertas([]);
          setEstado("empty");
          return;
        }
        setEstado("error");
      });
    return () => controller.abort();
  }, [recarga]);

  const reconocer = useCallback(
    async (alerta: AiFailoverAlertView) => {
      setAckId(alerta.id);
      setErrorAck(null);
      try {
        await ackAiAlert(alerta.id);
        setAlertas((prev) => prev.filter((a) => a.id !== alerta.id));
        onAcked?.();
      } catch {
        setErrorAck("No se pudo marcar la alerta como leída. Inténtalo de nuevo.");
      } finally {
        setAckId(null);
      }
    },
    [onAcked],
  );

  return (
    <section aria-labelledby="ai-alerts-heading" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h2 id="ai-alerts-heading" className="text-sm font-bold text-[#162744] dark:text-white">
          Alertas de failover
        </h2>
        <button
          type="button"
          onClick={() => setRecarga((n) => n + 1)}
          className="inline-flex items-center gap-1.5 rounded-md text-xs font-semibold text-[#557EFF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
        >
          <RefreshCw className="h-3.5 w-3.5" aria-hidden="true" />
          Actualizar
        </button>
      </div>

      {errorAck ? (
        <InlineAlert tone="error" compact>
          {errorAck}
        </InlineAlert>
      ) : null}

      {estado === "loading" ? (
        <p className="text-xs text-[#5A6A85]" role="status">
          Cargando alertas…
        </p>
      ) : null}

      {estado === "error" ? (
        <InlineAlert
          tone="error"
          title="No se pudieron cargar las alertas"
          action={
            <button
              type="button"
              onClick={() => setRecarga((n) => n + 1)}
              className="text-xs font-semibold underline"
            >
              Reintentar
            </button>
          }
        >
          Comprueba la conexión o que el servicio de alertas esté disponible en este ambiente.
        </InlineAlert>
      ) : null}

      {estado === "empty" ? (
        <p className="rounded-xl border border-dashed border-[#DFE5ED] px-3 py-4 text-xs text-[#5A6A85] dark:border-white/10">
          No hay alertas de failover pendientes.
        </p>
      ) : null}

      {estado === "ready" ? (
        <ul className="space-y-2" aria-label="Alertas de failover de IA">
          {alertas.map((a) => (
            <li
              key={a.id}
              className="flex flex-wrap items-start justify-between gap-3 rounded-xl border border-[#DFE5ED] bg-white/80 px-3 py-2.5 dark:border-white/10 dark:bg-[#0B0F14]/60"
            >
              <div className="min-w-0 space-y-1">
                <div className="flex flex-wrap items-center gap-2">
                  <StatusBadge
                    tone={a.acked ? "neutral" : "warning"}
                    label={a.acked ? "Leída" : "Pendiente"}
                  />
                  <span className="text-xs font-semibold text-[#162744] dark:text-white">
                    {etiquetaHerramienta(a.toolCode)}
                  </span>
                </div>
                <p className="text-xs text-[#5A6A85]">
                  {etiquetaProveedor(a.fromProvider)} → {etiquetaProveedor(a.toProvider)} ·{" "}
                  {formatFechaHora(a.occurredAt)}
                </p>
              </div>
              {!a.acked ? (
                <button
                  type="button"
                  onClick={() => void reconocer(a)}
                  disabled={ackId === a.id}
                  className="inline-flex items-center gap-1.5 rounded-xl border border-[#DFE5ED] px-3 py-1.5 text-xs font-semibold text-[#162744] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-60 dark:border-white/10 dark:text-white"
                >
                  <Check className="h-3.5 w-3.5" aria-hidden="true" />
                  {ackId === a.id ? "Marcando…" : "Marcar leída"}
                </button>
              ) : null}
            </li>
          ))}
        </ul>
      ) : null}
    </section>
  );
}
