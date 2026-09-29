"use client";

import { useCallback, useEffect, useState } from "react";
import { BellPlus, History, Pencil, Trash2 } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import {
  CARDLIST_CELL,
  CARDLIST_HEAD_ROW,
  CARDLIST_ROW,
  CARDLIST_SCROLL,
  CARDLIST_TABLE,
  CARDLIST_TH,
} from "@/components/atom/table-cardlist";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { CompanyNotice } from "../CompanyNotice";
import { cn } from "@/lib/utils";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import {
  createAlertRule,
  deleteAlertRule,
  fetchAlertRules,
  updateAlertRule,
  type AlertMetric,
  type AlertRule,
  type AlertRuleInput,
} from "@/lib/api/analytics-scheduling";
import {
  createOtAlertRule,
  deleteOtAlertRule,
  fetchOtAlertRules,
  updateOtAlertRule,
} from "@/lib/api/ot-scheduling";
import { METRIC_LABELS, OPERATOR_LABELS, formatDateTime } from "./labels";
import { AlertRuleForm } from "./AlertRuleForm";
import { AlertEventsHistory } from "./AlertEventsHistory";

interface AlertsSectionProps {
  tenantId?: string;
  /** SuperAdmin sin compañía elegida en el filtro superior: en vez de llamar a la API (que exige
   * tenant concreto y respondería 400), se muestra un aviso — mismo patrón que SchedulesSection. */
  needsCompany?: boolean;
  /** Alcance Organismo de Tránsito (Reportes 2.0, HU-D, tercera ola): cuando se indica, la
   * sección gestiona las alertas propias de ESE organismo (endpoint /admin/ot/alert-rules) en
   * vez de las de una compañía — mutuamente excluyente con `tenantId`/`needsCompany`. */
  otTransitOfficeId?: string;
}

const OT_METRICS: AlertMetric[] = ["ot_rejection_rate_pct", "ot_stuck_count"];

type SubView = "rules" | "history";

/**
 * Sección "Alertas" (Reportes 2.0, HU-D): tabla de reglas + formulario crear/editar +
 * eliminación con confirmación y sub-vista "Historial de disparos" (alert-events paginado).
 */
