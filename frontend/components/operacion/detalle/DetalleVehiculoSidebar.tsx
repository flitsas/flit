'use client';

import { CarFront } from 'lucide-react';
import { formatFecha } from '@/lib/format/date';
import type { InstanceSummary } from '@/lib/api/types/procedure-runtime';

const NAVY = '#162744';

const CARD =
  'h-full rounded-[18px] border bg-white p-4 dark:bg-[#162744] border-[#DFE5ED] dark:border-white/10';

/**
 * Columna izquierda del modal Ver (mockup): placeholder de imagen + placa/modelo/VIN/fechas.
 * Sin URL de foto en el contrato — placeholder estático FE.
 */
export function DetalleVehiculoSidebar({ item }: { item: InstanceSummary }) {
  const modelo = [item.vehiculoMarca, item.vehiculoLinea].filter(Boolean).join(' ') || '—';

  return (
    <aside className={CARD} aria-label="Datos del vehículo">
      <div
        className="flex aspect-[4/3] w-full items-center justify-center rounded-xl bg-[#DFE5ED]/40 dark:bg-white/5"
        role="img"
        aria-label={`Vehículo con placa ${item.placa ?? 'sin placa'}`}
      >
        <CarFront className="h-16 w-16 opacity-30" style={{ color: NAVY }} aria-hidden="true" />
      </div>
      <div className="mt-3 text-center">
        <p className="text-sm font-semibold" style={{ color: NAVY }}>
          Placa:{' '}
          <span className="font-mono font-bold uppercase tracking-wider">{item.placa || '—'}</span>
          <span className="mx-2 opacity-40">|</span>
          Modelo: <span className="font-bold">{modelo}</span>
        </p>
        <p className="mt-1 font-mono text-[11px] opacity-70">VIN: {item.vin ?? '—'}</p>
      </div>
      <div
        className="mt-3 border-t pt-3 text-[11px] opacity-70 border-[#DFE5ED] dark:border-white/10"
        style={{ color: '#5E6A7B' }}
      >
        <p>Creado: {formatFecha(item.createdAt)}</p>
        <p>Actualizado: {item.updatedAt ? formatFecha(item.updatedAt) : '—'}</p>
      </div>
    </aside>
  );
}
