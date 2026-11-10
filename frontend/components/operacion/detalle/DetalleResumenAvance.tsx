'use client';

import type { CSSProperties, ReactNode } from 'react';
import { Placa } from '@flit/ui/Placa';
import { formatFechaHora } from '@/lib/format/date';
import { canalLabel } from '@/lib/tramites/tramites-row-labels';
import {
  estadoChipStyle,
  estadoLabel,
  estadoLabelConOrigen,
} from '@/lib/tramites/estados';
import { familiaUsaEstado } from '@/lib/tramites/panelesEstado';
import type {
  InstanceSummary,
  ProcedureInstanceDetail,
  RejectedFrom,
  StatusHistory,
} from '@/lib/api/types/procedure-runtime';
import { DETALLE_CARD } from './detalle-visual';

/**
 * HU #14482 — bloque de resumen y avance del detalle del trámite, con el esquema de `vTramite` de la
 * propuesta (`.tramite-resumen` + `.tramite-avance`): arriba el vehículo y las fichas del trámite,
 * abajo la línea de avance. Reemplaza la columna `DetalleVehiculoSidebar` (4/12) para que la sección
 * activa use todo el ancho del popup. Solo datos que el detalle ya tenía: nada del prototipo que
 * nuestro modelo no traiga (color, «Sin multas», pastillas de SOAT/revisión).
 *
 * La línea de avance RESUME el historial de estados (`statusHistory`); la línea de tiempo completa
 * (quién, cuándo, correo, motivo) sigue en el panel «Línea de Tiempo del Trámite».
 */

/** Ruta lineal de nuestro ciclo de vida; los pasos pendientes salen de aquí según la familia. */
const RUTA_LINEAL = ['borrador', 'preparado', 'preasignacion', 'asignado', 'entregado', 'aprobado'] as const;

/** Estados que no siguen la ruta lineal: se ven como chip con su color y sin pasos pendientes. */
const FUERA_DE_RUTA = ['rechazado', 'subsanacion', 'anulado', 'revocado', 'aprobado'];

const DIA_CORTO = new Intl.DateTimeFormat('es-CO', {
  day: 'numeric',
  month: 'short',
  timeZone: 'America/Bogota',
});

function fechaCorta(iso: string): string {
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return '—';
  return DIA_CORTO.format(d) === DIA_CORTO.format(new Date()) ? 'Hoy' : DIA_CORTO.format(d).replace('.', '');
}

type EstadoPaso = 'hecho' | 'actual' | 'pendiente';

interface PasoAvance {
  key: string;
  estado: string;
  label: string;
  /** Texto bajo el nombre: fecha corta, «En curso» o «Pendiente». */
  nota: string;
  /** Fecha completa para el `title`. */
  fecha?: string;
  tipo: EstadoPaso;
}

export function pasosDeAvance({
  history,
  estadoVigente,
  rejectedFrom,
  createdAt,
  familia,
}: {
  history: readonly StatusHistory[];
  estadoVigente: string;
  rejectedFrom?: RejectedFrom | null;
  createdAt: string;
  familia: InstanceSummary['modalidad'];
}): PasoAvance[] {
  const orden = [...history].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  );
  const hitos: { estado: string; fecha?: string }[] = [];
  if (orden[0]?.fromStatus) hitos.push({ estado: orden[0].fromStatus, fecha: createdAt });
  for (const h of orden) {
    if (hitos.at(-1)?.estado === h.toStatus) continue;
    hitos.push({ estado: h.toStatus, fecha: h.changedAt });
  }
  if (hitos.at(-1)?.estado !== estadoVigente) {
    hitos.push({ estado: estadoVigente, fecha: hitos.length === 0 ? createdAt : undefined });
  }

  const pasos: PasoAvance[] = hitos.map((h, i) => {
    const actual = i === hitos.length - 1;
    return {
      key: `h-${i}`,
      estado: h.estado,
      label: actual ? estadoLabelConOrigen(h.estado, rejectedFrom) : estadoLabel(h.estado),
      nota: h.fecha ? fechaCorta(h.fecha) : 'En curso',
      fecha: h.fecha ? formatFechaHora(h.fecha) : undefined,
      tipo: actual ? 'actual' : 'hecho',
    };
  });

  const i = (RUTA_LINEAL as readonly string[]).indexOf(estadoVigente);
  if (i >= 0 && !FUERA_DE_RUTA.includes(estadoVigente)) {
    RUTA_LINEAL.slice(i + 1)
      .filter((e) => familiaUsaEstado(familia, e))
      .forEach((e) =>
        pasos.push({ key: `p-${e}`, estado: e, label: estadoLabel(e), nota: 'Pendiente', tipo: 'pendiente' }),
      );
  }
  return pasos;
}

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

