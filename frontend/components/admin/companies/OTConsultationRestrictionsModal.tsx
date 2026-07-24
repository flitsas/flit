"use client";

import { useState } from "react";
import { SlidersHorizontal } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "./ToggleSwitch";
import type {
  ConsultationRestrictionKind,
  OtConsultationRestriction,
  TransitOffice,
} from "@/lib/api/types";
import { ApiValidationError } from "@/lib/api/types";

// Modal de restricciones de consulta por OT, SCOPED a un solo OT (HU #10194 —
// consolidación de la config de OT en una tabla con menú "⋯ Acciones"). Reutiliza la
// lógica de HU #10761 (UI optimista + rollback; semántica dispersa) que antes vivía en
// `OTConsultationRestrictionsMatrix`, ahora aplicada a una sola fila (el OT elegido desde
// el menú de acciones de la tabla).

/** Consultas por OT, en orden de presentación. `vehicle` queda fuera (rompe el FUR). */
const KINDS: { kind: ConsultationRestrictionKind; label: string }[] = [
  { kind: "rnmc", label: "RNMC (medidas correctivas)" },
  { kind: "fines", label: "Comparendos (SIMIT)" },
];

/**
 * Default de cada consulta cuando no hay fila configurada (tabla dispersa). Comparendos es
 * OPT-OUT (se consulta salvo que lo apagues); RNMC es OPT-IN (no corre hasta que lo enciendas
 * para ese OT). Debe coincidir con el backend (ConsultationRestrictions.SettingOf).
 */
const KIND_DEFAULT: Record<ConsultationRestrictionKind, boolean> = {
  rnmc: false,
  fines: true,
};

export interface OTConsultationRestrictionsModalProps {
  office: TransitOffice;
  /** Filas dispersas ya cargadas por el panel (de todos los OT); se filtran por `office.id`. */
  restrictions: OtConsultationRestriction[];
  /** Persiste el estado deseado (PUT idempotente). */
  onToggle: (
    transitOfficeId: string,
    kind: ConsultationRestrictionKind,
    enabled: boolean,
  ) => Promise<void>;
  onClose: () => void;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

export function OTConsultationRestrictionsModal({
  office,
  restrictions,
  onToggle,
  onClose,
  onError,
}: OTConsultationRestrictionsModalProps) {
  const [state, setState] = useState<Map<ConsultationRestrictionKind, boolean>>(
    () =>
      new Map(
        restrictions
          .filter((r) => r.transitOfficeId === office.id)
          .map((r) => [r.consultationKind, r.enabled]),
      ),
  );
  const [pending, setPending] = useState<Set<ConsultationRestrictionKind>>(() => new Set());

  const handleToggle = async (kind: ConsultationRestrictionKind, label: string, next: boolean) => {
    const previous = state.get(kind) ?? KIND_DEFAULT[kind];

    // UI optimista.
    setState((current) => new Map(current).set(kind, next));
    setPending((current) => new Set(current).add(kind));

    try {
      await onToggle(office.id, kind, next);
    } catch (err) {
      // Rollback al estado anterior.
      setState((current) => new Map(current).set(kind, previous));
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ?? `No se pudo ${next ? "habilitar" : "inhabilitar"} ${label} en ${office.name}.`,
      );
    } finally {
      setPending((current) => {
        const next = new Set(current);
        next.delete(kind);
        return next;
      });
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Configurar restricciones de consulta — ${office.name}`}
      description={
        <>
          <strong>RNMC</strong> se consulta solo si lo activas aquí; <strong>Comparendos</strong> se
          consulta por defecto salvo que lo desactives. Nada de esto impide crear el trámite. Los
          cambios se guardan al instante.
        </>
      }
      icon={SlidersHorizontal}
    >
      <div
        data-testid={`ot-restrictions-${office.id}`}
        role="group"
        aria-label={`Restricciones de consulta de ${office.name}`}
        className="grid gap-2 sm:grid-cols-2"
      >
        {KINDS.map(({ kind, label }) => (
          <ToggleSwitch
            key={kind}
            id={`restriction-${office.id}-${kind}`}
            label={label}
            checked={state.get(kind) ?? KIND_DEFAULT[kind]}
            disabled={pending.has(kind)}
            onChange={(checked) => void handleToggle(kind, label, checked)}
          />
        ))}
      </div>
    </Modal>
  );
}
