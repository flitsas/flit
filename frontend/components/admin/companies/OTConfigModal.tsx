"use client";

import { useState } from "react";
import { Settings2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ToggleSwitch } from "./ToggleSwitch";
import type {
  BlockingCriterion,
  ConsultationRestrictionKind,
  OtBlockingPolicy,
  OtConsultationRestriction,
  OtPrendaDocumentPolicy,
  TransitOffice,
} from "@/lib/api/types";
import { ApiValidationError, BLOCKING_CRITERION_DEFAULTS } from "@/lib/api/types";

// Modal único de configuración de un OT (HU #10194 — ajuste: bloqueos y restricciones de
// consulta viven en el MISMO modal, en dos secciones tituladas, en vez de dos modales
// separados). Cada sección conserva su propia lógica optimista + rollback + semántica
// dispersa (ausencia de fila = default del criterio/tipo) y sus mismos endpoints
// (`setOtBlockingPolicy` / `setOtConsultationRestriction`). Scoped a un solo OT (la fila
// elegida desde «⋯ Acciones» → «Configurar» en la tabla).

/** Criterios de bloqueo del preflight, en orden de presentación (FEATURE 05). */
export const BLOCKING_CRITERIA: { criterion: BlockingCriterion; label: string }[] = [
  { criterion: "soat", label: "SOAT vencido" },
  { criterion: "rtm", label: "RTM no vigente" },
  { criterion: "estado_vehiculo", label: "Estado del vehículo (RUNT)" },
  { criterion: "fines", label: "Comparendos (SIMIT)" },
  { criterion: "rnmc", label: "RNMC (medidas correctivas)" },
];

/** Consultas por OT, en orden de presentación (HU #10761). `vehicle` queda fuera (rompe el FUR). */
const CONSULTATION_KINDS: { kind: ConsultationRestrictionKind; label: string }[] = [
  { kind: "rnmc", label: "RNMC (medidas correctivas)" },
  { kind: "fines", label: "Comparendos (SIMIT)" },
];

/**
 * Default de cada consulta cuando no hay fila configurada (tabla dispersa). Comparendos es
 * OPT-OUT (se consulta salvo que lo apagues); RNMC es OPT-IN (no corre hasta que lo enciendas
 * para ese OT). Debe coincidir con el backend (ConsultationRestrictions.SettingOf).
 */
const CONSULTATION_KIND_DEFAULT: Record<ConsultationRestrictionKind, boolean> = {
  rnmc: false,
  fines: true,
};

export interface OTConfigModalProps {
  office: TransitOffice;
  /** Filas dispersas de bloqueo ya cargadas por el panel (de todos los OT); se filtran por `office.id`. */
  policies: OtBlockingPolicy[];
  /** Filas dispersas de restricción ya cargadas por el panel (de todos los OT); se filtran por `office.id`. */
  restrictions: OtConsultationRestriction[];
  /** OTs donde el check de prenda opcional está activo (tabla dispersa). */
  prendaOptionalPolicies: OtPrendaDocumentPolicy[];
  /** Persiste el estado deseado del bloqueo (PUT idempotente). */
  onToggleBlocking: (transitOfficeId: string, criterion: BlockingCriterion, blocks: boolean) => Promise<void>;
  /** Persiste el estado deseado de la restricción (PUT idempotente). */
  onToggleRestriction: (
    transitOfficeId: string,
    kind: ConsultationRestrictionKind,
    enabled: boolean,
  ) => Promise<void>;
  /** Persiste opt-out de prenda (true = opcional / check ON). */
  onTogglePrendaOptional: (transitOfficeId: string, documentOptional: boolean) => Promise<void>;
  onClose: () => void;
  /** Notificación opcional de error de persistencia (toast). */
  onError?: (message: string) => void;
}

