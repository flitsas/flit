'use client';

import { type ReactNode, useState } from 'react';
import { Modal } from '@/components/atom/Modal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';
import {
  tramitesClient,
  TramitesApiError,
  type VencerIdentidadResult,
} from '@/lib/api/tramites-client';
import type { TenantBiometricValidation } from '@/lib/api/types/procedure-runtime';

/**
 * HU #13825 (Feature #13820, Épica #13817) — «Vencer» una identidad aprobada y vigente (solo Super
 * Admin). Confirma, llama a `POST /api/v1/tramites/biometric-validations/{id}/vencer` (HU #13824) y
 * muestra el resultado: envío del enlace nuevo por correo, envío en cola o el motivo por el que no salió.
 * Bug #14674: el enlace de captura nunca se muestra ni se copia en la UI.
 * La identidad queda vencida en todos los casos de éxito (200); el 409 se informa sin cerrar.
 */

export const MENSAJE_NO_APROBADA_VIGENTE =
  'Esta validación no está aprobada y vigente. No hay nada que vencer.';

const MOTIVOS_SIN_ENLACE: Record<string, string> = {
  validacion_en_curso: 'la persona ya tiene una validación en curso en el trámite',
  proveedor_error: 'el proveedor de identidad devolvió un error',
  proveedor_no_disponible: 'el proveedor de identidad no está disponible',
  datos_incompletos: 'faltan datos de la persona en el trámite',
  actor_requerido: 'el trámite no tiene a la persona registrada',
};

function mensajeResultado(res: VencerIdentidadResult): string {
  if (res.enlaceEnviado && res.enlaceCaptura) {
    return 'La identidad quedó vencida. Se envió un enlace nuevo para que la persona la vuelva a hacer.';
  }
  if (res.enCola) {
    return 'La identidad quedó vencida. El envío del enlace quedó en cola y se reintentará automáticamente.';
  }
  if (res.motivoSinEnlace === 'sin_borradores') {
    return 'La identidad quedó vencida. No hay trámites en borrador que la necesiten, no se envió enlace.';
  }
  const porQue = res.motivoSinEnlace ? MOTIVOS_SIN_ENLACE[res.motivoSinEnlace] : undefined;
  return `La identidad quedó vencida, pero el enlace nuevo no salió${porQue ? `: ${porQue}` : ''}.`;
}

function Boton({
  children,
  onClick,
  disabled,
  primario,
}: {
  children: ReactNode;
  onClick: () => void;
  disabled?: boolean;
  primario?: boolean;
}) {
  return primario ? (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white transition disabled:cursor-not-allowed disabled:opacity-60"
      style={{ background: WIZARD_CTA_GRADIENT }}
    >
      {children}
    </button>
  ) : (
    <button
      type="button"
      onClick={onClick}
      disabled={disabled}
      className="flex-1 rounded-xl border py-2.5 text-sm font-medium text-foreground transition hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-60"
      style={{ borderColor: 'var(--border)' }}
    >
      {children}
    </button>
  );
}

export interface VencerIdentidadModalProps {
  row: TenantBiometricValidation;
  onClose: () => void;
  /** Se llama apenas el backend confirma el vencimiento (refresca la grilla). */
  onVencida: () => void;
}

export function VencerIdentidadModal({ row, onClose, onVencida }: VencerIdentidadModalProps) {
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [resultado, setResultado] = useState<VencerIdentidadResult | null>(null);

  const confirmar = async () => {
    setBusy(true);
    setError(null);
    try {
      const res = await tramitesClient.vencerIdentidad(row.id);
      setResultado(res);
      onVencida();
    } catch (e) {
      if (e instanceof TramitesApiError && e.status === 409) {
        setError(MENSAJE_NO_APROBADA_VIGENTE);
      } else {
        setError(e instanceof Error ? e.message : 'No se pudo vencer la identidad. Inténtalo de nuevo.');
      }
    } finally {
      setBusy(false);
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Vencer identidad"
      titleClassName="text-base font-bold text-flit-brand"
      icono="vencido"
      description={row.name}
      size="sm"
      busy={busy}
    >
      {/* `contents`: detiene la propagación del clic sin aportar layout (el portal burbuja por React). */}
      <div className="contents" onClick={(e) => e.stopPropagation()}>
        {resultado ? (
          <div className="space-y-3">
            <div role="status" aria-live="polite" className="space-y-3">
              <InlineAlert tone={resultado.enlaceEnviado && resultado.enlaceCaptura ? 'success' : 'warning'}>
                {mensajeResultado(resultado)}
              </InlineAlert>
            </div>
            {error ? <InlineAlert tone="error">{error}</InlineAlert> : null}
            <div className="flex gap-3 pt-1">
              <Boton primario onClick={onClose}>
                Cerrar
              </Boton>
            </div>
          </div>
        ) : (
          <div className="space-y-3">
            <p className="text-sm text-foreground">
              La vigencia de esta identidad termina ahora y se envía un enlace nuevo para que la persona la
              vuelva a hacer. Los trámites en borrador que la usaban quedan sin firmar; los ya radicados
              conservan su firma.
            </p>
            <div aria-live="polite">
              {error ? <InlineAlert tone="error">{error}</InlineAlert> : null}
            </div>
            <div className="flex gap-3 pt-1">
              <Boton onClick={onClose} disabled={busy}>
                Cancelar
              </Boton>
              <Boton primario onClick={() => void confirmar()} disabled={busy}>
                {busy ? 'Venciendo…' : 'Vencer identidad'}
              </Boton>
            </div>
          </div>
        )}
      </div>
    </Modal>
  );
}
