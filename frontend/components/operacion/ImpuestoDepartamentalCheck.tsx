'use client';

import { useEffect, useId, useState } from 'react';
import { StatusBadge } from '@/components/atom/StatusBadge';
import { tramitesClient } from '@/lib/api/tramites-client';
import {
  esImpuestoPagadoPorFlito,
  ETIQUETA_IMPUESTO_PAGADO_FLITO,
  MOTIVO_IMPUESTO_PAGADO_FLITO,
} from '@/lib/tramites/flito';

/**
 * HU #13266 (AC2) — de quién es la marca `impuesto_departamental_pagado` del trámite:
 * - `cargando`: se está leyendo el detalle del trámite;
 * - `flito`: FLITO la marcó al adjuntar el comprobante (HU #13264) — el check va fijo;
 * - `gestor`: no hay marca de FLITO, el gestor decide;
 * - `error`: no se pudo leer — el gestor decide igual (el backend conserva la marca de FLITO).
 */
export type EstadoImpuestoFlito = 'cargando' | 'flito' | 'gestor' | 'error';

export const COPY_IMPUESTO_FLITO_CARGANDO = 'Verificando si el pago del impuesto ya quedó registrado…';
export const COPY_IMPUESTO_FLITO_ERROR =
  'No se pudo verificar si el pago del impuesto ya quedó registrado. Si lo está, se conserva al enviar.';

/**
 * Lee el detalle del trámite (`GET /instances/{id}`, cuyos `fieldValues` traen `source`) y dice si el
 * impuesto lo marcó FLITO. `instanceId` null deja el hook inerte.
 */
export function useImpuestoPagadoPorFlito(
  instanceId: string | null,
  tenantId?: string,
): EstadoImpuestoFlito {
  const [lectura, setLectura] = useState<{ instanceId: string; estado: EstadoImpuestoFlito } | null>(null);

  useEffect(() => {
    if (!instanceId) return;
    let activo = true;
    // `Promise.resolve().then` convierte una excepción síncrona del cliente en rechazo: nunca tumba el modal.
    void Promise.resolve()
      .then(() => tramitesClient.getInstance(instanceId, tenantId))
      .then((detalle) => {
        if (activo) {
          setLectura({
            instanceId,
            estado: esImpuestoPagadoPorFlito(detalle?.fieldValues) ? 'flito' : 'gestor',
          });
        }
      })
      .catch(() => {
        if (activo) setLectura({ instanceId, estado: 'error' });
      });
    return () => {
      activo = false;
    };
  }, [instanceId, tenantId]);

  if (!instanceId) return 'gestor';
  // Lectura de otro trámite (o ninguna aún): se está consultando el actual.
  return lectura?.instanceId === instanceId ? lectura.estado : 'cargando';
}

/**
 * Check «Impuesto departamental pagado» del modal «Enviar al OT».
 *
 * Con la marca de FLITO el check sale marcado y deshabilitado, con la etiqueta «Pagado (comprobante cargado)» (badge
 * `success`, mismo componente que los estados) y el motivo enlazado por `aria-describedby` (AC4): el
 * lector de pantalla anuncia por qué no se puede desmarcar y el estado no depende solo del color.
 */
export function ImpuestoDepartamentalCheck({
  estado,
  checked,
  onChange,
  disabled = false,
}: {
  estado: EstadoImpuestoFlito;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
}) {
  const ayudaId = useId();
  const deFlito = estado === 'flito';
  const cargando = estado === 'cargando';
  const ayuda = deFlito
    ? MOTIVO_IMPUESTO_PAGADO_FLITO
    : cargando
      ? COPY_IMPUESTO_FLITO_CARGANDO
      : estado === 'error'
        ? COPY_IMPUESTO_FLITO_ERROR
        : null;

  return (
    <div>
      <label
        className={`flex items-center gap-2 text-sm ${deFlito || cargando || disabled ? 'cursor-not-allowed' : 'cursor-pointer'}`}
      >
        <input
          type="checkbox"
          className="h-4 w-4 accent-[#557EFF]"
          checked={deFlito || checked}
          onChange={(e) => onChange(e.target.checked)}
          disabled={disabled || deFlito || cargando}
          aria-describedby={ayuda ? ayudaId : undefined}
          aria-busy={cargando || undefined}
        />
        Impuesto departamental pagado
        {deFlito && (
          <StatusBadge
            tone="success"
            label={ETIQUETA_IMPUESTO_PAGADO_FLITO}
            ariaLabel={ETIQUETA_IMPUESTO_PAGADO_FLITO}
          />
        )}
      </label>
      {/* Siempre montado: una región viva que aparece junto con su texto no se anuncia. Así el paso
          de «Verificando…» a «marcado por FLITO» llega al lector de pantalla. */}
      <p
        id={ayudaId}
        className={`ml-6 text-xs leading-snug opacity-80 ${ayuda ? 'mt-0.5' : ''}`}
        aria-live="polite"
      >
        {ayuda}
      </p>
    </div>
  );
}
