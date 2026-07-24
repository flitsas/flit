"use client";

import { useState } from "react";
import { ShieldAlert } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "./ToggleSwitch";
import type { BlockingCriterion, OtBlockingPolicy, TransitOffice } from "@/lib/api/types";
import { BLOCKING_CRITERION_DEFAULTS, ApiValidationError } from "@/lib/api/types";

// Modal de criterios de bloqueo del preflight, SCOPED a un solo OT (HU #10194 —
// consolidación de la config de OT en una tabla con menú "⋯ Acciones"). Reutiliza la
// lógica de FEATURE 05 (UI optimista + rollback; semántica dispersa: ausencia de fila =
// default del criterio) que antes vivía en `OTBlockingPoliciesMatrix`, ahora aplicada a
// una sola fila (el OT elegido desde el menú de acciones de la tabla).
export const BLOCKING_CRITERIA: { criterion: BlockingCriterion; label: string }[] = [
  { criterion: "soat", label: "SOAT vencido" },
  { criterion: "rtm", label: "RTM no vigente" },
  { criterion: "estado_vehiculo", label: "Estado del vehículo (RUNT)" },
  { criterion: "fines", label: "Comparendos (SIMIT)" },
  { criterion: "rnmc", label: "RNMC (medidas correctivas)" },
];

export interface OTBlockingPoliciesModalProps {
  office: TransitOffice;
  /** Filas dispersas ya cargadas por el panel (de todos los OT); se filtran por `office.id`. */
  policies: OtBlockingPolicy[];
  /** Persiste el estado deseado (PUT idempotente). */
  onToggle: (transitOfficeId: string, criterion: BlockingCriterion, blocks: boolean) => Promise<void>;
  onClose: () => void;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

export function OTBlockingPoliciesModal({
  office,
  policies,
  onToggle,
  onClose,
  onError,
}: OTBlockingPoliciesModalProps) {
  const [state, setState] = useState<Map<BlockingCriterion, boolean>>(
    () =>
      new Map(
        policies.filter((p) => p.transitOfficeId === office.id).map((p) => [p.criterion, p.blocks]),
      ),
  );
  const [pending, setPending] = useState<Set<BlockingCriterion>>(() => new Set());

  const handleToggle = async (criterion: BlockingCriterion, label: string, next: boolean) => {
    const previous = state.get(criterion) ?? BLOCKING_CRITERION_DEFAULTS[criterion];

    // UI optimista.
    setState((current) => new Map(current).set(criterion, next));
    setPending((current) => new Set(current).add(criterion));

    try {
      await onToggle(office.id, criterion, next);
    } catch (err) {
      // Rollback al estado anterior.
      setState((current) => new Map(current).set(criterion, previous));
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(serverMessage ?? `No se pudo cambiar el bloqueo de ${label} en ${office.name}.`);
    } finally {
      setPending((current) => {
        const next = new Set(current);
        next.delete(criterion);
        return next;
      });
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Configurar bloqueos — ${office.name}`}
      description="Activado = bloquea el trámite (el gestor debe subsanar o aceptar el riesgo); desactivado = solo advierte y el usuario decide continuar. Ningún criterio impide crear el trámite. Los cambios se guardan al instante."
      icon={ShieldAlert}
    >
      <div
        data-testid={`ot-blocking-${office.id}`}
        role="group"
        aria-label={`Criterios de bloqueo de ${office.name}`}
        className="grid gap-2 sm:grid-cols-2"
      >
        {BLOCKING_CRITERIA.map(({ criterion, label }) => (
          <ToggleSwitch
            key={criterion}
            id={`blocking-${office.id}-${criterion}`}
            label={label}
            checked={state.get(criterion) ?? BLOCKING_CRITERION_DEFAULTS[criterion]}
            disabled={pending.has(criterion)}
            onChange={(checked) => void handleToggle(criterion, label, checked)}
          />
        ))}
      </div>
    </Modal>
  );
}
