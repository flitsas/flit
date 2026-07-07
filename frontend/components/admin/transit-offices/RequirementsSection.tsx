"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { fetchOtRequirements, updateOtRequirements } from "@/lib/api/admin-ot";
import type { OtRequirements } from "@/lib/api/types-ot";

interface RequirementsSectionProps {
  /** Oficina OT en scope (SuperAdmin navegando el hub). */
  transitOfficeId?: string;
}

interface ToggleRowProps {
  id: string;
  label: string;
  description: string;
  checked: boolean;
  disabled?: boolean;
  onChange: (next: boolean) => void;
}

function ToggleRow({ id, label, description, checked, disabled, onChange }: ToggleRowProps) {
  return (
    <div className="flex items-start justify-between gap-4 rounded-2xl border p-4">
      <div className="min-w-0">
        <label htmlFor={id} className="block text-sm font-semibold">
          {label}
        </label>
        <p className="mt-1 text-xs opacity-70">{description}</p>
      </div>
      <button
        id={id}
        type="button"
        role="switch"
        aria-checked={checked}
        aria-label={label}
        disabled={disabled}
        onClick={() => onChange(!checked)}
        className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition-colors disabled:opacity-50"
        style={{ background: checked ? "#557EFF" : "#DFE5ED" }}
      >
        <span
          className="inline-block h-5 w-5 transform rounded-full bg-white shadow transition-transform"
          style={{ transform: checked ? "translateX(22px)" : "translateX(2px)" }}
        />
      </button>
    </div>
  );
}

/** Pantalla de requisitos configurables del OT (HU #10547): RNMC, ruta de placa e identidad. */
export function RequirementsSection({ transitOfficeId }: RequirementsSectionProps) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [requirements, setRequirements] = useState<OtRequirements | null>(null);
  const [saving, setSaving] = useState(false);

  const scope = transitOfficeId ? { transitOfficeId } : undefined;

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchOtRequirements(signal, transitOfficeId ? { transitOfficeId } : undefined);
        if (signal?.aborted) return;
        setRequirements(result);
        setStatus("ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [transitOfficeId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const patch = (change: Partial<OtRequirements>) =>
    setRequirements((prev) => (prev ? { ...prev, ...change } : prev));

  const save = async () => {
    if (!requirements) return;
    setSaving(true);
    try {
      const saved = await updateOtRequirements(
        {
          requiresRnmc: requirements.requiresRnmc,
          allowPlatePreassign: requirements.allowPlatePreassign,
          identityValidationEnabled: requirements.identityValidationEnabled,
        },
        scope,
      );
      setRequirements(saved);
      show("Requisitos del OT actualizados.", "success");
    } catch {
      show("No se pudieron guardar los requisitos.", "error");
    } finally {
      setSaving(false);
    }
  };

  return (
    <UiStateBoundary
      status={status}
      errorMessage="No se pudieron cargar los requisitos del OT."
      onRetry={() => void load()}
      skeletonRows={3}
    >
      {requirements && (
        <div className="space-y-4">
          <ToggleRow
            id="ot-req-rnmc"
            label="Exige RNMC"
            description="Solicita la consulta RNMC en el preflight de las radicaciones hacia este organismo."
            checked={requirements.requiresRnmc}
            disabled={saving}
            onChange={(v) => patch({ requiresRnmc: v })}
          />
          <ToggleRow
            id="ot-req-plate"
            label="Ruta de placa preasignada"
            description="Habilita la ruta con placa preasignada para secretarías con backoffice de preasignación."
            checked={requirements.allowPlatePreassign}
            disabled={saving}
            onChange={(v) => patch({ allowPlatePreassign: v })}
          />
          <ToggleRow
            id="ot-req-identity"
            label="Validación de identidad"
            description="Exige la validación biométrica de identidad. Desactívala solo según el acuerdo con el OT."
            checked={requirements.identityValidationEnabled}
            disabled={saving}
            onChange={(v) => patch({ identityValidationEnabled: v })}
          />

          <div className="flex justify-end">
            <button
              type="button"
              onClick={() => void save()}
              disabled={saving}
              className="rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-50"
              style={{ background: "#557EFF" }}
            >
              {saving ? "Guardando…" : "Guardar requisitos"}
            </button>
          </div>
        </div>
      )}
    </UiStateBoundary>
  );
}
