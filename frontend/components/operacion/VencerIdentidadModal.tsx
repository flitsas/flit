'use client';

import { type ReactNode, useState } from 'react';
import { CalendarX, Copy } from 'lucide-react';
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
 * muestra el resultado: enlace nuevo copiable, envío en cola o el motivo por el que no salió enlace.
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

/** Con el proveedor mock el enlace viene relativo (`/biometric/{token}`): se vuelve absoluto. */
export function enlaceAbsoluto(url: string): string {
  return url.startsWith('/') ? `${window.location.origin}${url}` : url;
}

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
      className="flex-1 rounded-xl border py-2.5 text-sm font-medium text-flit-primary transition hover:bg-[#162744]/[0.04] disabled:cursor-not-allowed disabled:opacity-60 dark:border-white/20 dark:text-white"
      style={{ borderColor: '#DFE5ED' }}
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
  const [copiado, setCopiado] = useState(false);

  const enlace = resultado?.enlaceCaptura ? enlaceAbsoluto(resultado.enlaceCaptura) : null;

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

  const copiar = async () => {
    if (!enlace) return;
    try {
      await navigator.clipboard.writeText(enlace);
      setCopiado(true);
    } catch {
      setError('No se pudo copiar el enlace. Selecciónalo y cópialo a mano.');
    }
  };

  return (
    <Modal
      open
      onClose={onClose}
      title="Vencer identidad"
      titleClassName="text-base font-bold text-flit-brand"
      icon={CalendarX}
      description={row.name}
      size="sm"
      busy={busy}
    >
      {/* `contents`: detiene la propagación del clic sin aportar layout (el portal burbuja por React). */}
      <div className="contents" onClick={(e) => e.stopPropagation()}>
        {resultado ? (
          <div className="space-y-3">
            <div role="status" aria-live="polite" className="space-y-3">
              <InlineAlert tone={resultado.enlaceEnviado && enlace ? 'success' : 'warning'}>
                {mensajeResultado(resultado)}
              </InlineAlert>
              {enlace ? (
                <div className="space-y-2">
                  <label
                    className="block text-xs font-semibold text-flit-primary dark:text-white"
                    htmlFor="vencer-identidad-enlace"
                  >
                    Enlace nuevo
                  </label>
                  <input
                    id="vencer-identidad-enlace"
                    readOnly
                    value={enlace}
                    onFocus={(e) => e.currentTarget.select()}
                    className="w-full rounded-xl border px-3 py-2 text-xs text-flit-primary dark:border-white/15 dark:bg-transparent dark:text-white"
                    style={{ borderColor: '#DFE5ED' }}
                  />
                  <button
                    type="button"
                    onClick={() => void copiar()}
                    className="inline-flex items-center gap-1.5 text-xs font-semibold text-flit-brand underline-offset-2 hover:underline"
                  >
                    <Copy className="h-3.5 w-3.5" aria-hidden="true" />
                    Copiar enlace
                  </button>
                  {copiado ? (
                    <p className="text-xs font-semibold" style={{ color: '#8CC63F' }}>
                      Enlace copiado.
                    </p>
                  ) : null}
                </div>
              ) : null}
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
            <p className="text-sm text-flit-primary dark:text-white">
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
