'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { AlertCircle, AlertTriangle, Link2, RefreshCw, Send } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { formatFechaHora } from '@/lib/format/date';
import {
  esAprobadaVigente,
  esFlujoManualActivo,
  hayKyverumEnCurso,
  mensajeErrorFlujoManual,
  type AccionManual,
} from '@/lib/identity/manual-flow';
import { ActionsMenu, type ActionsMenuItem } from '@/components/atom/ActionsMenu';
import { StatusBadge } from '@/components/atom/StatusBadge';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #13288 (Feature #13280, Épica #13202) — acciones del flujo manual de identidad en el detalle de una
 * validación, SOLO para Super Admin (el backend además responde 403): «Activar flujo manual» (si la
 * validación no está aprobada y vigente) y, con el flujo manual activo, chip «Flujo manual activo» con la
 * fecha de vencimiento del enlace y «Regenerar enlace». Un solo paso de confirmación en modal normal.
 */

const AVISO_CORREO =
  'No se pudo enviar el correo al titular. Regenera el enlace cuando tenga un correo válido.';

const NAVY_BTN =
  'inline-flex items-center justify-center gap-1.5 rounded-xl bg-[#162744] px-4 py-2 text-xs font-semibold text-white transition hover:opacity-90 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] disabled:cursor-not-allowed disabled:opacity-60';
const GHOST_BTN =
  'inline-flex items-center justify-center rounded-xl border border-[#DFE5ED] bg-white px-4 py-2 text-xs font-semibold text-[#162744] transition hover:bg-[#557EFF]/10 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] disabled:cursor-not-allowed disabled:opacity-60';

export interface IdentityManualFlowActionsProps {
  validation: BiometricValidation;
  /** Se llama tras una acción exitosa para que el detalle recargue su estado en sitio. */
  onChanged: () => void;
}

export function IdentityManualFlowActions({ validation: v, onChanged }: IdentityManualFlowActionsProps) {
  const [esSuperAdmin] = useState(() => isSuperAdmin(decodeJwtPayload(getToken())));
  const [dialog, setDialog] = useState<AccionManual | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [correoFallo, setCorreoFallo] = useState(false);
  const regionRef = useRef<HTMLDivElement>(null);

  const manualActivo = esFlujoManualActivo(v);
  // En los estados manuales no se ofrece «Activar» de nuevo: reactivaría el ciclo y descartaría la captura.
  const enFlujoManual =
    v.provider === 'manual' || v.status === 'manual_activo' || v.status === 'pendiente_revision_manual';
  const puedeActivar = !esAprobadaVigente(v) && !enFlujoManual;

  function openDialog(accion: AccionManual) {
    setError(null);
    setDialog(accion);
  }

  const closeDialog = useCallback(() => {
    setDialog(null);
    setError(null);
  }, []);

  async function confirm() {
    if (!dialog || busy) return;
    setBusy(true);
    setError(null);
    try {
      const res =
        dialog === 'activar'
          ? await tramitesClient.activateManualIdentity(v.id)
          : await tramitesClient.regenerateManualLink(v.id);
      setCorreoFallo(res.emailEnviado === false);
      setDialog(null);
      onChanged();
    } catch (err) {
      setError(mensajeErrorFlujoManual(err, dialog));
    } finally {
      setBusy(false);
    }
  }

  if (!esSuperAdmin) return null;

  const items: ActionsMenuItem[] = [];
  if (puedeActivar) {
    items.push({ key: 'activar', label: 'Activar flujo manual', icon: Send, onSelect: () => openDialog('activar') });
  }
  if (manualActivo) {
    items.push({ key: 'regenerar', label: 'Regenerar enlace', icon: RefreshCw, onSelect: () => openDialog('regenerar') });
  }
  if (items.length === 0 && !correoFallo) return null;

  return (
    <div ref={regionRef} tabIndex={-1} className="space-y-2 outline-none" data-testid="identity-manual-flow">
      <div className="flex flex-wrap items-center gap-2">
        {manualActivo && (
          <>
            <StatusBadge
              label={
                <span className="inline-flex items-center gap-1.5">
                  <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
                  Flujo manual activo
                </span>
              }
              tone="info"
              ariaLabel="Flujo manual activo"
            />
            <span className="text-xs">
              El enlace vence: <span className="font-semibold">{formatFechaHora(v.expiresAt)}</span>
            </span>
          </>
        )}
        <div className="ml-auto">
          {items.length > 1 ? (
            <ActionsMenu items={items} ariaLabel="Acciones del flujo manual de identidad" />
          ) : items.length === 1 ? (
            <button type="button" className={NAVY_BTN} onClick={items[0].onSelect}>
              {items[0].label}
            </button>
          ) : null}
        </div>
      </div>

      {correoFallo && (
        <div
          role="alert"
          className="flex items-start gap-2 rounded-xl border p-3 text-xs"
          style={{
            borderColor: 'var(--badge-warning-border)',
            background: 'var(--badge-warning-bg)',
            color: 'var(--badge-warning-fg)',
          }}
        >
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
          <p>{AVISO_CORREO}</p>
        </div>
      )}

      {dialog && (
        <ManualFlowConfirmDialog
          accion={dialog}
          kyverumEnCurso={dialog === 'activar' && hayKyverumEnCurso(v)}
          busy={busy}
          error={error}
          onConfirm={() => void confirm()}
          onClose={closeDialog}
          fallbackFocusRef={regionRef}
        />
      )}
    </div>
  );
}

