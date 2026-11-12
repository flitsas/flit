'use client';

import { useCallback, useEffect, useId, useRef, useState } from 'react';
import { createPortal } from 'react-dom';
import { Icono } from "@flit/ui/Icono";
import { useToast } from '@/components/admin/Toast';
import { tramitesClient } from '@/lib/api/tramites-client';
import { buttonClass } from '@flit/ui/Button';
import {
  DIALOGO_FRANJA,
  DIALOGO_FRANJA_CIRCULO,
  DIALOGO_FRANJA_TITULO,
  DIALOGO_PANEL,
} from '@/components/operacion/dialogo-propio';
import { MODAL_VELO_CLS } from "@flit/ui/Modal";
import { mensajeErrorFlujoManual, motivoFalloCopiaEnlace } from '@/lib/identity/manual-flow';

/**
 * HU #14306 (Feature #14304, Épica #14271) — «Copiar enlace» en la fila de validaciones manuales (solo Super Admin).
 * El backend entrega el enlace vigente; aquí solo pasa del backend al portapapeles: nunca se guarda en estado, URL,
 * logs ni consola. Con enlace vencido o no recuperable se explica el motivo y se ofrece «Regenerar enlace»
 * (mismo endpoint que el detalle). Si el portapapeles falla, mensaje claro sin detalle técnico.
 */

const MSG_COPIADO = 'Enlace copiado';
const MSG_PORTAPAPELES = 'No se pudo copiar el enlace. Inténtalo de nuevo.';

const FOCUSABLE = 'button:not([disabled]), [href], input:not([disabled]), [tabindex]:not([tabindex="-1"])';

interface Pendiente {
  id: string;
  mensaje: string;
}

export function useCopiarEnlaceManual(onRegenerado?: () => void) {
  const { show } = useToast();
  const [pendiente, setPendiente] = useState<Pendiente | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const copiar = useCallback(async (id: string) => {
    let url: string;
    try {
      ({ url } = await tramitesClient.copyManualLink(id));
    } catch (err) {
      const { motivo, mensaje } = motivoFalloCopiaEnlace(err);
      if (motivo === 'vencido' || motivo === 'no_disponible') {
        setError(null);
        setPendiente({ id, mensaje });
      } else {
        show(mensaje, 'error');
      }
      return;
    }
    try {
      await navigator.clipboard.writeText(url);
      show(MSG_COPIADO);
    } catch {
      show(MSG_PORTAPAPELES, 'error');
    }
  }, [show]);

  const cerrar = useCallback(() => {
    setPendiente(null);
    setError(null);
  }, []);

  const regenerar = useCallback(async () => {
    if (!pendiente || busy) return;
    setBusy(true);
    setError(null);
    try {
      await tramitesClient.regenerateManualLink(pendiente.id);
      setPendiente(null);
      show('Enlace regenerado y enviado por correo.');
      onRegenerado?.();
    } catch (err) {
      setError(mensajeErrorFlujoManual(err, 'regenerar'));
    } finally {
      setBusy(false);
    }
  }, [pendiente, busy, onRegenerado, show]);

  const region = pendiente ? (
    <RegenerarDialog mensaje={pendiente.mensaje} busy={busy} error={error} onConfirm={() => void regenerar()} onClose={cerrar} />
  ) : null;

  return { copiar, region };
}

function RegenerarDialog({
  mensaje,
  busy,
  error,
  onConfirm,
  onClose,
}: {
  mensaje: string;
  busy: boolean;
  error: string | null;
  onConfirm: () => void;
  onClose: () => void;
}) {
  const titleId = useId();
  const descId = useId();
  const panelRef = useRef<HTMLDivElement>(null);
  const busyRef = useRef(busy);
  const closeRef = useRef(onClose);
  useEffect(() => {
    busyRef.current = busy;
    closeRef.current = onClose;
  });

  useEffect(() => {
    const previo = document.activeElement as HTMLElement | null;
    panelRef.current?.querySelector<HTMLElement>(FOCUSABLE)?.focus();
    const onKey = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        e.stopPropagation();
        if (!busyRef.current) closeRef.current();
        return;
      }
      if (e.key !== 'Tab' || !panelRef.current) return;
      const nodes = Array.from(panelRef.current.querySelectorAll<HTMLElement>(FOCUSABLE));
      if (nodes.length === 0) return;
      const first = nodes[0];
      const last = nodes[nodes.length - 1];
      if (e.shiftKey && document.activeElement === first) {
        e.preventDefault();
        last.focus();
      } else if (!e.shiftKey && document.activeElement === last) {
        e.preventDefault();
        first.focus();
      }
    };
    document.addEventListener('keydown', onKey);
    return () => {
      document.removeEventListener('keydown', onKey);
      if (previo && previo.isConnected) previo.focus();
    };
  }, []);

  return createPortal(
    <div
      className={`fixed inset-0 z-[110] flex items-center justify-center px-4 py-6 ${MODAL_VELO_CLS}`}
      onMouseDown={(e) => {
        if (e.target === e.currentTarget && !busy) onClose();
      }}
    >
      <div
        ref={panelRef}
        role="dialog"
        aria-modal="true"
        aria-labelledby={titleId}
        aria-describedby={descId}
        className={`${DIALOGO_PANEL} w-full max-w-md`}
      >
        <div className={DIALOGO_FRANJA}>
          <span aria-hidden="true" className={DIALOGO_FRANJA_CIRCULO} />
          <h2 id={titleId} className={DIALOGO_FRANJA_TITULO}>
            No se pudo copiar el enlace
          </h2>
        </div>
        <div className="p-5">
        <p id={descId} className="text-sm text-flit-primary dark:text-foreground">
          {mensaje} Regenera uno nuevo: el anterior dejará de funcionar y se enviará el nuevo por correo.
        </p>
        {error && (
          <div
            role="alert"
            className="mt-3 flex items-start gap-2 rounded-[10px] border p-2.5 text-xs"
            style={{
              borderColor: 'var(--badge-danger-border)',
              background: 'var(--badge-danger-bg)',
              color: 'var(--badge-danger-fg)',
            }}
          >
            <Icono nombre="alerta" className="mt-0.5 h-4 w-4 shrink-0" />
            <p>{error}</p>
          </div>
        )}
        <div className="mt-5 flex justify-end gap-2">
          <button type="button" className={buttonClass({ variant: 'secondary', size: 'sm' })} onClick={onClose} disabled={busy}>
            Cancelar
          </button>
          <button
            type="button"
            className={buttonClass({ size: 'sm' })}
            onClick={onConfirm}
            disabled={busy}
            aria-busy={busy}
          >
            {busy ? 'Enviando…' : 'Regenerar enlace'}
          </button>
        </div>
        </div>
      </div>
    </div>,
    document.body,
  );
}
