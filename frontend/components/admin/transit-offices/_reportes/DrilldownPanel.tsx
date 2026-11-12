"use client";

// Drill-down: de qué está hecho cada número del panel operativo.
//
// Ningún número del reporte puede ser un callejón sin salida: quien lee «3 con más de 7 días»
// necesita saber cuáles son para ir a resolverlos. El backend los recalcula con los MISMOS
// predicados de la tarjeta, así que la lista nunca contradice al número que la abrió.

import { buttonClass } from "@flit/ui/Button";
import { DIALOGO_CERRAR_CLS, DIALOGO_PANEL_CLS, DIALOGO_TITULO_CLS, DIALOGO_VELO_CLS, FranjaDialogo } from "@/components/admin/dialogo-propio";
import Link from "next/link";
import type { OtDrilldown } from "@/lib/api/ot-metrics";
import { Empty } from "./shared";
import { ETIQUETA_PUNTO_CLS, etiquetaKit } from "@flit/ui/StatusBadge";

/** Estado del drawer: qué bloque se abrió y con qué se pobló. */
export interface DrilldownState {
  bucket: string;
  label: string;
  loading: boolean;
  error: string | null;
  data: OtDrilldown | null;
}

/** Ruta de la bandeja OT filtrada por la placa o el VIN del trámite: «ir a decidirlo» real. */
function procedureDeepLink(transitOfficeId: string, item: OtDrilldown["items"][number]): string {
  const params = new URLSearchParams();
  if (item.placa) params.set("placa", item.placa);
  else if (item.vin) params.set("vin", item.vin);
  params.set("status", item.status);
  return `/admin/transit-offices/${transitOfficeId}/client-procedures?${params.toString()}`;
}

export function DrilldownPanel({
  state,
  transitOfficeId,
  onClose,
}: {
  state: DrilldownState | null;
  transitOfficeId: string;
  onClose: () => void;
}) {
  if (!state) return null;
  const { label, loading, error, data } = state;

  return (
    <div
      className={`fixed inset-0 z-[100] flex items-center justify-center px-4 ${DIALOGO_VELO_CLS}`}
      role="dialog"
      aria-modal="true"
      aria-label={label}
      data-testid="ot-reports-drilldown"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className={`flex max-h-[85dvh] w-full max-w-2xl flex-col overflow-hidden p-4 sm:p-6 ${DIALOGO_PANEL_CLS}`}>
        <FranjaDialogo icono="reportes" className="-mx-4 -mt-4 mb-4 sm:-mx-6 sm:-mt-6">
          <h3 className={`text-sm font-semibold ${DIALOGO_TITULO_CLS}`}>{label}</h3>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className={DIALOGO_CERRAR_CLS}
          >
            ✕
          </button>
        </FranjaDialogo>

        <div className="min-h-0 flex-1 overflow-y-auto">
          {loading && <p className="text-xs text-muted-foreground">Cargando…</p>}
          {error && (
            <p className="rounded-xl bg-[var(--di-danger-soft)] px-4 py-3 text-xs text-[color:var(--di-danger)]">
              {error}
            </p>
          )}
          {data && data.items.length === 0 && (
            <Empty>Ningún trámite compone este bloque en el periodo seleccionado.</Empty>
          )}
          {data && data.items.length > 0 && (
            <>
              <ul className="flex flex-col gap-2" data-testid="ot-reports-drilldown-list">
                {data.items.map((item) => (
                  <li
                    key={item.procedureInstanceId}
                    className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-border px-3 py-2 text-xs"
                  >
                    <div className="min-w-0">
                      <p className="font-semibold">
                        {item.referenceNumber}
                        {item.prioritario && (
                          <span {...etiquetaKit("warning", { className: "ml-2 align-middle" })}>
                            <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
                            Prioritario
                          </span>
                        )}
                      </p>
                      <p className="truncate text-[11px] text-muted-foreground">
                        {item.clientTenantName}
                        {(item.placa || item.vin) && ` · ${item.placa ?? item.vin}`}
                        {item.diasEsperando !== null && ` · ${item.diasEsperando} días esperando`}
                      </p>
                    </div>
                    {/* En pestaña nueva a propósito: el drill-down se abre desde un reporte con
                        filtros puestos y, muchas veces, con varios trámites que atender. Navegar en
                        la misma pestaña obliga a volver atrás y rearmar el reporte por cada uno. */}
                    <Link
                      href={procedureDeepLink(transitOfficeId, item)}
                      target="_blank"
                      rel="noopener noreferrer"
                      title="Se abre en una pestaña nueva"
                      className={buttonClass({ size: "sm", className: "shrink-0 text-[11px]" })}
                    >
                      Ir a gestionar ↗
                    </Link>
                  </li>
                ))}
              </ul>
              {data.omitidos > 0 && (
                <p className="mt-3 text-[11px] text-muted-foreground">
                  Se muestran {data.items.length} de {data.total}: {data.omitidos} quedaron fuera por
                  el tope de filas.
                </p>
              )}
            </>
          )}
        </div>
      </div>
    </div>
  );
}
