'use client';

import { useState } from 'react';
import { CheckCircle2, Undo2 } from 'lucide-react';
import { usePermissions } from '@/hooks/usePermissions';
import type { RevocationEligibility, RequestRevocationResult } from '@/lib/api/types/procedure-runtime';
import { RevocationRequestModal } from './RevocationRequestModal';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';

/**
 * HU #12573 (Feature #12565) — botón "Solicitar revocatoria" + sus gates de visibilidad/habilitación
 * sobre un trámite `aprobado` (AC1-AC3). Vive en `ReadOnlyStateNotice` del wizard (mismo patrón que
 * "Subsanar trámite" sobre `rechazado`): un solo punto de acción dentro del aviso que ya explica por
 * qué el trámite no se edita.
 *
 * HU #12574 — el `onClick` habilitado abre `RevocationRequestModal` (Paso 1 advertencia → Paso 2
 * formulario). Tras un 201 exitoso, el botón se apaga LOCALMENTE (`justRequested`) en vez de seguir
 * accionable: `RevocationEligibility` no expone "ya hay una solicitud activa" (ver XML doc del DTO
 * en el backend), así que un segundo click solo produciría un 409 `solicitud_activa_existente`. AC3
 * — no hay opción de retirar la solicitud: sin botón accionable, no hay de dónde retirarla.
 */

/** AC2 — rol Operario o interno FLIT: no es el Administrador de la compañía dueña del trámite. */
const REASON_NOT_ADMIN = 'Solo el Administrador de la compañía puede solicitar la revocatoria.';
/** AC1 (Given "trámite Aprobado/FLIT") — origen ICT o foto migrada de V1, no creado en FLIT. */
const REASON_SOURCE_NOT_SUPPORTED = 'El trámite no fue creado en FLIT; no admite solicitud de revocatoria.';
/** AC3 — texto EXACTO del criterio de aceptación. */
const REASON_WINDOW_EXPIRED = 'Ventana de revocatoria vencida';
/** Fallback defensivo: el backend solo calcula `revocationEligibility` sobre `aprobado` (ver tipo). */
const REASON_NOT_APPLICABLE = 'La revocatoria no aplica al estado actual del trámite.';

export interface RevocationRequestButtonProps {
  instanceId: string;
  /** `ProcedureInstanceDetail.revocationEligibility` ya resuelto por el backend (AC1/AC3). */
  eligibility: RevocationEligibility | null | undefined;
  /**
   * HU #12574 — se dispara tras el 201 del modal, para que el padre (`TramiteWizard`) recargue
   * wizard/detalle (mismo patrón que `activarSubsanacion`/`cancelarSubsanacion`: `refresh()` +
   * re-consulta del detalle). No es obligatorio: el botón ya refleja el envío localmente (AC3) sin
   * depender de este callback.
   */
  onRequested?: (result: RequestRevocationResult) => void;
}

export function RevocationRequestButton({ instanceId, eligibility, onRequested }: RevocationRequestButtonProps) {
  const { isAdminCompany } = usePermissions();
  const [modalOpen, setModalOpen] = useState(false);
  // AC3 — tras un envío exitoso NO hay opción de retirar la solicitud (ver comentario de clase).
  const [justRequested, setJustRequested] = useState(false);

  // AC1 — habilitado: rol Administrador + trámite Aprobado/FLIT + (sin ventana o dentro de ventana).
  // AC2 — Operario/interno FLIT: visible pero no accionable, cualquiera que sea el estado del gate de
  // trámite (el rol manda primero, para que el motivo sea siempre el correcto).
  // AC3 — ventana vencida: deshabilitado con motivo específico.
  const disabledReason = !isAdminCompany
    ? REASON_NOT_ADMIN
    : !eligibility
      ? REASON_NOT_APPLICABLE
      : !eligibility.sourceSupported
        ? REASON_SOURCE_NOT_SUPPORTED
        : eligibility.windowExpired
          ? REASON_WINDOW_EXPIRED
          : null;

  const enabled = disabledReason === null && !justRequested;
  const reasonId = `revocation-request-reason-${instanceId}`;

  if (justRequested) {
    return (
      <p
        className="inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-medium"
        style={{ borderColor: '#557EFF', color: '#557EFF', background: 'rgba(85,126,255,0.08)' }}
        role="status"
      >
        <CheckCircle2 className="h-3.5 w-3.5" aria-hidden="true" />
        Solicitud de revocatoria enviada — en revisión
      </p>
    );
  }

  return (
    <div>
      <button
        type="button"
        disabled={!enabled}
        aria-describedby={disabledReason ? reasonId : undefined}
        onClick={enabled ? () => setModalOpen(true) : undefined}
        title={disabledReason ?? 'Solicitar revocatoria de este trámite'}
        className="inline-flex items-center gap-1.5 rounded-xl px-5 py-2 text-xs font-semibold text-white transition hover:opacity-90 disabled:cursor-not-allowed disabled:opacity-50 focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-[#557EFF]"
        style={{ background: enabled ? WIZARD_CTA_GRADIENT : '#94A3B8' }}
      >
        <Undo2 className="h-3.5 w-3.5" aria-hidden="true" />
        Solicitar revocatoria
      </button>
      {disabledReason ? (
        <p id={reasonId} className="mt-1.5 text-[11px]" style={{ color: '#59677D' }}>
          {disabledReason}
        </p>
      ) : null}
      {modalOpen ? (
        <RevocationRequestModal
          instanceId={instanceId}
          onClose={() => setModalOpen(false)}
          onSuccess={(result) => {
            setModalOpen(false);
            setJustRequested(true);
            onRequested?.(result);
          }}
        />
      ) : null}
    </div>
  );
}
