'use client';

import { formatPesos } from '@/lib/api/admin-pn-tarifas';
import { formatFechaHora } from '@/lib/format/date';
import type { PagoPortal } from '@/lib/api/types/procedure-runtime';
import { DETALLE_NAVY } from './detalle-visual';

const MEDIOS: Record<string, string> = {
  efectivo: 'Efectivo',
  datafono: 'Datáfono',
  transferencia: 'Transferencia',
};

/** HU #14470 — medio de pago legible; `otro` añade el detalle si viene. */
export function medioPagoLabel(
  pago: Pick<PagoPortal, 'medio' | 'medioCaja' | 'medioDetalle'>,
): string {
  if (pago.medio === 'sin_cobro' || !pago.medioCaja) return '—';
  if (pago.medioCaja === 'otro') {
    const detalle = pago.medioDetalle?.trim();
    return detalle ? `Otro: ${detalle}` : 'Otro';
  }
  return MEDIOS[pago.medioCaja] ?? '—';
}

/**
 * HU #14470 — «Pagado en caja» con valor, medio y fecha. No renderiza nada si el trámite no tiene
 * pago del portal (AC2).
 */
export function PagoPortalBloque({ pago }: { pago?: PagoPortal | null }) {
  if (!pago) return null;
  const sinCobro = pago.medio === 'sin_cobro';
  return (
    <section
      className='mt-3 rounded-xl border border-[#DFE5ED] p-3 text-[11px] dark:border-white/5'
      aria-label='Pago en caja'
      data-testid='detalle-pago-portal'
    >
      <p className='text-sm font-semibold' style={{ color: DETALLE_NAVY }}>
        {sinCobro ? 'Sin cobro' : 'Pagado en caja'}
      </p>
      <dl className='mt-1 space-y-0.5'>
        <div className='flex gap-1'>
          <dt>Valor:</dt>
          <dd className='font-semibold'>{formatPesos(pago.valorCop)}</dd>
        </div>
        {sinCobro ? null : (
          <div className='flex gap-1'>
            <dt>Medio:</dt>
            <dd className='font-semibold'>{medioPagoLabel(pago)}</dd>
          </div>
        )}
        <div className='flex gap-1'>
          <dt>Fecha:</dt>
          <dd className='font-semibold'>{formatFechaHora(pago.pagadoEn)}</dd>
        </div>
      </dl>
    </section>
  );
}