export function OTConfigModal({
  office,
  policies,
  restrictions,
  prendaOptionalPolicies,
  onToggleBlocking,
  onToggleRestriction,
  onTogglePrendaOptional,
  onClose,
  onError,
}: OTConfigModalProps) {
  const [blockingState, setBlockingState] = useState<Map<BlockingCriterion, boolean>>(
    () =>
      new Map(
        policies.filter((p) => p.transitOfficeId === office.id).map((p) => [p.criterion, p.blocks]),
      ),
  );
  const [blockingPending, setBlockingPending] = useState<Set<BlockingCriterion>>(() => new Set());

  const [restrictionState, setRestrictionState] = useState<Map<ConsultationRestrictionKind, boolean>>(
    () =>
      new Map(
        restrictions
          .filter((r) => r.transitOfficeId === office.id)
          .map((r) => [r.consultationKind, r.enabled]),
      ),
  );
  const [restrictionPending, setRestrictionPending] = useState<Set<ConsultationRestrictionKind>>(
    () => new Set(),
  );

  const [prendaOptional, setPrendaOptional] = useState(
    () => prendaOptionalPolicies.some((p) => p.transitOfficeId === office.id && p.documentOptional),
  );
  const [prendaPending, setPrendaPending] = useState(false);

  const handleBlockingToggle = async (criterion: BlockingCriterion, label: string, next: boolean) => {
    const previous = blockingState.get(criterion) ?? BLOCKING_CRITERION_DEFAULTS[criterion];

    // UI optimista.
    setBlockingState((current) => new Map(current).set(criterion, next));
    setBlockingPending((current) => new Set(current).add(criterion));

    try {
      await onToggleBlocking(office.id, criterion, next);
    } catch (err) {
      // Rollback al estado anterior.
      setBlockingState((current) => new Map(current).set(criterion, previous));
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(serverMessage ?? `No se pudo cambiar el bloqueo de ${label} en ${office.name}.`);
    } finally {
      setBlockingPending((current) => {
        const next = new Set(current);
        next.delete(criterion);
        return next;
      });
    }
  };

  const handleRestrictionToggle = async (
    kind: ConsultationRestrictionKind,
    label: string,
    next: boolean,
  ) => {
    const previous = restrictionState.get(kind) ?? CONSULTATION_KIND_DEFAULT[kind];

    // UI optimista.
    setRestrictionState((current) => new Map(current).set(kind, next));
    setRestrictionPending((current) => new Set(current).add(kind));

    try {
      await onToggleRestriction(office.id, kind, next);
    } catch (err) {
      // Rollback al estado anterior.
      setRestrictionState((current) => new Map(current).set(kind, previous));
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ?? `No se pudo ${next ? "habilitar" : "inhabilitar"} ${label} en ${office.name}.`,
      );
    } finally {
      setRestrictionPending((current) => {
        const next = new Set(current);
        next.delete(kind);
        return next;
      });
    }
  };

  const handlePrendaOptionalToggle = async (next: boolean) => {
    const previous = prendaOptional;
    setPrendaOptional(next);
    setPrendaPending(true);
    try {
      await onTogglePrendaOptional(office.id, next);
    } catch (err) {
      setPrendaOptional(previous);
      const serverMessage = err instanceof ApiValidationError ? err.errors[0]?.message : undefined;
      onError?.(
        serverMessage ??
          `No se pudo ${next ? "hacer opcional" : "volver a exigir"} el documento de prenda en ${office.name}.`,
      );
    } finally {
      setPrendaPending(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title={`Configurar — ${office.name}`}
      description="Define, para este organismo, bloqueos del preflight, consultas y obligatoriedad de prenda."
      icon={Settings2}
      size="lg"
    >
      <div className="space-y-5">
        <section aria-labelledby="ot-config-blocking-heading">
          <h3 id="ot-config-blocking-heading" className="mb-1 text-xs font-semibold">
            Bloqueos
          </h3>
          <p className="mb-2 text-[11px] opacity-60">
            Activado = bloquea el trámite (el gestor debe subsanar o aceptar el riesgo);
            desactivado = solo advierte y el usuario decide continuar. Ningún criterio impide crear
            el trámite. Los cambios se guardan al instante.
          </p>
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
                checked={blockingState.get(criterion) ?? BLOCKING_CRITERION_DEFAULTS[criterion]}
                disabled={blockingPending.has(criterion)}
                onChange={(checked) => void handleBlockingToggle(criterion, label, checked)}
              />
            ))}
          </div>
        </section>

        <hr className="border-t" />

        <section aria-labelledby="ot-config-restrictions-heading">
          <h3 id="ot-config-restrictions-heading" className="mb-1 text-xs font-semibold">
            Restricciones de consulta
          </h3>
          <p className="mb-2 text-[11px] opacity-60">
            <strong>RNMC</strong> se consulta solo si lo activas aquí; <strong>Comparendos</strong>{" "}
            se consulta por defecto salvo que lo desactives. Nada de esto impide crear el trámite.
          </p>
          <div
            data-testid={`ot-restrictions-${office.id}`}
            role="group"
            aria-label={`Restricciones de consulta de ${office.name}`}
            className="grid gap-2 sm:grid-cols-2"
          >
            {CONSULTATION_KINDS.map(({ kind, label }) => (
              <ToggleSwitch
                key={kind}
                id={`restriction-${office.id}-${kind}`}
                label={label}
                checked={restrictionState.get(kind) ?? CONSULTATION_KIND_DEFAULT[kind]}
                disabled={restrictionPending.has(kind)}
                onChange={(checked) => void handleRestrictionToggle(kind, label, checked)}
              />
            ))}
          </div>
        </section>

        <hr className="border-t" />

        <section aria-labelledby="ot-config-prenda-heading">
          <h3 id="ot-config-prenda-heading" className="mb-1 text-xs font-semibold">
            Documento de prenda
          </h3>
          <p className="mb-2 text-[11px] opacity-60">
            Por defecto el documento de inscripción/registro de prenda es <strong>obligatorio</strong>.
            Activa el check para que deje de serlo (opcional) en esta compañía y organismo.
          </p>
          <div data-testid={`ot-prenda-optional-${office.id}`}>
            <ToggleSwitch
              id={`prenda-optional-${office.id}`}
              label="Prenda opcional (no exigir documento)"
              checked={prendaOptional}
              disabled={prendaPending}
              onChange={(checked) => void handlePrendaOptionalToggle(checked)}
            />
          </div>
        </section>
      </div>
    </Modal>
  );
}
