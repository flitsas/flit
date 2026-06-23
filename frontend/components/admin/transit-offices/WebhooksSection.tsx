"use client";

import { useCallback, useEffect, useState } from "react";
import { Plus } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import {
  createOtWebhook,
  fetchOtApiLogs,
  fetchOtWebhooks,
  updateOtWebhook,
} from "@/lib/api/admin-ot";
import type { OtApiCallLog, OtWebhook } from "@/lib/api/types-ot";
import { maskTargetUrl } from "./ot-utils";
import { WebhookFormPanel } from "./WebhookFormPanel";

type Tab = "webhooks" | "logs";

/** Gestión webhooks + bitácora API OT (HU #10219). */
export function WebhooksSection() {
  const { show } = useToast();
  const [tab, setTab] = useState<Tab>("webhooks");
  const [webhookStatus, setWebhookStatus] = useState<UiStatus>("loading");
  const [webhooks, setWebhooks] = useState<OtWebhook[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editing, setEditing] = useState<OtWebhook | null>(null);

  const [logStatus, setLogStatus] = useState<UiStatus>("loading");
  const [logs, setLogs] = useState<OtApiCallLog[]>([]);
  const [selectedLog, setSelectedLog] = useState<OtApiCallLog | null>(null);
  const [direction, setDirection] = useState("outbound");
  const [httpClass, setHttpClass] = useState<"all" | "5xx">("all");

  const loadWebhooks = useCallback(async (signal?: AbortSignal) => {
    setWebhookStatus("loading");
    try {
      const result = await fetchOtWebhooks(signal);
      if (signal?.aborted) return;
      setWebhooks(result.data);
      setWebhookStatus(result.data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setWebhookStatus("error");
    }
  }, []);

  const loadLogs = useCallback(async (signal?: AbortSignal) => {
    setLogStatus("loading");
    try {
      const result = await fetchOtApiLogs({ direction, page: 1, pageSize: 50 }, signal);
      if (signal?.aborted) return;
      let rows = result.data;
      if (httpClass === "5xx") {
        rows = rows.filter((l) => (l.responseCode ?? 0) >= 500);
      }
      setLogs(rows);
      setLogStatus(rows.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setLogStatus("error");
    }
  }, [direction, httpClass]);

  useEffect(() => {
    const c = new AbortController();
    void loadWebhooks(c.signal);
    return () => c.abort();
  }, [loadWebhooks]);

  useEffect(() => {
    if (tab !== "logs") return;
    const c = new AbortController();
    void loadLogs(c.signal);
    return () => c.abort();
  }, [tab, loadLogs]);

  const handleSaved = (webhook: OtWebhook, isNew: boolean) => {
    setWebhooks((prev) =>
      isNew ? [webhook, ...prev] : prev.map((w) => (w.id === webhook.id ? webhook : w)),
    );
    setWebhookStatus("ready");
    setFormOpen(false);
    setEditing(null);
    show(isNew ? "Webhook creado." : "Webhook actualizado.", "success");
  };

  return (
    <div className="space-y-4">
      <div role="tablist" aria-label="Secciones de integración" className="flex gap-2">
        <button
          type="button"
          role="tab"
          aria-selected={tab === "webhooks"}
          className="rounded-xl px-4 py-2 text-xs font-semibold"
          style={{
            background: tab === "webhooks" ? "#557EFF" : "#F4F7FC",
            color: tab === "webhooks" ? "#FFF" : "#162744",
          }}
          onClick={() => setTab("webhooks")}
        >
          Webhooks
        </button>
        <button
          type="button"
          role="tab"
          aria-selected={tab === "logs"}
          className="rounded-xl px-4 py-2 text-xs font-semibold"
          style={{
            background: tab === "logs" ? "#557EFF" : "#F4F7FC",
            color: tab === "logs" ? "#FFF" : "#162744",
          }}
          onClick={() => setTab("logs")}
        >
          Bitácora
        </button>
      </div>

      {tab === "webhooks" && (
        <div role="tabpanel">
          <div className="mb-3 flex justify-end">
            <button
              type="button"
              className="flex items-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: "#557EFF" }}
              onClick={() => {
                setEditing(null);
                setFormOpen(true);
              }}
            >
              <Plus className="h-3.5 w-3.5" /> Nuevo webhook
            </button>
          </div>
          <UiStateBoundary
            status={webhookStatus}
            emptyMessage="No hay webhooks configurados."
            errorMessage="Error al cargar webhooks."
            onRetry={() => void loadWebhooks()}
          >
            <div className="overflow-x-auto rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
              <table className="w-full text-left text-xs">
                <thead>
                  <tr className="border-b" style={{ borderColor: "#DFE5ED" }}>
                    <th className="px-3 py-2 font-semibold">Evento</th>
                    <th className="px-3 py-2 font-semibold">URL destino</th>
                    <th className="px-3 py-2 font-semibold">Estado</th>
                    <th className="px-3 py-2 font-semibold">Creado</th>
                    <th className="px-3 py-2 font-semibold" />
                  </tr>
                </thead>
                <tbody>
                  {webhooks.map((w) => (
                    <tr key={w.id} className="border-b" style={{ borderColor: "#DFE5ED" }}>
                      <td className="px-3 py-2">{w.eventType}</td>
                      <td className="px-3 py-2 font-mono">{maskTargetUrl(w.targetUrl)}</td>
                      <td className="px-3 py-2">{w.isActive ? "Activo" : "Inactivo"}</td>
                      <td className="px-3 py-2">{new Date(w.createdAt).toLocaleString()}</td>
                      <td className="px-3 py-2">
                        <button
                          type="button"
                          className="text-[#557EFF] font-semibold"
                          onClick={() => {
                            setEditing(w);
                            setFormOpen(true);
                          }}
                        >
                          Editar
                        </button>
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </UiStateBoundary>
        </div>
      )}

      {tab === "logs" && (
        <div role="tabpanel" className="space-y-3">
          <div className="flex flex-wrap gap-2">
            <select
              aria-label="Dirección"
              className="rounded-lg border px-2 py-1 text-xs"
              style={{ borderColor: "#DFE5ED" }}
              value={direction}
              onChange={(e) => setDirection(e.target.value)}
            >
              <option value="outbound">outbound</option>
              <option value="inbound">inbound</option>
            </select>
            <select
              aria-label="Código HTTP"
              className="rounded-lg border px-2 py-1 text-xs"
              style={{ borderColor: "#DFE5ED" }}
              value={httpClass}
              onChange={(e) => setHttpClass(e.target.value as "all" | "5xx")}
            >
              <option value="all">Todos</option>
              <option value="5xx">5xx</option>
            </select>
            <button
              type="button"
              className="rounded-lg px-3 py-1 text-xs font-semibold text-white"
              style={{ background: "#557EFF" }}
              onClick={() => void loadLogs()}
            >
              Aplicar filtros
            </button>
          </div>
          <UiStateBoundary
            status={logStatus}
            emptyMessage="Sin registros en el período seleccionado."
            errorMessage="Error al cargar la bitácora."
            onRetry={() => void loadLogs()}
            skeletonRows={5}
          >
            <div className="overflow-x-auto rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
              <table className="w-full text-left text-xs">
                <thead>
                  <tr className="border-b" style={{ borderColor: "#DFE5ED" }}>
                    <th className="px-3 py-2">Endpoint</th>
                    <th className="px-3 py-2">Método</th>
                    <th className="px-3 py-2">Código</th>
                    <th className="px-3 py-2">Duración (ms)</th>
                    <th className="px-3 py-2">Fecha</th>
                  </tr>
                </thead>
                <tbody>
                  {logs.map((log, i) => (
                    <tr
                      key={`${log.calledAt}-${i}`}
                      className="cursor-pointer border-b hover:bg-[#F4F7FC]"
                      style={{ borderColor: "#DFE5ED" }}
                      onClick={() => setSelectedLog(log)}
                    >
                      <td className="px-3 py-2 max-w-[200px] truncate">{log.endpoint}</td>
                      <td className="px-3 py-2">{log.httpMethod}</td>
                      <td className="px-3 py-2">{log.responseCode ?? "—"}</td>
                      <td className="px-3 py-2">{log.durationMs ?? "—"}</td>
                      <td className="px-3 py-2">{new Date(log.calledAt).toLocaleString()}</td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          </UiStateBoundary>
        </div>
      )}

      {selectedLog && (
        <aside
          className="fixed inset-y-0 right-0 z-50 w-full max-w-md border-l bg-white p-6 shadow-xl"
          style={{ borderColor: "#DFE5ED" }}
          role="dialog"
          aria-label="Detalle de log"
        >
          <button type="button" className="mb-4 text-xs font-semibold text-[#557EFF]" onClick={() => setSelectedLog(null)}>
            Cerrar
          </button>
          <h3 className="text-sm font-bold mb-2">Detalle de llamada</h3>
          <p className="text-xs mb-2">
            <strong>payload_hash:</strong>{" "}
            <code className="break-all">{selectedLog.payloadHash}</code>
          </p>
          <p className="text-[11px] opacity-70">
            Datos protegidos por Ley 1581 de 2012 — el payload completo no se expone.
          </p>
        </aside>
      )}

      <WebhookFormPanel
        open={formOpen}
        editing={editing}
        onClose={() => {
          setFormOpen(false);
          setEditing(null);
        }}
        onCreate={createOtWebhook}
        onUpdate={updateOtWebhook}
        onSaved={handleSaved}
      />
    </div>
  );
}
