'use client';

import { useEffect, useId, useRef, useState, type ReactNode } from 'react';
import { AlertTriangle, Check, X } from 'lucide-react';
import { Modal } from '@flit/ui/Modal';
import { useWizardFocusTrap } from '@/components/operacion/use-wizard-focus-trap';
import { MANUAL_AVISO, MANUAL_AVISO_BASE, MANUAL_BTN_SECUNDARIO, MANUAL_FOCO, MANUAL_SELECT_CLASS } from './manual-field-styles';
import { WIZARD_CTA_GRADIENT, WIZARD_CTA_GRADIENT_DANGER, WIZARD_LABEL, WIZARD_SELECT } from '@/components/operacion/wizard-field-styles';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { ApiError } from '@/lib/api/types';
import type { ManualDetail } from '@/lib/api/types/manual-review';
import { MOTIVOS_RECHAZO_MANUAL } from '@/lib/identidad/motivos-rechazo-manual';

/**
 * Aprobar y rechazar una validación manual (HU-C7). Solo se ofrecen con el registro en
 * `pendiente_revision_manual`. Un solo paso de confirmación por acción. Los motivos salen de la lista
 * homologada (`motivos-rechazo-manual.ts`), la misma del correo al cliente.
 */

const BTN_BASE =
  'inline-flex items-center justify-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white transition disabled:cursor-not-allowed disabled:opacity-70 ' +
  MANUAL_FOCO;

export function GradientButton({
  children,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode }) {
  return (
    <button type="button" {...props} className={`${BTN_BASE} ${props.className ?? ''}`} style={{ background: WIZARD_CTA_GRADIENT }}>
      {children}
    </button>
  );
}

export function DangerButton({
  children,
  ...props
}: React.ButtonHTMLAttributes<HTMLButtonElement> & { children: ReactNode }) {
  return (
    <button type="button" {...props} className={`${BTN_BASE} ${props.className ?? ''}`} style={{ background: WIZARD_CTA_GRADIENT_DANGER }}>
      {children}
    </button>
  );
}

const BTN_SECUNDARIO = MANUAL_BTN_SECUNDARIO;

/** `code` del cuerpo de error del backend (`{ code, message }`), si lo hay. */
function codigoDeError(err: unknown): string | undefined {
  const body = err instanceof ApiError ? (err.body as { code?: unknown } | null | undefined) : undefined;
  return typeof body?.code === 'string' ? body.code : undefined;
}

/**
 * 409 `estado_invalido`: la validación ya no está pendiente (alguien más decidió o el cliente cambió algo) y el detalle se
 * refresca. 409 `tramite_inactivo` NO es eso: el trámite está anulado o revocado y la validación queda como estaba.
 */
function esEstadoCambiado(err: unknown): boolean {
  return err instanceof ApiError && err.status === 409 && codigoDeError(err) !== 'tramite_inactivo';
}

/** Mensaje claro para el Super Admin según el error del backend (409, 400 u otro). */
export function mensajeErrorAccion(err: unknown): string {
  if (err instanceof ApiError) {
    if (err.status === 409 && codigoDeError(err) === 'tramite_inactivo') {
      return 'El trámite está anulado o revocado: la validación se conserva sin cambios.';
    }
    if (err.status === 409) return 'Esta validación ya no está pendiente de revisión. Actualizamos el detalle.';
    if (err.status === 400) return 'El motivo elegido no es válido. Elige otro de la lista.';
  }
  return 'No se pudo completar la acción. Inténtalo de nuevo.';
}

export function ManualReviewActions({
  detail,
  client,
  onDone,
  onStale,
  onDialogChange,
}: {
  detail: ManualDetail;
  client: ManualReviewClient;
  /** La acción se aplicó: el padre refresca fila y detalle y muestra el mensaje. */
  onDone: (message: string) => void;
  /** El backend dijo que el estado ya cambió (409): el padre refresca el detalle. */
  onStale: () => void;
  /** Avisa si hay un diálogo de confirmación abierto (para que Escape no cierre también el detalle). */
  onDialogChange?: (open: boolean) => void;
}) {
  const [dialogo, setDialogo] = useState<'aprobar' | 'rechazar' | null>(null);

  useEffect(() => {
    onDialogChange?.(dialogo !== null);
  }, [dialogo, onDialogChange]);

  if (detail.status !== 'pendiente_revision_manual') return null;

  return (
    <>
      <div className="flex flex-wrap items-center justify-end gap-3" role="group" aria-label="Decisión sobre la validación">
        <DangerButton onClick={() => setDialogo('rechazar')}>
          <X className="h-4 w-4" aria-hidden /> Rechazar
        </DangerButton>
        <GradientButton onClick={() => setDialogo('aprobar')}>
          <Check className="h-4 w-4" aria-hidden /> Aprobar
        </GradientButton>
      </div>
      {dialogo === 'aprobar' && (
        <AprobarDialog
          detail={detail}
          client={client}
          onClose={() => setDialogo(null)}
          onDone={(m) => {
            setDialogo(null);
            onDone(m);
          }}
          onStale={() => {
            setDialogo(null);
            onStale();
          }}
        />
      )}
      {dialogo === 'rechazar' && (
        <RechazarDialog
          detail={detail}
          client={client}
          onClose={() => setDialogo(null)}
          onDone={(m) => {
            setDialogo(null);
            onDone(m);
          }}
          onStale={() => {
            setDialogo(null);
            onStale();
          }}
        />
      )}
    </>
  );
}

