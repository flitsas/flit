"use client";

import { useCallback, useEffect, useState } from "react";
import { Pencil, Plus, Trash2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  createReportSchedule,
  deleteReportSchedule,
  fetchReportSchedules,
  updateReportSchedule,
  type ReportSchedule,
  type ReportScheduleInput,
} from "@/lib/api/analytics-scheduling";
import {
  DAY_OF_WEEK_LABELS,
  FORMAT_LABELS,
  FREQUENCY_LABELS,
  REPORT_TYPE_LABELS,
  formatDateTime,
} from "./labels";
import { ScheduleForm } from "./ScheduleForm";

interface SchedulesSectionProps {
  tenantId?: string;
}

function describeWhen(s: ReportSchedule): string {
  const hour = `${String(s.sendHour).padStart(2, "0")}:00`;
  if (s.frequency === "weekly" && s.dayOfWeek !== null)
    return `${FREQUENCY_LABELS.weekly} · ${DAY_OF_WEEK_LABELS[s.dayOfWeek]} ${hour}`;
  if (s.frequency === "monthly" && s.dayOfMonth !== null)
    return `${FREQUENCY_LABELS.monthly} · día ${s.dayOfMonth} ${hour}`;
  return `${FREQUENCY_LABELS.daily} · ${hour}`;
}

/**
 * Sección "Informes programados" (Reportes 2.0, HU-D): tabla + formulario crear/editar +
 * eliminación con confirmación. Estados loading/vacío/error con UiStateBoundary.
 */
export function SchedulesSection({ tenantId }: SchedulesSectionProps) {
  const [items, setItems] = useState<ReportSchedule[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [editing, setEditing] = useState<ReportSchedule | null>(null);
  const [creating, setCreating] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState<ReportSchedule | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const data = await fetchReportSchedules(tenantId);
      setItems(data.items);
      setStatus(data.items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, [tenantId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  async function handleCreate(input: ReportScheduleInput) {
    await createReportSchedule(input, tenantId);
    setCreating(false);
    await load();
  }

  async function handleUpdate(input: ReportScheduleInput) {
    if (!editing) return;
    await updateReportSchedule(editing.id, input, tenantId);
    setEditing(null);
    await load();
  }

  async function handleDelete() {
    if (!confirmDelete) return;
    setActionError(null);
    try {
      await deleteReportSchedule(confirmDelete.id, tenantId);
      setConfirmDelete(null);
      await load();
    } catch {
      setActionError("No se pudo eliminar el informe programado. Inténtalo de nuevo.");
    }
  }

  const formOpen = creating || editing !== null;

  return (
    <section data-testid="schedules-section" className="space-y-3">
      <div className="flex items-center justify-between">
        <p className="text-xs text-slate-500 dark:text-slate-400">
          Recibe por correo un resumen de indicadores del periodo, en la hora de Bogotá que elijas.
        </p>
        {!formOpen && (
          <button
            type="button"
            onClick={() => setCreating(true)}
            className="flex items-center gap-1.5 rounded-xl px-3 py-2 text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            <Plus className="h-3.5 w-3.5" aria-hidden="true" /> Nuevo informe
          </button>
        )}
      </div>

      {creating && <ScheduleForm onSubmit={handleCreate} onCancel={() => setCreating(false)} />}
      {editing && (
        <ScheduleForm initial={editing} onSubmit={handleUpdate} onCancel={() => setEditing(null)} />
      )}

      {!formOpen && (
        <UiStateBoundary
          status={status}
          emptyMessage="Aún no hay informes programados. Crea el primero con «Nuevo informe»."
          errorMessage="No se pudieron cargar los informes programados."
          onRetry={() => void load()}
          skeletonRows={3}
        >
          <div className="overflow-x-auto rounded-2xl border border-slate-200 dark:border-slate-700">
            <table className="w-full text-left text-sm">
              <thead>
                <tr className="text-xs uppercase tracking-wide text-slate-500 dark:text-slate-400 border-b border-slate-200 dark:border-slate-700">
                  <th className="px-3 py-2">Nombre</th>
                  <th className="px-3 py-2">Tipo</th>
                  <th className="px-3 py-2">Programación</th>
                  <th className="px-3 py-2">Formato</th>
                  <th className="px-3 py-2">Destinatarios</th>
                  <th className="px-3 py-2">Último envío</th>
                  <th className="px-3 py-2">Estado</th>
                  <th className="px-3 py-2 text-right">Acciones</th>
                </tr>
              </thead>
              <tbody>
                {items.map((s) => (
                  <tr
                    key={s.id}
                    data-testid="schedule-row"
                    className="border-b border-slate-100 dark:border-slate-800 last:border-0 text-[#162744] dark:text-slate-200"
                  >
                    <td className="px-3 py-2 font-medium">{s.name}</td>
                    <td className="px-3 py-2">{REPORT_TYPE_LABELS[s.reportType]}</td>
                    <td className="px-3 py-2 whitespace-nowrap">{describeWhen(s)}</td>
                    <td className="px-3 py-2">{FORMAT_LABELS[s.format]}</td>
                    <td className="px-3 py-2">{s.recipients.length}</td>
                    <td className="px-3 py-2 whitespace-nowrap">{formatDateTime(s.lastSentAt)}</td>
                    <td className="px-3 py-2">
                      <span
                        className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
                        style={
                          s.isActive
                            ? { background: "#557EFF1A", color: "#557EFF" }
                            : { background: "#6b7a941A", color: "#6b7a94" }
                        }
                      >
                        {s.isActive ? "Activo" : "Inactivo"}
                      </span>
                    </td>
                    <td className="px-3 py-2">
                      <div className="flex justify-end gap-1">
                        <button
                          type="button"
                          aria-label={`Editar ${s.name}`}
                          onClick={() => setEditing(s)}
                          className="rounded-lg border border-slate-200 dark:border-slate-700 p-1.5 hover:bg-slate-50 dark:hover:bg-slate-800"
                        >
                          <Pencil className="h-3.5 w-3.5" aria-hidden="true" />
                        </button>
                        <button
                          type="button"
                          aria-label={`Eliminar ${s.name}`}
                          onClick={() => setConfirmDelete(s)}
                          className="rounded-lg border border-slate-200 dark:border-slate-700 p-1.5 text-red-600 hover:bg-red-50 dark:hover:bg-red-950"
                        >
                          <Trash2 className="h-3.5 w-3.5" aria-hidden="true" />
                        </button>
                      </div>
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        </UiStateBoundary>
      )}

      {confirmDelete && (
        <div
          role="alertdialog"
          aria-label="Confirmar eliminación"
          data-testid="schedule-delete-confirm"
          className="rounded-2xl border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 p-4 space-y-2"
        >
          <p className="text-sm text-[#162744] dark:text-slate-100">
            ¿Eliminar el informe programado <strong>{confirmDelete.name}</strong>? Esta acción no se puede deshacer.
          </p>
          {actionError && (
            <p role="alert" className="text-xs font-medium text-red-600 dark:text-red-400">{actionError}</p>
          )}
          <div className="flex gap-2">
            <button
              type="button"
              onClick={handleDelete}
              className="rounded-xl bg-red-600 px-4 py-2 text-xs font-semibold text-white"
            >
              Eliminar
            </button>
            <button
              type="button"
              onClick={() => {
                setConfirmDelete(null);
                setActionError(null);
              }}
              className="rounded-xl border border-slate-200 dark:border-slate-700 px-4 py-2 text-xs font-semibold text-[#162744] dark:text-slate-200"
            >
              Cancelar
            </button>
          </div>
        </div>
      )}
    </section>
  );
}
