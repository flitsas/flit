'use client';

import { formatFecha } from '@/lib/format/date';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';
import { DETALLE_CARD, DETALLE_META, DETALLE_NAVY, DETALLE_BORDER } from './detalle-visual';

/**
 * Columna vehículo 4/12 — card con imagen placeholder (spec flit-detalle-tramite).
 */
export function DetalleVehiculoSidebar({ item }: { item: InstanceSummary }) {
  const modelo = [item.vehiculoMarca, item.vehiculoLinea].filter(Boolean).join(' ') || '—';

  return (
    <aside className={`${DETALLE_CARD} h-full`} aria-label="Datos del vehículo">
      <div
        className="aspect-[4/3] w-full rounded-xl bg-gradient-to-br from-[#DFE5ED]/60 to-[#DFE5ED]/20 dark:from-white/10 dark:to-white/5"
        role="img"
        aria-label={`Vehículo con placa ${item.placa ?? 'sin placa'}`}
      />
      <div className="mt-3 text-center">
        <p className="text-sm font-semibold" style={{ color: DETALLE_NAVY }}>
          Placa:{' '}
          <span className="font-bold uppercase tracking-wider">{item.placa || '—'}</span>
          <span className="mx-2 opacity-40">|</span>
          Modelo: <span className="font-bold">{modelo}</span>
        </p>
        <p className="mt-1 font-mono text-[11px] opacity-70">VIN: {item.vin ?? '—'}</p>
      </div>
      <div
        className="mt-3 border-t pt-3 text-[11px] border-[#DFE5ED] dark:border-white/5"
        style={{ color: DETALLE_META }}
      >
        <p>Creado: {formatFecha(item.createdAt)}</p>
        <p>Actualizado: {item.updatedAt ? formatFecha(item.updatedAt) : '—'}</p>
      </div>
    </aside>
  );
}
