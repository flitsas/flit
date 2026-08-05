"use client";

// Drill-down: de qué está hecho cada número del panel operativo.
//
// Ningún número del reporte puede ser un callejón sin salida: quien lee «3 con más de 7 días»
// necesita saber cuáles son para ir a resolverlos. El backend los recalcula con los MISMOS
// predicados de la tarjeta, así que la lista nunca contradice al número que la abrió.

import Link from "next/link";
import type { OtDrilldown } from "@/lib/api/ot-metrics";
import { Empty } from "./shared";

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
      className="fixed inset-0 z-[100] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-label={label}
      data-testid="ot-reports-drilldown"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget) onClose();
      }}
    >
      <div className="flex max-h-[85dvh] w-full max-w-2xl flex-col rounded-2xl border border-[#DFE5ED] bg-white p-4 shadow-2xl sm:p-6 dark:border-white/10 dark:bg-[#0B0F14]">
        <div className="mb-3 flex shrink-0 items-start justify-between gap-3">
          <h3 className="text-sm font-semibold">{label}</h3>
          <button
            type="button"
            aria-label="Cerrar"
            onClick={onClose}
            className="text-slate-400 hover:text-slate-700 dark:hover:text-white"
          >
            ✕
          </button>
        </div>

        <div className="min-h-0 flex-1 overflow-y-auto">
          {loading && <p className="text-xs text-[#6B7280] dark:text-white/50">Cargando…</p>}
          {error && (
            <p className="rounded-xl bg-red-50 px-4 py-3 text-xs text-red-700 dark:bg-red-500/10 dark:text-red-300">
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
                    className="flex flex-wrap items-center justify-between gap-2 rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs dark:border-white/10"
                  >
                    <div className="min-w-0">
                      <p className="font-semibold">
                        {item.referenceNumber}
                        {item.prioritario && (
                          <span className="ml-2 rounded-full bg-amber-100 px-2 py-0.5 text-[10px] font-semibold text-amber-800 dark:bg-amber-500/20 dark:text-amber-300">
                            Prioritario
                          </span>
                        )}
                      </p>
                      <p className="truncate text-[11px] text-[#6B7280] dark:text-white/50">
                        {item.clientTenantName}
                        {(item.placa || item.vin) && ` · ${item.placa ?? item.vin}`}
                        {item.diasEsperando !== null && ` · ${item.diasEsperando} días esperando`}
                      </p>
                    </div>
                    <Link
                      href={procedureDeepLink(transitOfficeId, item)}
                      className="shrink-0 rounded-lg px-3 py-1.5 text-[11px] font-semibold text-white"
                      style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
                    >
                      Ir a gestionar
                    </Link>
                  </li>
                ))}
              </ul>
              {data.omitidos > 0 && (
                <p className="mt-3 text-[11px] text-[#6B7280] dark:text-white/50">
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
