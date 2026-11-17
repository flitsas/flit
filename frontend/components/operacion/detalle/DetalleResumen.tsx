'use client';

import type { ReactNode } from 'react';
import { Placa } from '@flit/ui/Placa';
import { formatFechaHora } from '@/lib/format/date';
import { AvisoDigitoPlacaOt } from '@/components/operacion/AvisoDigitoPlacaOt';
import { PLATE_PREFERRED_LAST_DIGIT_KEY } from '@/lib/tramites/plate-digit-preference';
import type { InstanceSummary, ProcedureInstanceDetail } from '@/lib/api/types/procedure-runtime';
import { DETALLE_CARD } from './detalle-visual';

/**
 * HU #14482 — bloque de resumen del detalle del trámite, con el esquema `.tramite-resumen` de la
 * propuesta (`vTramite`): arriba el vehículo y las fichas del trámite y, tras la línea divisoria, los
 * pasos de sección (`DetalleStepper`), que llegan por `pasos`. Reemplaza la columna
 * `DetalleVehiculoSidebar` (4/12) para que la sección activa use todo el ancho del popup. Solo datos
 * que el detalle ya tenía: nada del prototipo que nuestro modelo no traiga (color, «Sin multas»,
 * pastillas de SOAT/revisión). El estado lo dice el chip de la cabecera.
 *
 * Registro diferido 2026-11-09 (HU 3, traído de develop desde la antigua columna del vehículo) — de
 * `detalle.fieldValues` salen la placa que asignó el OT (`plate`), el dígito de preferencia del gestor y la
 * clase (en motos se compara el primer número): con ellos se pinta el aviso de coincidencia bajo el vehículo.
 */

function Ficha({ dt, children, testId }: { dt: string; children: ReactNode; testId?: string }) {
  return (
    <div data-testid={testId}>
      <dt className="di-fichas__dt">
        {dt}
        <span className="sr-only">: </span>
      </dt>
      <dd className="di-fichas__dd m-0">{children}</dd>
    </div>
  );
}

export function DetalleResumen({
  item,
  detalle = null,
  pasos,
}: {
  item: InstanceSummary;
  /** Detalle cargado; `null` mientras carga o con error (sin OT del punto). */
  detalle?:
    | (Pick<ProcedureInstanceDetail, 'channel' | 'channelTransitOfficeName'> & {
        fieldValues?: ProcedureInstanceDetail['fieldValues'];
      })
    | null;
  /** Navegador de pasos de sección, debajo de la línea divisoria. */
  pasos?: ReactNode;
}) {
  const nombre = [item.vehiculoMarca, item.vehiculoLinea].filter(Boolean).join(' ') || '—';
  const fieldValues = detalle?.fieldValues ?? [];
  const plateField = fieldValues.find((f) => f.fieldKey === 'plate');
  const preferredDigit =
    fieldValues.find((f) => f.fieldKey === PLATE_PREFERRED_LAST_DIGIT_KEY)?.valueText ?? '';
  const vehicleClass = fieldValues.find((f) => f.fieldKey === 'vehicle_class')?.valueText ?? '';

  return (
    <section className={`${DETALLE_CARD} p-5 max-sm:p-4`} aria-label="Resumen del trámite">
      <div className="grid items-center gap-6 lg:grid-cols-[minmax(0,1.15fr)_minmax(0,1fr)]">
        {/* Mismo nombre accesible que la antigua columna del vehículo. */}
        <div className="di-vehiculo" aria-label="Datos del vehículo" role="group">
          <div role="img" aria-label={`Vehículo con placa ${item.placa ?? 'sin placa'}`} className="shrink-0">
            {item.placa ? (
              <Placa placa={item.placa} size="lg" aria-hidden="true" />
            ) : (
              <span className="di-placa-vacia" aria-hidden="true">
                Sin placa
              </span>
            )}
          </div>
          <div className="di-vehiculo__datos">
            <p className="di-antetitulo">Vehículo</p>
            <p className="di-vehiculo__nombre">{nombre}</p>
            <p className="di-vehiculo__meta font-mono">VIN: {item.vin ?? '—'}</p>
            {plateField && (
              <AvisoDigitoPlacaOt
                placa={plateField.valueText}
                source={plateField.source}
                digito={preferredDigit}
                clase={vehicleClass}
                testId="detalle-aviso-digito-placa"
                className="mt-2"
              />
            )}
          </div>
        </div>

        <dl className="di-fichas">
          <Ficha dt="Organismo de tránsito">{item.organismoTransito || '—'}</Ficha>
          <Ficha dt="Compañía">{item.companiaNombre || '—'}</Ficha>
          <Ficha dt="Creado">{formatFechaHora(item.createdAt)}</Ficha>
          <Ficha dt="Actualizado">{item.updatedAt ? formatFechaHora(item.updatedAt) : '—'}</Ficha>
          {/* HU #14322: el canal ya no se muestra (decisión de Samuel en el rediseño, Epic #14471). Solo queda el OT
              del punto cuando el trámite llegó por uno, en solo lectura. */}
          {detalle?.channel === 'pn_punto' && detalle.channelTransitOfficeName ? (
            <Ficha dt="OT del punto" testId="detalle-canal-ot">
              {detalle.channelTransitOfficeName}
            </Ficha>
          ) : null}
        </dl>
      </div>

      {pasos ? (
        <div className="mt-5 border-t border-[color:var(--di-divider)] pt-4">{pasos}</div>
      ) : null}
    </section>
  );
}
