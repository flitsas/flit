"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { createOtRule, fetchOtRules, updateOtRule } from "@/lib/api/admin-ot";
import type { OtRule } from "@/lib/api/types-ot";
import { OtStatusBadge } from "./OtStatusBadge";
import { RuleFormPanel } from "./RuleFormPanel";
import { formatOtRuleAction } from "./ot-utils";

/** Lista y constructor de reglas OT con hot-swap (HU #10223). */
export function RulesSection() {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rules, setRules] = useState<OtRule[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [togglingId, setTogglingId] = useState<string | null>(null);

  const loadRules = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const result = await fetchOtRules(signal);
      if (signal?.aborted) return;
      setRules(result.data);
      setStatus(result.data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) setStatus("error");
    }
  }, []);

  useEffect(() => {
    const c = new AbortController();
    void loadRules(c.signal);
    return () => c.abort();
  }, [loadRules]);

  const handleToggle = async (rule: OtRule, next: boolean) => {
    setTogglingId(rule.id);
    setRules((prev) =>
      prev.map((r) => (r.id === rule.id ? { ...r, isEnabled: next } : r)),
    );
    try {
      const updated = await updateOtRule(rule.id, { isEnabled: next });
      setRules((prev) => prev.map((r) => (r.id === rule.id ? updated : r)));
    } catch {
      setRules((prev) =>
        prev.map((r) => (r.id === rule.id ? { ...r, isEnabled: !next } : r)),
      );
      show("No se pudo actualizar la regla.", "error");
    } finally {
      setTogglingId(null);
    }
  };

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={() => setFormOpen(true)}
    >
      Crear primera regla
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex justify-end">
        <button
          type="button"
          className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          onClick={() => setFormOpen(true)}
        >
          Nueva regla
        </button>
      </div>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay reglas configuradas."
        emptyCta={emptyCta}
        errorMessage="Error al cargar reglas."
        onRetry={() => void loadRules()}
        skeletonRows={4}
      >
        <table className="w-full border-separate border-spacing-y-2 text-xs">
          <thead>
            <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
              <th className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Nombre
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Lógica
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Acción
              </th>
              <th className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                Estado
              </th>
              <th className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
                Toggle
              </th>
            </tr>
          </thead>
          <tbody>
            {rules.map((rule) => (
              <tr key={rule.id} className="bg-white dark:bg-[#0B0F14]">
                <td className="rounded-l-xl border-y border-l px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  {rule.name}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  {rule.logic}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  {formatOtRuleAction(rule.action.type)}
                  {rule.action.queue_name ? ` (${rule.action.queue_name})` : ""}
                </td>
                <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                  <OtStatusBadge
                    label={rule.isEnabled ? "Activa" : "Inactiva"}
                    tone={rule.isEnabled ? "success" : "neutral"}
                  />
                </td>
                <td
                  className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                  style={{ borderColor: "#DFE5ED" }}
                >
                  <label className="inline-flex cursor-pointer items-center gap-2">
                    <span className="sr-only">Activa / Inactiva — {rule.name}</span>
                    <input
                      type="checkbox"
                      role="switch"
                      checked={rule.isEnabled}
                      disabled={togglingId === rule.id}
                      aria-checked={rule.isEnabled}
                      className="h-4 w-8 cursor-pointer accent-[#557EFF]"
                      onChange={(e) => void handleToggle(rule, e.target.checked)}
                    />
                  </label>
                </td>
              </tr>
            ))}
          </tbody>
        </table>
      </UiStateBoundary>

      <RuleFormPanel
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onCreate={createOtRule}
        onSaved={(rule) => {
          setRules((prev) => [rule, ...prev]);
          setStatus("ready");
          setFormOpen(false);
          show("Regla creada.", "success");
        }}
      />
    </div>
  );
}