export function DetalleResumenAvance({
  item,
  detalle = null,
  estadoVigente,
  rejectedFrom,
}: {
  item: InstanceSummary;
  /** Detalle cargado; `null` mientras carga o con error (canal en «—», avance solo con el estado). */
  detalle?: Pick<
    ProcedureInstanceDetail,
    'channel' | 'channelTransitOfficeName' | 'statusHistory'
  > | null;
  estadoVigente: string;
  rejectedFrom?: RejectedFrom | null;
}) {
  const nombre = [item.vehiculoMarca, item.vehiculoLinea].filter(Boolean).join(' ') || '—';
  const pasos = pasosDeAvance({
    history: detalle?.statusHistory ?? [],
    estadoVigente,
    rejectedFrom,
    createdAt: item.createdAt,
    familia: item.modalidad,
  });
  const actual = pasos.find((p) => p.tipo === 'actual');
  const bajada =
    estadoVigente === 'borrador' && item.totalPasos > 0
      ? `Paso ${item.pasoActual} de ${item.totalPasos}${item.pasoNombre ? `: ${item.pasoNombre}` : ''}.`
      : `Estado actual: ${actual?.label ?? estadoLabel(estadoVigente)}.`;

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
          </div>
        </div>

        <dl className="di-fichas">
          <Ficha dt="Organismo de tránsito">{item.organismoTransito || '—'}</Ficha>
          <Ficha dt="Compañía">{item.companiaNombre || '—'}</Ficha>
          <Ficha dt="Creado">{formatFechaHora(item.createdAt)}</Ficha>
          <Ficha dt="Actualizado">{item.updatedAt ? formatFechaHora(item.updatedAt) : '—'}</Ficha>
          {/* HU #14322 — solo lectura: texto, sin control para cambiarlo (ningún rol lo edita). */}
          <Ficha dt="Canal" testId="detalle-canal">
            {detalle ? canalLabel(detalle.channel) : '—'}
          </Ficha>
          {detalle?.channel === 'pn_punto' && detalle.channelTransitOfficeName ? (
            <Ficha dt="OT del punto" testId="detalle-canal-ot">
              {detalle.channelTransitOfficeName}
            </Ficha>
          ) : null}
        </dl>
      </div>

      <div className="mt-5 border-t border-[color:var(--di-divider)] pt-4">
        <h3 className="text-base font-bold" style={{ color: 'var(--di-heading)' }}>
          Avance
        </h3>
        <p className="mt-0.5 text-[13px]" style={{ color: 'var(--di-text-secondary)' }}>
          {bajada}
        </p>
        <div className="-mx-1 mt-4 overflow-x-auto px-1 pb-1">
          <ol className="di-linea" aria-label="Avance del trámite">
            {pasos.map((p) => {
              const chip = p.tipo === 'actual' && FUERA_DE_RUTA.includes(p.estado);
              const estilo = estadoChipStyle(p.estado);
              return (
                <li
                  key={p.key}
                  className={`di-linea__paso${p.tipo === 'hecho' ? ' di-linea__paso--hecho' : ''}${
                    p.tipo === 'actual' ? ' di-linea__paso--actual' : ''
                  }`}
                  aria-current={p.tipo === 'actual' ? 'step' : undefined}
                  title={p.fecha}
                >
                  <span
                    className="di-linea__punto"
                    aria-hidden="true"
                    style={
                      chip
                        ? {
                            background: `var(--estado-${p.estado}-acento)`,
                            borderColor: `var(--estado-${p.estado}-acento)`,
                          }
                        : undefined
                    }
                  />
                  {/* Nombre y fecha en el MISMO elemento (la primera línea se estiliza con
                      ::first-line): así el nombre del estado no queda como un texto suelto idéntico
                      al del chip de la cabecera. En los estados fuera de ruta toma el color del
                      estado, como su chip. */}
                  <span
                    className="di-linea__texto"
                    style={chip ? ({ '--paso-color': estilo.color } as CSSProperties) : undefined}
                  >
                    {p.label}
                    <br />
                    {p.nota}
                  </span>
                </li>
              );
            })}
          </ol>
        </div>
      </div>
    </section>
  );
}
