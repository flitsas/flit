"use client";

import { useCallback, useEffect, useState } from "react";
import { Pencil } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { RowActions } from "@/components/atom/RowActions";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Pagination } from "@/components/atom/Pagination";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { useToast } from "@/components/admin/Toast";
import {
  createOtWebhook,
  fetchOtApiLogs,
  fetchOtWebhooks,
  updateOtWebhook,
} from "@/lib/api/admin-ot";
import type { OtApiCallLog, OtWebhook } from "@/lib/api/types-ot";
import { OtSidePanel } from "./OtSidePanel";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { OtTabBar } from "./OtTabBar";
import { OT_FILTER_FORM_CLS, OT_INPUT_CLS } from "./ot-form-styles";
import { maskTargetUrl } from "./ot-utils";
import { WebhookFormPanel } from "./WebhookFormPanel";

import { formatFechaHora } from "@/lib/format/date";
// Bug #13055 — tabla homologada con el modelo de trámites.
const HEAD_STYLE = { background: TABLA_HEADER_BG, color: TABLA_HEADER_FG } as const;
const CELL_STYLE = { borderColor: "#DFE5ED" } as const;
const ROW_CLS = `bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`;

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
  const [logTotal, setLogTotal] = useState(0);
  // Bitácora: paginación de servidor con «Filas por página».
  const { page: logPage, setPage: setLogPage, pageSize: logPageSize, setPageSize: setLogPageSize } =
    usePaginacion();
  // Lista de webhooks: paginación en cliente.
  const pgWebhooks = usePaginacion();
  const [selectedLog, setSelectedLog] = useState<OtApiCallLog | null>(null);

  const [direction, setDirection] = useState("outbound");
  const [httpClass, setHttpClass] = useState<"all" | "5xx">("all");
  const [dateFrom, setDateFrom] = useState("");
  const [dateTo, setDateTo] = useState("");

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

    const loadLogs = useCallback(
    async (signal?: AbortSignal, targetPage = 1) => {
      setLogStatus("loading");
      try {
        const result = await fetchOtApiLogs(
          {
            direction,
            from: dateFrom ? `${dateFrom}T00:00:00.000Z` : undefined,
            to: dateTo ? `${dateTo}T23:59:59.999Z` : undefined,
            minResponseCode: httpClass === "5xx" ? 500 : undefined,
            page: targetPage,
            pageSize: logPageSize,
          },
          signal,
        );
        if (signal?.aborted) return;
        setLogs(result.data);
        setLogTotal(result.totalCount);
        setLogPage(result.page);
        setLogStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setLogStatus("error");
      }
    },
    [direction, httpClass, dateFrom, dateTo, logPageSize, setLogPage],
  );

  useEffect(() => {
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void loadWebhooks(c.signal);
    return () => c.abort();
  }, [loadWebhooks]);

  useEffect(() => {
    if (tab !== "logs") return;
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- recarga bitácora al cambiar filtros/página
    void loadLogs(c.signal, logPage);
    return () => c.abort();
  }, [tab, loadLogs, logPage]);

  const handleSaved = (webhook: OtWebhook, isNew: boolean) => {
    setWebhooks((prev) =>
      isNew ? [webhook, ...prev] : prev.map((w) => (w.id === webhook.id ? webhook : w)),
    );
    setWebhookStatus("ready");
    setFormOpen(false);
    setEditing(null);
    show(isNew ? "Webhook creado." : "Webhook actualizado.", "success");
  };

  const applyLogFilters = () => {
    setLogPage(1);
    void loadLogs(undefined, 1);
  };

  return (
    <div className="space-y-4">
      <OtTabBar
        ariaLabel="Secciones de integración"
        tabs={[
          { id: "webhooks", label: "Webhooks" },
          { id: "logs", label: "Bitácora" },
        ]}
        activeId={tab}
        onChange={(id) => setTab(id as Tab)}
      />

      {tab === "webhooks" && (
        <div role="tabpanel" className="space-y-3 pt-2">
          <div className="flex justify-end">
            <button
              type="button"
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: "#557EFF" }}
              onClick={() => {
                setEditing(null);
                setFormOpen(true);
              }}
            >
              Nuevo webhook
            </button>
          </div>
          {webhookStatus === "loading" ? (
            <CarLoaderModal label="Cargando webhooks…" />
          ) : (
          <UiStateBoundary
            status={webhookStatus}
            emptyMessage="No hay webhooks configurados."
            errorMessage="Error al cargar webhooks."
            onRetry={() => void loadWebhooks()}
          >
            <div className="overflow-x-auto">
            <table
              className="text-xs"
              style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
            >
              <caption className="sr-only">Webhooks configurados</caption>
              <thead>
                <tr>
                  <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={HEAD_STYLE}>
                    Evento
                  </th>
                  <th scope="col" className={TABLA_HEADER_CELL_CLS} style={HEAD_STYLE}>
                    URL destino
                  </th>
                  <th scope="col" className={TABLA_HEADER_CELL_CLS} style={HEAD_STYLE}>
                    Estado
                  </th>
                  <th scope="col" className={TABLA_HEADER_CELL_CLS} style={HEAD_STYLE}>
                    Creado
                  </th>
                  <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={HEAD_STYLE}>
                    Acciones
                  </th>
                </tr>
              </thead>
              <tbody>
                {pgWebhooks.paginar(webhooks).map((w) => (
                  <tr key={w.id} className={ROW_CLS}>
                    <td className="rounded-l-xl border-y border-l px-4 py-3" style={CELL_STYLE}>
                      {w.eventType}
                    </td>
                    <td className="border-y px-4 py-3 font-mono" style={CELL_STYLE}>
                      {maskTargetUrl(w.targetUrl)}
                    </td>
                    <td className="border-y px-4 py-3" style={CELL_STYLE}>
                      <StatusBadge
                        label={w.isActive ? "Activo" : "Inactivo"}
                        tone={w.isActive ? "success" : "danger"}
                      />
                    </td>
                    <td className="border-y px-4 py-3 opacity-70" style={CELL_STYLE}>
                      {formatFechaHora(new Date(w.createdAt))}
                    </td>
                    <td
                      className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                      style={CELL_STYLE}
                    >
                      <RowActions
                        actions={[
                          {
                            icon: Pencil,
                            label: `Editar webhook ${w.eventType}`,
                            onClick: () => {
                              setEditing(w);
                              setFormOpen(true);
                            },
                            tone: "primary",
                          },
                        ]}
                      />
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
            <Pagination
              page={pgWebhooks.page}
              pageSize={pgWebhooks.pageSize}
              totalCount={webhooks.length}
              onPageChange={pgWebhooks.setPage}
              onPageSizeChange={pgWebhooks.setPageSize}
              ariaLabel="Paginación de webhooks"
              noun="webhooks"
            />
          </UiStateBoundary>
          )}
        </div>
      )}

      {tab === "logs" && (
        <div role="tabpanel" className="space-y-3 pt-2">
          <form
            className={OT_FILTER_FORM_CLS}
            onSubmit={(e) => {
              e.preventDefault();
              applyLogFilters();
            }}
            aria-label="Filtros de bitácora"
          >
            <label className="text-xs font-semibold text-foreground">
              Desde
              <input
                type="date"
                className={`mt-1 ${OT_INPUT_CLS}`}
                value={dateFrom}
                onChange={(e) => setDateFrom(e.target.value)}
              />
            </label>
            <label className="text-xs font-semibold text-foreground">
              Hasta
              <input
                type="date"
                className={`mt-1 ${OT_INPUT_CLS}`}
                value={dateTo}
                onChange={(e) => setDateTo(e.target.value)}
              />
            </label>
            <label className="text-xs font-semibold text-foreground">
              Dirección
              <select
                aria-label="Dirección"
                className={`mt-1 ${OT_INPUT_CLS}`}
                value={direction}
                onChange={(e) => setDirection(e.target.value)}
              >
                <option value="outbound">outbound</option>
                <option value="inbound">inbound</option>
              </select>
            </label>
            <label className="text-xs font-semibold text-foreground">
              Código HTTP
              <select
                aria-label="Código HTTP"
                className={`mt-1 ${OT_INPUT_CLS}`}
                value={httpClass}
                onChange={(e) => setHttpClass(e.target.value as "all" | "5xx")}
              >
                <option value="all">Todos</option>
                <option value="5xx">5xx</option>
              </select>
            </label>
            <div className="flex items-end md:col-span-4">
              <button
                type="submit"
                className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
              >
                Aplicar filtros
              </button>
            </div>
          </form>

          {logStatus === "loading" ? (
            <CarLoaderModal label="Cargando bitácora…" />
          ) : (
          <UiStateBoundary
            status={logStatus}
            emptyMessage="Sin registros en el período seleccionado."
            errorMessage="Error al cargar la bitácora."
            onRetry={() => void loadLogs()}
            skeletonRows={5}
          >
            <div className="overflow-x-auto">
            <table
              className="min-w-[720px] text-xs"
              style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
            >
              <caption className="sr-only">Bitácora de llamadas a la API</caption>
              <thead>
                <tr>
                  <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={HEAD_STYLE}>
                    Endpoint
                  </th>
                  <th scope="col" className={TABLA_HEADER_CELL_CLS} style={HEAD_STYLE}>
                    Método
                  </th>
                  <th scope="col" className={TABLA_HEADER_CELL_CLS} style={HEAD_STYLE}>
                    Código
                  </th>
                  <th scope="col" className={TABLA_HEADER_CELL_CLS} style={HEAD_STYLE}>
                    Duración (ms)
                  </th>
                  <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl`} style={HEAD_STYLE}>
                    Fecha
                  </th>
                </tr>
              </thead>
              <tbody>
                {logs.map((log, i) => (
                  <tr
                    key={`${log.calledAt}-${i}`}
                    className={`cursor-pointer ${ROW_CLS}`}
                    onClick={() => setSelectedLog(log)}
                  >
                    <td
                      className="max-w-[220px] truncate rounded-l-xl border-y border-l px-4 py-3"
                      style={CELL_STYLE}
                    >
                      {log.endpoint}
                    </td>
                    <td className="border-y px-4 py-3" style={CELL_STYLE}>
                      {log.httpMethod}
                    </td>
                    <td className="border-y px-4 py-3" style={CELL_STYLE}>
                      {log.responseCode ?? "—"}
                    </td>
                    <td className="border-y px-4 py-3" style={CELL_STYLE}>
                      {log.durationMs ?? "—"}
                    </td>
                    <td
                      className="rounded-r-xl border-y border-r px-4 py-3 opacity-70"
                      style={CELL_STYLE}
                    >
                      {formatFechaHora(new Date(log.calledAt))}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
            </div>
            <Pagination
              page={logPage}
              pageSize={logPageSize}
              totalCount={logTotal}
              onPageChange={setLogPage}
              onPageSizeChange={setLogPageSize}
              ariaLabel="Paginación de la bitácora"
              noun="registros"
            />
          </UiStateBoundary>
          )}
        </div>
      )}

      <OtSidePanel
        open={selectedLog !== null}
        title="Detalle de llamada"
        ariaLabel="Detalle de log"
        onClose={() => setSelectedLog(null)}
      >
        {selectedLog && (
          <>
            <p className="text-xs mb-2">
              <strong>payload_hash:</strong>{" "}
              <code className="break-all">{selectedLog.payloadHash}</code>
            </p>
            <p className="text-[11px] opacity-70">
              Datos protegidos por Ley 1581 de 2012 — el payload completo no se expone.
            </p>
          </>
        )}
      </OtSidePanel>

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