export function AlertsSection({ tenantId, needsCompany = false, otTransitOfficeId }: AlertsSectionProps) {
  const [items, setItems] = useState<AlertRule[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [subView, setSubView] = useState<SubView>("rules");
  const [editing, setEditing] = useState<AlertRule | null>(null);
  const [creating, setCreating] = useState(false);
  const [confirmDelete, setConfirmDelete] = useState<AlertRule | null>(null);
  const [actionError, setActionError] = useState<string | null>(null);
  // Bug #13055 — las reglas pueden crecer: paginación en cliente con «Filas por página».
  const pg = usePaginacion();

  const load = useCallback(async () => {
    if (needsCompany) return;
    setStatus("loading");
    try {
      const data = otTransitOfficeId
        ? await fetchOtAlertRules(otTransitOfficeId)
        : await fetchAlertRules(tenantId);
      setItems(data.items);
      setStatus(data.items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, [tenantId, needsCompany, otTransitOfficeId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  async function handleCreate(input: AlertRuleInput) {
    if (otTransitOfficeId) {
      await createOtAlertRule(input, otTransitOfficeId);
    } else {
      await createAlertRule(input, tenantId);
    }
    setCreating(false);
    await load();
  }

  async function handleUpdate(input: AlertRuleInput) {
    if (!editing) return;
    if (otTransitOfficeId) {
      await updateOtAlertRule(editing.id, input, otTransitOfficeId);
    } else {
      await updateAlertRule(editing.id, input, tenantId);
    }
    setEditing(null);
    await load();
  }

  async function handleDelete() {
    if (!confirmDelete) return;
    setActionError(null);
    try {
      if (otTransitOfficeId) {
        await deleteOtAlertRule(confirmDelete.id, otTransitOfficeId);
      } else {
        await deleteAlertRule(confirmDelete.id, tenantId);
      }
      setConfirmDelete(null);
      await load();
    } catch {
      setActionError("No se pudo eliminar la regla de alerta. Inténtalo de nuevo.");
    }
  }

  const formOpen = creating || editing !== null;
  const subTabClass = (active: boolean) =>
    cn(
      "rounded-lg px-3 py-1.5 text-xs font-semibold",
      active
        ? "text-white"
        : "text-[#162744] dark:text-slate-200 border border-slate-200 dark:border-slate-700",
    );

  return (
    <section data-testid="alerts-section" className="space-y-3">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <div className="flex items-center gap-1.5">
          <button
            type="button"
            onClick={() => setSubView("rules")}
            className={subTabClass(subView === "rules")}
            style={subView === "rules" ? { background: "#557EFF" } : undefined}
          >
            Reglas
          </button>
          <button
            type="button"
            onClick={() => setSubView("history")}
            className={cn(subTabClass(subView === "history"), "flex items-center gap-1")}
            style={subView === "history" ? { background: "#557EFF" } : undefined}
          >
            <History className="h-3.5 w-3.5" aria-hidden="true" /> Historial de disparos
          </button>
        </div>
        {subView === "rules" && !formOpen && !needsCompany && (
          <CreateButton size="sm" label="Nueva alerta" icon={BellPlus} onClick={() => setCreating(true)} />
        )}
      </div>

      {subView === "history" ? (
        <AlertEventsHistory rules={items} tenantId={tenantId} otTransitOfficeId={otTransitOfficeId} />
      ) : needsCompany ? (
        <CompanyNotice message="Como SuperAdmin debes elegir una compañía en el filtro superior para ver o crear sus alertas." />
      ) : (
        <>
          {creating && (
            <AlertRuleForm
              allowedMetrics={otTransitOfficeId ? OT_METRICS : undefined}
              onSubmit={handleCreate}
              onCancel={() => setCreating(false)}
            />
          )}
          {editing && (
            <AlertRuleForm
              initial={editing}
              allowedMetrics={otTransitOfficeId ? OT_METRICS : undefined}
              onSubmit={handleUpdate}
              onCancel={() => setEditing(null)}
            />
          )}

          {!formOpen && (
            <UiStateBoundary
              status={status}
              emptyMessage="Aún no hay alertas configuradas. Crea la primera con «Nueva alerta»."
              errorMessage="No se pudieron cargar las reglas de alerta."
              onRetry={() => void load()}
              skeletonRows={3}
            >
              {/* Bug #13055 — tabla homologada con el modelo de trámites (sin tarjeta envolvente). */}
              <div className={CARDLIST_SCROLL}>
                <table className={CARDLIST_TABLE} aria-label="Reglas de alerta">
                  <thead>
                    <tr className={CARDLIST_HEAD_ROW}>
                      <th scope="col" className={CARDLIST_TH}>Nombre</th>
                      <th scope="col" className={CARDLIST_TH}>Métrica</th>
                      <th scope="col" className={CARDLIST_TH}>Condición</th>
                      <th scope="col" className={CARDLIST_TH}>Ventana</th>
                      <th scope="col" className={CARDLIST_TH}>Cooldown</th>
                      <th scope="col" className={CARDLIST_TH}>Último disparo</th>
                      <th scope="col" className={CARDLIST_TH}>Estado</th>
                      <th scope="col" className={`${CARDLIST_TH} text-right`}>Acciones</th>
                    </tr>
                  </thead>
                  <tbody>
                    {pg.paginar(items).map((r) => (
                      <tr key={r.id} data-testid="alert-rule-row" className={CARDLIST_ROW}>
                        <td className={`${CARDLIST_CELL} font-medium`}>{r.name}</td>
                        <td className={CARDLIST_CELL}>{METRIC_LABELS[r.metric]}</td>
                        <td className={`${CARDLIST_CELL} whitespace-nowrap`}>
                          {OPERATOR_LABELS[r.operator]} {r.threshold}
                        </td>
                        <td className={`${CARDLIST_CELL} whitespace-nowrap`}>{r.windowMinutes} min</td>
                        <td className={`${CARDLIST_CELL} whitespace-nowrap`} data-testid="alert-cooldown-cell">
                          {r.cooldownMinutes} min
                        </td>
                        <td className={`${CARDLIST_CELL} whitespace-nowrap`}>{formatDateTime(r.lastTriggeredAt)}</td>
                        <td className={CARDLIST_CELL}>
                          <span
                            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
                            style={
                              r.isActive
                                ? { background: "#557EFF1A", color: "#557EFF" }
                                : { background: "#6b7a941A", color: "#6b7a94" }
                            }
                          >
                            {r.isActive ? "Activa" : "Inactiva"}
                          </span>
                        </td>
                        <td className={CARDLIST_CELL}>
                          <RowActions
                            actions={[
                              { icon: Pencil, label: `Editar ${r.name}`, tone: "primary", onClick: () => setEditing(r) },
                              { icon: Trash2, label: `Eliminar ${r.name}`, tone: "danger", onClick: () => setConfirmDelete(r) },
                            ]}
                          />
                        </td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
              <Pagination
                page={Math.min(pg.page, Math.max(1, Math.ceil(items.length / pg.pageSize)))}
                pageSize={pg.pageSize}
                totalCount={items.length}
                onPageChange={pg.setPage}
                onPageSizeChange={pg.setPageSize}
                ariaLabel="Paginación de reglas de alerta"
                noun="alertas"
              />
            </UiStateBoundary>
          )}

          {confirmDelete && (
            <div
              role="alertdialog"
              aria-label="Confirmar eliminación"
              data-testid="alert-delete-confirm"
              className="rounded-2xl border border-red-200 dark:border-red-900 bg-red-50 dark:bg-red-950/40 p-4 space-y-2"
            >
              <p className="text-sm text-[#162744] dark:text-slate-100">
                ¿Eliminar la alerta <strong>{confirmDelete.name}</strong>? Su historial de disparos se conserva.
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
        </>
      )}
    </section>
  );
}
