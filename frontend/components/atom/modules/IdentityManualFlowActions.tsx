'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { AlertCircle, AlertTriangle, ExternalLink, Link2, RefreshCw, Send } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { formatFechaHora } from '@/lib/format/date';
import {
  esRechazadoManual,
  esperaCapturaManual,
  puedeActivarFlujoManual,
  hayKyverumEnCurso,
  mensajeErrorFlujoManual,
  tieneDetalleManual,
  type AccionManual,
} from '@/lib/identity/manual-flow';
import { ActionsMenu, type ActionsMenuItem } from '@/components/atom/ActionsMenu';
import { WIZARD_CTA_GRADIENT } from '@/components/operacion/wizard-field-styles';
import { StatusBadge } from '@/components/atom/StatusBadge';
import type { BiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #13288 (Feature #13280, Épica #13202) — acciones del flujo manual de identidad en el detalle de una
 * validación, SOLO para Super Admin (el backend además responde 403): «Activar flujo manual» (si la
 * validación no está aprobada y vigente) y, con el flujo manual activo, chip «Flujo manual activo» con la
 * fecha de vencimiento del enlace y «Regenerar enlace». Un solo paso de confirmación en modal normal.
 */

const CHIP_RECHAZADA = 'Rechazada · esperando nueva captura';

const AVISO_CORREO =
  'No se pudo enviar el correo al titular. Regenera el enlace cuando tenga un correo válido.';

const BTN_BASE =
  'inline-flex items-center justify-center gap-1.5 rounded-full px-5 py-2 text-xs font-semibold transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:focus-visible:ring-[#7C9BFF] disabled:cursor-not-allowed disabled:opacity-70';
/** Acción secundaria navy del guardián (patrón «NavyButton»); en tema oscuro, translúcida sobre la tarjeta #162744. */
const NAVY_BTN = `${BTN_BASE} bg-[#162744] text-white hover:opacity-90 dark:border dark:border-white/15 dark:bg-white/10 dark:hover:bg-white/15`;
/** Secundario «blanco con borde» del guardián. */
const GHOST_BTN = `${BTN_BASE} border border-[#DFE5ED] bg-white text-[#162744] hover:bg-[#F4F8FF] dark:border-white/15 dark:bg-transparent dark:text-white dark:hover:bg-white/5`;
/** CTA principal del modal: degradado primario (token gradient.primary), con el mismo valor de WIZARD_CTA_GRADIENT. */
const CTA_BTN = `${BTN_BASE} text-white hover:opacity-90`;
const AVISO_CLS = 'flex items-start gap-2 rounded-[10px] border text-xs';

export interface IdentityManualFlowActionsProps {
  validation: BiometricValidation;
  /** Se llama tras una acción exitosa para que el detalle recargue su estado en sitio. */
  onChanged: () => void;
  /**
   * Acceso directo al detalle de ESTA validación en la pestaña «Validaciones manuales» (solo Super Admin y solo
   * si la validación es manual). Quien lo provee cierra el detalle y navega por enlace profundo.
   */
  onVerEnManuales?: (validationId: string) => void;
}

export function IdentityManualFlowActions({ validation: v, onChanged, onVerEnManuales }: IdentityManualFlowActionsProps) {
  const [esSuperAdmin] = useState(() => isSuperAdmin(decodeJwtPayload(getToken())));
  const [dialog, setDialog] = useState<AccionManual | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [correoFallo, setCorreoFallo] = useState(false);
  const regionRef = useRef<HTMLDivElement>(null);

  const esperaCaptura = esperaCapturaManual(v);
  const rechazada = esRechazadoManual(v);
  // Regla en lib/identity/manual-flow.ts: sin «Activar» sobre estados manuales (salvo rechazada con enlace vencido).
  const puedeActivar = puedeActivarFlujoManual(v);

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
  if (esperaCaptura) {
    items.push({ key: 'regenerar', label: 'Regenerar enlace', icon: RefreshCw, onSelect: () => openDialog('regenerar') });
  }
  const verEnManuales = onVerEnManuales && tieneDetalleManual(v) ? onVerEnManuales : undefined;
  if (items.length === 0 && !correoFallo && !verEnManuales) return null;

  return (
    <div ref={regionRef} tabIndex={-1} className="space-y-2 outline-none" data-testid="identity-manual-flow">
      <div className="flex flex-wrap items-center gap-2">
        {esperaCaptura && (
          <>
            <StatusBadge
              label={
                <span className="inline-flex items-center gap-1.5">
                  {rechazada ? (
                    <AlertCircle className="h-3.5 w-3.5" aria-hidden="true" />
                  ) : (
                    <Link2 className="h-3.5 w-3.5" aria-hidden="true" />
                  )}
                  {rechazada ? CHIP_RECHAZADA : 'Flujo manual activo'}
                </span>
              }
              tone={rechazada ? 'danger' : 'info'}
              ariaLabel={rechazada ? CHIP_RECHAZADA : 'Flujo manual activo'}
            />
            {v.expiresAt && (
              <span className="text-xs text-[#162744] dark:text-white/80">
                El enlace vence: <span className="font-semibold">{formatFechaHora(v.expiresAt)}</span>
              </span>
            )}
          </>
        )}
        <div className="ml-auto flex flex-wrap items-center justify-end gap-2">
          {verEnManuales && (
            <button type="button" className={GHOST_BTN} onClick={() => verEnManuales(v.id)}>
              <ExternalLink className="h-3.5 w-3.5" aria-hidden="true" />
              Ver en validaciones manuales
            </button>
          )}
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
          className={`${AVISO_CLS} p-3`}
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
      className="fixed inset-0 z-[110] flex items-center justify-center px-4 py-6"
      style={{ background: 'rgba(22,39,68,0.45)', backdropFilter: 'blur(6px)' }}
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
        className="w-full max-w-md rounded-[18px] border border-[#DFE5ED] bg-white p-5 text-[#162744] shadow-2xl dark:border-white/10 dark:bg-[#162744] dark:text-white"
      >
        <h2 id={titleId} className="text-base font-bold">
          {titulo}
        </h2>
        <div id={descId} className="mt-3 space-y-2 text-sm text-[#162744] dark:text-white/80">
          <p>{texto}</p>
          {kyverumEnCurso && (
            <p
              className={`${AVISO_CLS} p-2.5 font-semibold`}
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
            className={`mt-3 ${AVISO_CLS} p-2.5`}
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
          <button
            type="button"
            className={CTA_BTN}
            style={{ background: WIZARD_CTA_GRADIENT }}
            onClick={onConfirm}
            disabled={busy}
            aria-busy={busy}
          >
            {busy ? 'Enviando…' : 'Confirmar'}
          </button>
        </div>
      </div>
    </div>,
    document.body,
  );
}
