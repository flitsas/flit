"use client";

import { useCallback, useEffect, useState } from "react";
import { Ban, Building2, CalendarDays, FileText, RotateCcw } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { ApiError } from "@/lib/api/types";
import {
  cancelQuipuxSubmission,
  canCancelQuipuxSubmission,
  canRetryQuipuxSubmission,
  fetchQuipuxCola,
  retryQuipuxSubmission,
  type QuipuxColaItem,
  type QuipuxSubmissionStatus,
} from "@/lib/api/admin-transit-office-tenants";
import { OtStatusBadge, type OtStatusBadgeProps } from "./OtStatusBadge";
import { formatOtDate } from "./ot-utils";

const PAGE_SIZE = 20;

type Tone = NonNullable<OtStatusBadgeProps["tone"]>;

// Estado de la radicación → tono del badge y etiqueta legible. `pendiente` en neutro (aún en cola),
// `registrado` en azul (en vuelo), `aprobado` en verde, `rechazado`/`fallido` en naranja (desenlace
// que suele requerir acción).
const STATUS_META: Record<QuipuxSubmissionStatus, { tone: Tone; label: string }> = {
  pendiente: { tone: "neutral", label: "Pendiente" },
  registrado: { tone: "warning", label: "Registrado" },
  aprobado: { tone: "success", label: "Aprobado" },
  rechazado: { tone: "danger", label: "Rechazado" },
  fallido: { tone: "danger", label: "Fallido" },
};

interface ConfirmTarget {
  item: QuipuxColaItem;
  action: "retry" | "cancel";
}

export interface QuipuxQueueListProps {
  transitOfficeId: string;
}

/**
 * Cola Quipux de una secretaría destino (HU #10774): las radicaciones reales de
 * `tramites.quipux_submissions`, con su estado en el ciclo de Quipux y las acciones de operación
 * del SuperAdmin (re-encolar un `fallido`, cancelar un `pendiente`). Reemplaza la vista-fachada que
 * repintaba la bandeja del dashboard.
 */