interface DialogoProps {
  detail: ManualDetail;
  client: ManualReviewClient;
  onClose: () => void;
  onDone: (message: string) => void;
  onStale: () => void;
}

function useAccion(onClose: () => void) {
  const panelRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(panelRef, { active: true });
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  return { panelRef, busy, setBusy, error, setError, close: () => !busy && onClose() };
}

function AprobarDialog({ detail, client, onClose, onDone, onStale }: DialogoProps) {
  const { panelRef, busy, setBusy, error, setError } = useAccion(onClose);

  const confirmar = async () => {
    setBusy(true);
    setError(null);
    try {
      await client.approveManual(detail.id);
      onDone('Aprobada por 30 días.');
    } catch (err) {
      setBusy(false);
      if (esEstadoCambiado(err)) {
        onStale();
        return;
      }
      setError(mensajeErrorAccion(err));
    }
  };

  return (
    <Modal open onClose={onClose} busy={busy} title="Aprobar validación" size="sm" zClassName="z-[110]">
      <div ref={panelRef} tabIndex={-1} className="flex flex-col gap-4 outline-none">
        <p className="text-sm">
          La validación de <strong>{detail.fullName}</strong> quedará vigente 30 días.
        </p>
        {error && (
          <p role="alert" className={`${MANUAL_AVISO_BASE} items-center font-medium ${MANUAL_AVISO.danger}`}>
            <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden /> {error}
          </p>
        )}
        <div className="flex justify-end gap-3">
          <button type="button" onClick={onClose} disabled={busy} className={BTN_SECUNDARIO}>
            Cancelar
          </button>
          <GradientButton onClick={() => void confirmar()} disabled={busy}>
            {busy ? 'Aprobando…' : 'Aprobar'}
          </GradientButton>
        </div>
      </div>
    </Modal>
  );
}

function RechazarDialog({ detail, client, onClose, onDone, onStale }: DialogoProps) {
  const { panelRef, busy, setBusy, error, setError } = useAccion(onClose);
  const [motivo, setMotivo] = useState('');
  const selectId = useId();

  const confirmar = async () => {
    if (!motivo) return;
    setBusy(true);
    setError(null);
    try {
      const res = await client.rejectManual(detail.id, motivo);
      onDone(
        res.emailEnviado === false
          ? 'Rechazada, pero el correo al cliente no pudo enviarse.'
          : 'Rechazada. El cliente recibirá el motivo por correo con un enlace nuevo.',
      );
    } catch (err) {
      setBusy(false);
      if (esEstadoCambiado(err)) {
        onStale();
        return;
      }
      setError(mensajeErrorAccion(err));
    }
  };

  return (
    <Modal open onClose={onClose} busy={busy} title="Rechazar validación" size="sm" zClassName="z-[110]">
      <div ref={panelRef} tabIndex={-1} className="flex flex-col gap-4 outline-none">
        <div>
          <label htmlFor={selectId} className={WIZARD_LABEL}>
            Motivo del rechazo
          </label>
          <select
            id={selectId}
            value={motivo}
            onChange={(e) => setMotivo(e.target.value)}
            disabled={busy}
            required
            className={`${WIZARD_SELECT} ${MANUAL_SELECT_CLASS} mt-1`}
          >
            <option value="">Elige un motivo</option>
            {MOTIVOS_RECHAZO_MANUAL.map((m) => (
              <option key={m.code} value={m.code}>
                {m.label}
              </option>
            ))}
          </select>
        </div>
        <p className="text-sm text-[#59677D] dark:text-white/70">
          El cliente recibirá este motivo por correo y podrá repetir la captura.
        </p>
        {error && (
          <p role="alert" className={`${MANUAL_AVISO_BASE} items-center font-medium ${MANUAL_AVISO.danger}`}>
            <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden /> {error}
          </p>
        )}
        <div className="flex justify-end gap-3">
          <button type="button" onClick={onClose} disabled={busy} className={BTN_SECUNDARIO}>
            Cancelar
          </button>
          <DangerButton onClick={() => void confirmar()} disabled={busy || !motivo}>
            {busy ? 'Rechazando…' : 'Rechazar'}
          </DangerButton>
        </div>
      </div>
    </Modal>
  );
}
