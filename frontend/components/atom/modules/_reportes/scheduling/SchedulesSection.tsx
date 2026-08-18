"use client";

import { useCallback, useEffect, useState } from "react";
import { CalendarPlus, Pencil, Trash2 } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  createReportSchedule,
  createSuperAdminReportSchedule,
  deleteReportSchedule,
  deleteSuperAdminReportSchedule,
  fetchReportSchedules,
  fetchSuperAdminReportSchedules,
  updateReportSchedule,
  updateSuperAdminReportSchedule,
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
import { ScheduleForm, type SchedulePresetConsulta } from "./ScheduleForm";

interface SchedulesSectionProps {
  tenantId?: string;
  /** SuperAdmin sin compañía elegida en el filtro superior ("Todas las compañías"): en vez de
   * bloquear la vista, se listan por defecto los informes de consulta cross-compañía (alcance
   * "superadmin") — son los únicos que no necesitan una compañía concreta. Elegir una compañía
   * en el filtro vuelve a mostrar los informes de esa compañía. */
  needsCompany?: boolean;
  /** "Programar este informe": abre el formulario de creación ya prellenado con esto. */
  presetConsulta?: SchedulePresetConsulta | null;
  /** Se llama una vez el preset ya abrió el formulario. */
  onConsumePreset?: () => void;
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
export function SchedulesSection({
  tenantId, needsCompany = false, presetConsulta = null, onConsumePreset,
}: SchedulesSectionProps) {
  const [items, setItems] = useState<ReportSchedule[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [editing, setEditing] = useState<ReportSchedule | null>(null);
  const [creating, setCreating] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState<ReportSchedule | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  // Copia LOCAL del preset activo: la prop se limpia (onConsumePreset) apenas se lee, pero el
  // formulario sigue abierto y necesita seguir sabiendo con qué consulta se prellenó.
  const [activePreset, setActivePreset] = useState<SchedulePresetConsulta | null>(null);
  // Alcance SuperAdmin (Reportes 2.0, HU-D, segunda ola): se fija cuando llega un preset con
  // savedQueryScope="superadmin" y, a diferencia de leerlo directo de `presetConsulta` en cada
  // render, NO se limpia junto con el preset — el padre lo limpia (onConsumePreset) en el mismo
  // tick en que se consume, así que sin este estado persistido el POST del submit caía en el
  // endpoint de empresa con tenantId en vez de /report-schedules/superadmin. Combinado con la
  // propia prop (línea de abajo) para que el primer render — antes de que el efecto corra — ya
  // tenga el alcance correcto y no dispare un primer fetch al endpoint equivocado.
  const [scopeLocked, setScopeLocked] = useState(false);
  // Sin compañía elegida (`needsCompany`) y sin preset activo: se listan los informes de consulta
  // cross-compañía por defecto — de otro modo, los que se crearon con "Programar este informe"
  // desde una consulta de SuperAdmin quedaban invisibles para editar/eliminar (no hay ninguna
  // compañía "dueña" de ellos, así que exigir una para verlos los deja huérfanos en la UI).
  const superAdminMode =
    scopeLocked || presetConsulta?.savedQueryScope === "superadmin" || needsCompany;

  // "Programar este informe": el preset abre el formulario de creación solo (nunca reemplaza una
  // edición en curso) y se consume enseguida — sin esto, reabrir el panel en la misma sesión
  // reabriría el formulario aunque la persona ya lo haya cerrado.
  useEffect(() => {
    if (!presetConsulta) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- reacciona a un preset que llega de fuera, no a estado propio
    setEditing(null);
    setCreating(true);
    setActivePreset(presetConsulta);
    setScopeLocked(presetConsulta.savedQueryScope === "superadmin");
    onConsumePreset?.();
  }, [presetConsulta, onConsumePreset]);

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const data = superAdminMode
        ? await fetchSuperAdminReportSchedules()
        : await fetchReportSchedules(tenantId);
      setItems(data.items);
      setStatus(data.items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, [tenantId, superAdminMode]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  async function handleCreate(input: ReportScheduleInput) {
    if (superAdminMode) {
      await createSuperAdminReportSchedule(input);
    } else {
      await createReportSchedule(input, tenantId);
    }
    setCreating(false);
    setActivePreset(null);
    await load();
  }

  async function handleUpdate(input: ReportScheduleInput) {
    if (!editing) return;
    if (superAdminMode) {
      await updateSuperAdminReportSchedule(editing.id, input);
    } else {
      await updateReportSchedule(editing.id, input, tenantId);
    }
    setEditing(null);
    await load();
  }

  async function handleDelete() {
    if (!confirmDelete) return;
    setActionError(null);
    try {
      if (superAdminMode) {
        await deleteSuperAdminReportSchedule(confirmDelete.id);
      } else {
        await deleteReportSchedule(confirmDelete.id, tenantId);
      }
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
          {superAdminMode
            // Se repite aquí (no solo en emptyMessage de abajo) porque esa guía desaparece en
            // cuanto hay al menos un informe listado — sin esto, con la lista llena no quedaba
            // ninguna pista de cómo programar uno más. También explica por qué el botón «Nuevo
            // informe» no está: sin esto, alguien sin el contexto de por qué desapareció no
            // sabría que elegir una compañía en el filtro superior lo hace reaparecer.
            ? "Aquí solo se listan informes de consultas guardadas: para crear uno, ve a «Consultas personalizadas» y haz clic en el ícono de calendario junto a la consulta que quieras programar. Para un informe de resumen, operación, uso u organismo de tránsito de una compañía específica, elígela primero en el filtro «COMPAÑÍA» arriba — ahí reaparece el botón «Nuevo informe»."
            : "Recibe por correo un resumen de indicadores del periodo, en la hora de Bogotá que elijas."}
        </p>
        {/* En alcance SuperAdmin no hay "informe en blanco": todo informe aquí es de tipo
            "consulta" y se crea desde "Programar este informe" en una consulta guardada. */}
        {!formOpen && !superAdminMode && (
          <CreateButton size="sm" label="Nuevo informe" icon={CalendarPlus} onClick={() => setCreating(true)} />
        )}
      </div>

      {creating && (
        <ScheduleForm
          presetConsulta={activePreset ?? undefined}
          onSubmit={handleCreate}
          onCancel={() => {
            setCreating(false);
            setActivePreset(null);
          }}
        />
      )}
      {editing && (
        <ScheduleForm initial={editing} onSubmit={handleUpdate} onCancel={() => setEditing(null)} />
      )}

      {!formOpen && (
        <UiStateBoundary
          status={status}
          emptyMessage={
            superAdminMode
              ? "Aún no hay informes de consultas guardadas. Ve a «Consultas personalizadas» y haz clic en el ícono de calendario junto a la consulta que quieras programar."
              : "Aún no hay informes programados. Crea el primero con «Nuevo informe»."
          }
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