export function QuipuxQueueList({ transitOfficeId }: QuipuxQueueListProps) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<QuipuxColaItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  const [actingId, setActingId] = useState<string | null>(null);
  const [confirmTarget, setConfirmTarget] = useState<ConfirmTarget | null>(null);

  const load = useCallback(
    async (targetPage: number, signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchQuipuxCola(
          transitOfficeId,
          { page: targetPage, pageSize: PAGE_SIZE },
          signal,
        );
        if (signal?.aborted) {
          return;
        }
        setItems(result.data);
        setTotalCount(result.totalCount);
        setPage(result.page);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(1, controller.signal);
    return () => controller.abort();
  }, [load]);

  const confirmAction = async () => {
    if (!confirmTarget) {
      return;
    }
    const { item, action } = confirmTarget;
    setActingId(item.id);
    try {
      if (action === "retry") {
        await retryQuipuxSubmission(transitOfficeId, item.id);
        show("Radicación re-encolada.", "success");
      } else {
        await cancelQuipuxSubmission(transitOfficeId, item.id);
        show("Radicación cancelada.", "success");
      }
      setConfirmTarget(null);
      await load(page);
    } catch (error) {
      show(errorMessageFor(action, error), "error");
    } finally {
      setActingId(null);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));

  return (
    <>
      <UiStateBoundary
        status={status}
        emptyMessage="La cola Quipux de esta secretaría no tiene radicaciones."
        errorMessage="Error al consultar la cola QX."
        onRetry={() => void load(page)}
        skeletonRows={3}
      >
        <ul className="space-y-3" aria-label="Cola de radicaciones Quipux">
          {items.map((item) => {
            const meta = STATUS_META[item.status];
            const acting = actingId === item.id;
            return (
              <li
                key={item.id}
                className="group flex flex-col gap-4 rounded-2xl border bg-card p-4 sm:flex-row sm:items-center"
              >
                <div
                  className="flex h-11 w-11 shrink-0 items-center justify-center rounded-xl bg-[#557EFF]/10"
                  style={{ color: "#557EFF" }}
                  aria-hidden="true"
                >
                  <FileText className="h-5 w-5" />
                </div>

                <div className="min-w-0 flex-1">
                  <div className="flex flex-wrap items-center gap-x-2 gap-y-1">
                    <p className="truncate text-sm font-semibold text-foreground">
                      {item.procedureTypeName}
                    </p>
                    <OtStatusBadge label={meta.label} tone={meta.tone} />
                    {item.attempts > 0 && (
                      <span className="text-[10px] font-medium opacity-60">
                        {item.attempts} intento{item.attempts === 1 ? "" : "s"}
                      </span>
                    )}
                  </div>
                  <dl className="mt-1.5 flex flex-wrap items-center gap-x-4 gap-y-1 text-[11px] text-foreground">
                    <div className="inline-flex items-center gap-1">
                      <dt className="sr-only">Radicado</dt>
                      <dd
                        className="rounded-md px-1.5 py-0.5 font-mono font-medium tracking-tight bg-muted"
                        style={{ color: "#557EFF" }}
                      >
                        {item.referenceNumber}
                      </dd>
                    </div>
                    <div className="inline-flex items-center gap-1 opacity-70">
                      <Building2 className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      <dt className="sr-only">Empresa cliente</dt>
                      <dd className="truncate">{item.clientTenantName}</dd>
                    </div>
                    <div className="inline-flex items-center gap-1 opacity-70">
                      <CalendarDays className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                      <dt className="sr-only">Fecha de encolado</dt>
                      <dd>{formatOtDate(item.createdAt)}</dd>
                    </div>
                  </dl>
                  <p className="mt-1 truncate font-mono text-[10px] opacity-50" title={item.documentName}>
                    {item.documentName}
                  </p>
                  {item.rejectionReason && (
                    <p className="mt-1 text-[11px]" style={{ color: "#FF4E00" }}>
                      {item.rejectionReason}
                    </p>
                  )}
                </div>

                {(canRetryQuipuxSubmission(item) || canCancelQuipuxSubmission(item)) && (
                  <div className="flex shrink-0 flex-wrap gap-2 border-t pt-3 sm:border-t-0 sm:pt-0">
                    {canRetryQuipuxSubmission(item) && (
                      <button
                        type="button"
                        className="inline-flex items-center gap-1.5 rounded-xl px-3.5 py-2 text-xs font-semibold text-white shadow-sm transition-colors hover:brightness-95 disabled:opacity-50"
                        style={{ background: "#557EFF" }}
                        disabled={acting}
                        onClick={() => setConfirmTarget({ item, action: "retry" })}
                      >
                        <RotateCcw className="h-4 w-4" aria-hidden="true" />
                        Re-encolar
                      </button>
                    )}
                    {canCancelQuipuxSubmission(item) && (
                      <button
                        type="button"
                        className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-2 text-xs font-semibold transition-colors hover:bg-[#FFF4EC] disabled:opacity-50"
                        style={{ borderColor: "#FFD9C7", color: "#FF4E00" }}
                        disabled={acting}
                        onClick={() => setConfirmTarget({ item, action: "cancel" })}
                      >
                        <Ban className="h-4 w-4" aria-hidden="true" />
                        Cancelar
                      </button>
                    )}
                  </div>
                )}
              </li>
            );
          })}
        </ul>

        {totalPages > 1 && (
          <nav className="mt-4 flex items-center justify-between text-xs" aria-label="Paginación de la cola">
            <button
              type="button"
              className="rounded-lg border px-3 py-1.5 font-semibold disabled:opacity-40"
              disabled={page <= 1 || status === "loading"}
              onClick={() => void load(page - 1)}
            >
              Anterior
            </button>
            <span className="opacity-70">
              Página {page} de {totalPages} · {totalCount} radicaciones
            </span>
            <button
              type="button"
              className="rounded-lg border px-3 py-1.5 font-semibold disabled:opacity-40"
              disabled={page >= totalPages || status === "loading"}
              onClick={() => void load(page + 1)}
            >
              Siguiente
            </button>
          </nav>
        )}
      </UiStateBoundary>

      {confirmTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label={confirmTarget.action === "retry" ? "Confirmar re-encolado" : "Confirmar cancelación"}
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl border bg-card p-6 shadow-2xl">
            <h2 className="text-lg font-semibold text-foreground">
              {confirmTarget.action === "retry"
                ? "¿Re-encolar esta radicación?"
                : "¿Cancelar esta radicación?"}
            </h2>
            <p className="mt-2 text-sm opacity-80">
              {confirmTarget.action === "retry"
                ? "Volverá a la cola con intentos frescos y el registrador la radicará de nuevo."
                : "Quedará como fallida y no se radicará en Quipux."}
            </p>
            <p className="mt-2 font-mono text-xs opacity-70">{confirmTarget.item.referenceNumber}</p>
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setConfirmTarget(null)}
                disabled={actingId !== null}
              >
                Volver
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: confirmTarget.action === "retry" ? "#557EFF" : "#FF4E00" }}
                disabled={actingId !== null}
                onClick={() => void confirmAction()}
              >
                {actingId !== null ? "Procesando…" : "Confirmar"}
              </button>
            </div>
          </div>
        </div>
      )}
    </>
  );
}

function errorMessageFor(action: "retry" | "cancel", error: unknown): string {
  const code =
    error instanceof ApiError && isCodeBody(error.body) ? error.body.code : undefined;
  if (code === "QUIPUX_SUBMISSION_CONFLICT") {
    return "El trámite ya tiene otra radicación en curso.";
  }
  if (code === "QUIPUX_SUBMISSION_INVALID_STATE") {
    return "La radicación cambió de estado; refresca la cola.";
  }
  if (code === "QUIPUX_SUBMISSION_NOT_FOUND") {
    return "La radicación ya no existe.";
  }
  return action === "retry"
    ? "No se pudo re-encolar la radicación."
    : "No se pudo cancelar la radicación.";
}

function isCodeBody(body: unknown): body is { code: string } {
  return typeof body === "object" && body !== null && typeof (body as { code?: unknown }).code === "string";
}
