'use client';

import { formatFechaHora } from '@/lib/format/date';
import { canalLabel } from '@/lib/tramites/tramites-row-labels';
import type { InstanceSummary, ProcedureInstanceDetail } from '@/lib/api/types/procedure-runtime';
import { DETALLE_CARD, DETALLE_META, DETALLE_NAVY, DETALLE_BORDER } from './detalle-visual';

/**
 * Columna vehículo 4/12 — card con imagen placeholder (spec flit-detalle-tramite).
 */
/**
 * HU #14322 — `canal` es el detalle ya cargado del trámite. `null` (aún cargando o con error) pinta
 * «—»: no se asume «Trámites» hasta saber que el backend no trae el dato.
 */
export function DetalleVehiculoSidebar({
  item,
  canal = null,
}: {
  item: InstanceSummary;
  canal?: Pick<
    ProcedureInstanceDetail,
    'channel' | 'channelTransitOfficeName'
  > | null;
}) {
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
        <p>Creado: {formatFechaHora(item.createdAt)}</p>
        <p>Actualizado: {item.updatedAt ? formatFechaHora(item.updatedAt) : '—'}</p>
        {/* HU #14322 — solo lectura: texto, sin control para cambiarlo (ningún rol lo edita). */}
        <p data-testid="detalle-canal">
          Canal: <span className="font-semibold">{canal ? canalLabel(canal.channel) : '—'}</span>
        </p>
        {canal?.channel === 'pn_punto' && canal.channelTransitOfficeName ? (
          <p data-testid="detalle-canal-ot">
            OT del punto: <span className="font-semibold">{canal.channelTransitOfficeName}</span>
          </p>
        ) : null}
      </div>
    </aside>
  );
}
