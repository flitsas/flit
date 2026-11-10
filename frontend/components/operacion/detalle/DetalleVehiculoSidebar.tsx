'use client';

import { Placa } from '@flit/ui/Placa';
import { formatFechaHora } from '@/lib/format/date';
import { canalLabel } from '@/lib/tramites/tramites-row-labels';
import type { InstanceSummary, ProcedureInstanceDetail } from '@/lib/api/types/procedure-runtime';
import { DETALLE_CARD } from './detalle-visual';

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
      {/* Propuesta: panel cielo con la ilustración del trámite y la placa amarilla en grande. Sigue
          siendo un `role="img"` con el mismo nombre accesible: lo de dentro es presentación. */}
      <div
        className="relative grid aspect-[4/3] w-full place-items-center overflow-hidden rounded-xl"
        style={{
          background:
            'url("/assets/ilustraciones/traspaso.webp") right 8% top 10% / 34% auto no-repeat, var(--di-sky)',
        }}
        role="img"
        aria-label={`Vehículo con placa ${item.placa ?? 'sin placa'}`}
      >
        {item.placa ? (
          <Placa placa={item.placa} size="lg" aria-hidden="true" />
        ) : (
          <span className="di-placa-vacia" aria-hidden="true">
            Sin placa
          </span>
        )}
      </div>
      <div className="mt-3 grid gap-1">
        <p className="di-antetitulo text-center">Vehículo</p>
        <p className="text-center text-sm font-semibold" style={{ color: 'var(--di-heading)' }}>
          Placa:{' '}
          <span className="font-bold uppercase tracking-wider">{item.placa || '—'}</span>
          <span className="mx-2 opacity-40">|</span>
          Modelo: <span className="font-bold">{modelo}</span>
        </p>
        <p className="text-center font-mono text-[11px]" style={{ color: 'var(--di-text-secondary)' }}>
          VIN: {item.vin ?? '—'}
        </p>
      </div>
      <div
        className="mt-3 grid gap-1 rounded-xl px-3.5 py-3 text-[12px]"
        style={{ color: 'var(--di-text-secondary)', background: 'var(--di-sky)' }}
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
