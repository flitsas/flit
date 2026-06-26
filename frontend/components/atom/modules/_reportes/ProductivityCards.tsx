"use client";

import { useMemo, useState } from "react";
import { Users } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import type { TopProducer } from "@/lib/api/types";

interface ProductivityCardsProps {
  producers: TopProducer[];
  status: UiStatus;
  errorMessage?: string;
  onRetry?: () => void;
}

/**
 * Top 5 de productividad con multiselect de usuarios (HU #10248, AC2). Sin selección,
 * los indicadores agregan a todos; al seleccionar uno o más, reflejan solo esos
 * usuarios. Si el conjunto activo no tiene trámites enviados, muestra el mensaje
 * "Sin trámites en el periodo seleccionado" (AC3).
 */
export function ProductivityCards({ producers, status, errorMessage, onRetry }: ProductivityCardsProps) {
  const [selected, setSelected] = useState<Set<string>>(new Set());

  function toggle(userId: string) {
    setSelected((prev) => {
      const next = new Set(prev);
      if (next.has(userId)) next.delete(userId);
      else next.add(userId);
      return next;
    });
  }

  const activeProducers = useMemo(
    () => (selected.size === 0 ? producers : producers.filter((p) => selected.has(p.userId))),
    [producers, selected],
  );

  const totals = useMemo(
    () =>
      activeProducers.reduce(
        (acc, p) => ({
          submitted: acc.submitted + p.submittedCount,
          approved: acc.approved + p.approvedCount,
          rejected: acc.rejected + p.rejectedCount,
        }),
        { submitted: 0, approved: 0, rejected: 0 },
      ),
    [activeProducers],
  );

  const noActivity = totals.submitted === 0;

  return (
    <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border flex flex-col" style={{ borderColor: "#DFE5ED" }}>
      <div className="flex items-center justify-between mb-3 shrink-0">
        <h2 className="text-sm font-bold flex items-center gap-2">
          <Users className="h-4 w-4" style={{ color: "#557EFF" }} /> Top 5 productividad
        </h2>
        {selected.size > 0 && (
          <button
            type="button"
            onClick={() => setSelected(new Set())}
            className="text-[11px] font-semibold opacity-70 hover:opacity-100"
          >
            Limpiar selección ({selected.size})
          </button>
        )}
      </div>

      <UiStateBoundary
        status={status}
        errorMessage={errorMessage}
        onRetry={onRetry}
        emptyMessage="No hay productividad para el periodo seleccionado."
        skeletonRows={3}
      >
        {/* Indicadores agregados del conjunto activo (AC2) */}
        <div
          className="grid grid-cols-3 gap-3 mb-3"
          aria-live="polite"
          aria-label={selected.size === 0 ? "Indicadores de todos los usuarios" : "Indicadores de los usuarios seleccionados"}
        >
          <Indicator label="Enviados" value={totals.submitted} color="#557EFF" />
          <Indicator label="Aprobados" value={totals.approved} color="#8CC63F" />
          <Indicator label="Rechazados" value={totals.rejected} color="#FF4E00" />
        </div>

        {noActivity ? (
          <p className="text-xs opacity-70 py-2" data-testid="productividad-sin-tramites">
            Sin trámites en el periodo seleccionado.
          </p>
        ) : (
          <ul className="grid grid-cols-1 sm:grid-cols-2 xl:grid-cols-3 gap-2">
            {producers.map((p) => {
              const isSelected = selected.has(p.userId);
              return (
                <li key={p.userId}>
                  <button
                    type="button"
                    onClick={() => toggle(p.userId)}
                    aria-pressed={isSelected}
                    className="w-full text-left rounded-xl border p-3 outline-none transition-colors hover:border-[#557EFF] focus:border-[#557EFF]"
                    style={{ borderColor: isSelected ? "#557EFF" : "#DFE5ED", background: isSelected ? "#557EFF0F" : undefined }}
                  >
                    <p className="text-xs font-semibold truncate">{p.displayName}</p>
                    <div className="flex items-center gap-3 mt-1 text-[10px] opacity-80">
                      <span>Env. <b>{p.submittedCount}</b></span>
                      <span>Apr. <b>{p.approvedCount}</b></span>
                      <span>Rech. <b>{p.rejectedCount}</b></span>
                    </div>
                  </button>
                </li>
              );
            })}
          </ul>
        )}
      </UiStateBoundary>
    </section>
  );
}

function Indicator({ label, value, color }: { label: string; value: number; color: string }) {
  return (
    <div className="rounded-xl border p-3" style={{ borderColor: "#DFE5ED" }}>
      <p className="text-[10px] opacity-70 font-medium">{label}</p>
      <p className="text-xl font-bold mt-0.5" style={{ color }}>
        {value}
      </p>
    </div>
  );
}