interface DialogProps {
  accion: AccionManual;
  kyverumEnCurso: boolean;
  busy: boolean;
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
  fallbackFocusRef: React.RefObject<HTMLElement | null>;
}

const FOCUSABLE =
  'button:not([disabled]), [href], input:not([disabled]), select:not([disabled]), textarea:not([disabled]), [tabindex]:not([tabindex="-1"])';

/** Modal normal (no lateral): role="dialog", aria-modal, focus trap, Escape y retorno de foco. */
function ManualFlowConfirmDialog({
  accion,
  kyverumEnCurso,
  busy,
  error,
  onConfirm,
  onClose,
  fallbackFocusRef,
}: DialogProps) {
  const titleId = useId();
  const descId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const busyRef = useRef(busy);
  const onCloseRef = useRef(onClose);
  useEffect(() => {
    busyRef.current = busy;
    onCloseRef.current = onClose;
  });

  useEffect(() => {
    const previo = document.activeElement as HTMLElement | null;
    panelRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus();

    // Escape: se corta la propagación para que el panel de detalle que contiene este modal NO se cierre.
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        if (!busyRef.current) onCloseRef.current();
        return;
      }
      if (e.key !== 'Tab' || !panelRef.current) return;
      const nodes = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE));
      if (nodes.length === 0) return;
      const firstEl = nodes[0];
      const lastEl = nodes[nodes.length - 1];
      const active = document.activeElement;
      if (e.shiftKey && (active === firstEl || !panelRef.current.contains(active))) {
        e.preventDefault();
        lastEl.focus();
      } else if (!e.shiftKey && (active === lastEl || !panelRef.current.contains(active))) {
        e.preventDefault();
        firstEl.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    const regionEl = fallbackFocusRef.current;
    return () => {
      document.removeEventListener('keydown', onKey);
      // Retorno de foco: al disparador; si desapareció (el estado cambió), a la región de acciones.
      if (previo && previo.isConnected) previo.focus();
      else regionEl?.focus();
    };
  }, [fallbackFocusRef]);

  if (typeof document === 'undefined') return null;

  const titulo = accion === 'activar' ? 'Activar flujo manual' : 'Regenerar enlace';
  const texto =
    accion === 'activar'
      ? 'Se enviará al cliente un enlace de captura por correo (vale 24 horas).'
      : 'El enlace anterior dejará de funcionar y se enviará uno nuevo por correo.';

  return createPortal(
    <div
      className="fixed inset-0 z-[110] flex items-center justify-center bg-slate-900/50 px-4 py-6 backdrop-blur-md"
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
      onClick={(e) => e.stopPropagation()}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descId}
        className="w-full max-w-md rounded-2xl border border-[#DFE5ED] bg-white p-5 text-[#162744] shadow-2xl dark:border-white/10 dark:bg-[#0B0F14] dark:text-white"
      >
        <h2 id={titleId} className="text-base font-bold">
          {titulo}
        </h2>
        <div id={descId} className="mt-3 space-y-2 text-sm">
          <p>{texto}</p>
          {kyverumEnCurso && (
            <p
              className="flex items-start gap-2 rounded-xl border p-2.5 text-xs font-semibold"
              style={{
                borderColor: 'var(--badge-warning-border)',
                background: 'var(--badge-warning-bg)',
                color: 'var(--badge-warning-fg)',
              }}
            >
              <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
              Se cancelará la validación de Kyverum en curso
            </p>
          )}
        </div>
        {error && (
          <div
            role="alert"
            className="mt-3 flex items-start gap-2 rounded-xl border p-2.5 text-xs"
            style={{
              borderColor: 'var(--badge-danger-border)',
              background: 'var(--badge-danger-bg)',
              color: 'var(--badge-danger-fg)',
            }}
          >
            <AlertCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden="true" />
            <p>{error}</p>
          </div>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className={GHOST_BTN} onClick={onClose} disabled={busy}>
            Cancelar
          </button>
          <button type="button" className={NAVY_BTN} onClick={onConfirm} disabled={busy} aria-busy={busy}>
            {busy ? 'Enviando…' : 'Confirmar'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
