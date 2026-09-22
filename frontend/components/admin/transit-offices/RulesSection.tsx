"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Pencil } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { createOtRule, fetchOtRules, updateOtRule } from "@/lib/api/admin-ot";
import type { OtRule } from "@/lib/api/types-ot";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActions } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { RuleFormPanel } from "./RuleFormPanel";

/** Lista y constructor de reglas OT con hot-swap (HU #10223). HU #12731 — DataTable sin columnas de detalle. */
export function RulesSection() {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rules, setRules] = useState<OtRule[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [editingRule, setEditingRule] = useState<OtRule | null>(null);
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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
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

  const columns: DataTableColumn<OtRule>[] = useMemo(
    () => [
      {
        key: "name",
        header: "Nombre",
        cellClassName: "font-semibold",
        render: (row) => row.name,
      },
      {
        key: "state",
        header: "Estado",
        render: (row) => (
          <StatusBadge
            label={row.isEnabled ? "Activa" : "Inactiva"}
            tone={row.isEnabled ? "success" : "neutral"}
          />
        ),
      },
      {
        key: "toggle",
        header: "Toggle",
        align: "right",
        render: (row) => (
          <label className="inline-flex cursor-pointer items-center gap-2">
            <span className="sr-only">Activa / Inactiva — {row.name}</span>
            <input
              type="checkbox"
              role="switch"
              checked={row.isEnabled}
              disabled={togglingId === row.id}
              aria-checked={row.isEnabled}
              className="h-4 w-8 cursor-pointer accent-[#557EFF]"
              onChange={(e) => void handleToggle(row, e.target.checked)}
            />
          </label>
        ),
      },
      {
        key: "actions",
        header: "Acción",
        align: "right",
        render: (row) => (
          <RowActions
            actions={[
              {
                icon: Pencil,
                label: `Editar regla ${row.name}`,
                tone: "primary",
                onClick: () => {
                  setEditingRule(row);
                  setFormOpen(true);
                },
              },
            ]}
          />
        ),
      },
    ],
    [togglingId],
  );

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={() => {
        setEditingRule(null);
        setFormOpen(true);
      }}
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
          onClick={() => {
            setEditingRule(null);
            setFormOpen(true);
          }}
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
        <DataTable
          columns={columns}
          rows={rules}
          getRowKey={(row) => row.id}
          ariaLabel="Reglas del motor de reglas OT"
          minWidth={640}
        />
      </UiStateBoundary>

      <RuleFormPanel
        open={formOpen}
        rule={editingRule}
        onClose={() => {
          setFormOpen(false);
          setEditingRule(null);
        }}
        onCreate={createOtRule}
        onUpdate={updateOtRule}
        onSaved={(rule) => {
          setRules((prev) => {
            const idx = prev.findIndex((r) => r.id === rule.id);
            if (idx >= 0) {
              const next = [...prev];
              next[idx] = rule;
              return next;
            }
            return [rule, ...prev];
          });
          setStatus("ready");
          setFormOpen(false);
          setEditingRule(null);
          show(editingRule ? "Regla actualizada." : "Regla creada.", "success");
        }}
      />
    </div>
  );
}
