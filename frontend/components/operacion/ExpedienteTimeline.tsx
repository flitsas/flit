'use client';

import { useCallback, useEffect, useId, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { StatusHistory, StatusHistoryModification } from '@/lib/api/types/procedure-runtime';
import { formatFechaHora } from '@/lib/format/date';
import { estadoLabel } from '@/lib/tramites/estados';
import { SeccionCargando, SeccionError, SeccionVacia } from './detalle/primitivos';
import { WizardAccordion } from './WizardAccordion';

/** Página de modificaciones (HU #14181). El API entrega la más reciente primero. */
const MODIFICACIONES_PAGE_SIZE = 20;

// Línea de tiempo del expediente. Adaptado del ExpedienteTimeline de Johan a la
// capa de datos de FLIT: la cronología se construye desde el statusHistory[] que
// ya devuelve getInstance (el historial real N 03 de procedure_instance_status_history).
// Labels/colores desde la fuente única lib/tramites/estados.ts (6 estados de negocio).
// El backend ya entrega el historial ordenado (fecha/hora + Id); aquí se re-ordena
// ASCENDENTE de forma defensiva para que la trazabilidad siempre se lea del estado
// inicial al actual aunque el caller pase los datos desordenados.

interface Props {
  statusHistory: StatusHistory[];
}

function fmt(iso: string): string {
  try {
    return formatFechaHora(new Date(iso));
  } catch {
    return iso;
  }
}

/**
 * Rótulo de un hito, en el formato literal de la propuesta: «Rechazado desde Entregado (motivo)».
 * `toStatus` siempre; `fromStatus` y `reason` se añaden solo cuando el backend los trae.
 */
function hitoLabel(e: StatusHistory): string {
  const to = estadoLabel(e.toStatus);
  const from = e.fromStatus ? estadoLabel(e.fromStatus) : null;
  const reason = e.reason?.trim();
  return `${to}${from ? ` desde ${from}` : ''}${reason ? ` (${reason})` : ''}`;
}

function sortHistory(rawHistory: StatusHistory[]): StatusHistory[] {
  return [...rawHistory].sort(
    (a, b) => new Date(a.changedAt).getTime() - new Date(b.changedAt).getTime(),
  );
}

function ExpedienteTimelineList({ statusHistory }: { statusHistory: StatusHistory[] }) {
  if (statusHistory.length === 0) {
    return <p className="text-xs opacity-70">Sin eventos registrados todavía.</p>;
  }

  return (
    <ol className="relative space-y-3 border-l pl-4" style={{ borderColor: '#DFE5ED' }}>
      {statusHistory.map((e, i) => (
        <li key={`${e.toStatus}-${e.changedAt}-${i}`} className="relative">
          <span
            className="absolute -left-[21px] top-1 h-2.5 w-2.5 rounded-full"
            style={{ background: i === statusHistory.length - 1 ? '#8CC63F' : 'var(--color-flit-brand)' }}
            aria-hidden="true"
          />
          <p className="text-xs font-semibold">{hitoLabel(e)}</p>
          <p className="mt-0.5 text-xs opacity-70">{fmt(e.changedAt)}</p>
        </li>
      ))}
    </ol>
  );
}

/** Sección fija (legacy) — preferir `ExpedienteCronologicoAccordion` en el wizard. */
export default function ExpedienteTimeline({ statusHistory: rawHistory }: Props) {
  const statusHistory = sortHistory(rawHistory);
  return (
    <section aria-label="Línea de tiempo del expediente" className="rounded-2xl border bg-white p-4 dark:bg-[#162744]">
      <div className="mb-3">
        <h4 className="text-sm font-bold">Expediente</h4>
        <p className="text-xs opacity-70">Trazabilidad cronológica del trámite.</p>
      </div>
      <ExpedienteTimelineList statusHistory={statusHistory} />
    </section>
  );
}

/**
 * Responsable de una modificación.
 * Si el API ya manda el rótulo («Sistema · RUNT», «Sistema · Validación de identidad») se muestra
 * tal cual. Solo cuando el nombre viene vacío se usa «Sistema». La compañía se añade aparte.
 */
function responsableDe(m: StatusHistoryModification): string {
  const nombre = m.changedByName?.trim();
  const base = nombre ? nombre : 'Sistema';
  const compania = m.changedByCompania?.trim();
  return compania ? `${base} · ${compania}` : base;
}

const CAMPOS_SI_NO = new Set([
  'cambio_color',
  'cambio_carroceria',
  'cambio_combustible',
  'blindaje',
]);

function valorDe(campo: string, valor: string, anterior: boolean): string {
  const limpio = valor.trim();
  if (anterior && limpio === '') return '—';
  if (campo === 'estado' && limpio !== '') return estadoLabel(limpio);
  if (CAMPOS_SI_NO.has(campo)) {
    if (limpio.toLowerCase() === 'true') return 'Sí';
    if (limpio.toLowerCase() === 'false') return 'No';
  }
  return valor;
}

/**
 * HU #14181 — bitácora de modificaciones de datos. Se monta al abrir el acordeón
 * (o al montar, si ya está abierto) y pide GET .../status-history. Conserva el
 * orden del API: más reciente primero. No reordena ascendente.
 */
export function ModificacionesBitacora({
  instanceId,
  tenantId,
  onCargada,
  embebida = false,
}: {
  instanceId: string;
  tenantId?: string;
  onCargada?: (info: { total: number; etiqueta: string | null; changedAt: string | null }) => void;
  /** Sin el borde superior: el contenedor ya es la tarjeta del detalle. */
  embebida?: boolean;
}) {
  const [rows, setRows] = useState<StatusHistoryModification[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState(false);
  const tituloId = useId();

  const fetchPage = useCallback(async (nextPage: number) => {
    try {
      const res = tenantId
        ? await tramitesClient.getStatusHistory(
            instanceId,
            nextPage,
            MODIFICACIONES_PAGE_SIZE,
            tenantId,
          )
        : await tramitesClient.getStatusHistory(instanceId, nextPage, MODIFICACIONES_PAGE_SIZE);
      const raw = res?.modificaciones;
      const chunk = Array.isArray(raw) ? raw : [];
      setRows((prev) => (nextPage === 1 ? chunk : [...prev, ...chunk]));
      const totalMods = typeof res?.modificacionesTotal === 'number' ? res.modificacionesTotal : 0;
      setTotal(totalMods);
      setPage(nextPage);
      setError(false);
      if (nextPage === 1) {
        onCargada?.({
          total: totalMods,
          etiqueta: chunk[0]?.etiqueta ?? null,
          changedAt: chunk[0]?.changedAt ?? null,
        });
      }
    } catch {
      setError(true);
    } finally {
      setLoading(false);
    }
  }, [instanceId, tenantId, onCargada]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga al montar (el acordeón solo monta el cuerpo al abrirse)
    void fetchPage(1);
  }, [fetchPage]);

  const loadMore = () => {
    setLoading(true);
    setError(false);
    void fetchPage(page + 1);
  };

  const retry = () => {
    setLoading(true);
    setError(false);
    void fetchPage(rows.length === 0 ? 1 : page + 1);
  };

  return (
    <section
      aria-labelledby={tituloId}
      className={embebida ? undefined : 'mt-4 border-t pt-3'}
      style={embebida ? undefined : { borderColor: '#DFE5ED' }}
    >
      <h4 id={tituloId} className="mb-2 text-xs font-bold" style={{ color: 'var(--color-flit-brand)' }}>
        Modificaciones
      </h4>
      {loading && rows.length === 0 ? <SeccionCargando etiqueta="Cargando modificaciones" /> : null}
      {error && rows.length === 0 ? (
        <SeccionError
          mensaje="No se pudieron cargar las modificaciones."
          onReintentar={retry}
          contexto="las modificaciones"
        />
      ) : null}
      {!loading && !error && rows.length === 0 ? (
        <SeccionVacia mensaje="Todavía no hay modificaciones" />
      ) : null}
      {rows.length > 0 ? (
        <>
          <ol aria-label="Modificaciones del expediente" className="space-y-3">
            {rows.map((m) => (
              <li key={m.id} className="text-xs">
                <p className="font-semibold">{formatFechaHora(m.changedAt)}</p>
                <p className="mt-0.5">
                  <span className="opacity-70">Responsable: </span>
                  <span>{responsableDe(m)}</span>
                </p>
                <p className="mt-0.5">
                  <span className="opacity-70">Dato: </span>
                  <span>{m.etiqueta}</span>
                </p>
                <p className="mt-0.5">
                  <span className="opacity-70">Valor anterior: </span>
                  <span>{valorDe(m.campo, m.anterior, true)}</span>
                </p>
                <p className="mt-0.5">
                  <span className="opacity-70">Valor nuevo: </span>
                  <span>{valorDe(m.campo, m.nuevo, false)}</span>
                </p>
              </li>
            ))}
          </ol>
          {error ? (
            <div className="mt-3">
              <SeccionError
                mensaje="No se pudieron cargar las modificaciones."
                onReintentar={retry}
                contexto="las modificaciones"
              />
            </div>
          ) : null}
          {!error && rows.length < total ? (
            <button
              type="button"
              onClick={loadMore}
              disabled={loading}
              className="mt-3 rounded-xl border px-3 py-1.5 text-xs font-semibold transition hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
              style={{ borderColor: '#DFE5ED', color: 'var(--color-flit-brand)' }}
            >
              {loading ? 'Cargando…' : 'Ver más'}
            </button>
          ) : null}
        </>
      ) : null}
    </section>
  );
}

/**
 * HU #12730 (D.5) — expediente cronológico como acordeón plegado con resumen en cabecera.
 * HU #14181 — al abrirlo, la línea de modificaciones (incluye el estado) es la única.
 * Los hitos de estado quedan como respaldo si esa línea no carga.
 */
export function ExpedienteCronologicoAccordion({
  statusHistory: rawHistory,
  instanceId,
}: Props & { instanceId?: string }) {
  const statusHistory = sortHistory(rawHistory);
  const last = statusHistory[statusHistory.length - 1];
  const [lineaUnica, setLineaUnica] = useState(false);
  const [resumenLinea, setResumenLinea] = useState<string | null>(null);
  const onCargada = useCallback(
    (info: { total: number; etiqueta: string | null; changedAt: string | null }) => {
      setLineaUnica(true);
      if (info.total === 0 || !info.etiqueta || !info.changedAt) {
        setResumenLinea(null);
        return;
      }
      const n = info.total;
      const cuenta = n === 1 ? '1 modificación' : `${n} modificaciones`;
      setResumenLinea(`${cuenta} · última: ${info.etiqueta} · ${fmt(info.changedAt)}`);
    },
    [],
  );
  const subtitle =
    resumenLinea ??
    (statusHistory.length === 0
      ? 'Sin eventos registrados'
      : `${statusHistory.length} evento${statusHistory.length === 1 ? '' : 's'} · último: ${hitoLabel(last)} · ${fmt(last.changedAt)}`);

  return (
    <WizardAccordion
      title="Expediente — Trazabilidad cronológica"
      subtitle={subtitle}
      defaultOpen={false}
      level="h3"
      regionLabel="Trazabilidad cronológica del trámite"
    >
      {lineaUnica ? null : <ExpedienteTimelineList statusHistory={statusHistory} />}
      {instanceId ? <ModificacionesBitacora instanceId={instanceId} onCargada={onCargada} /> : null}
    </WizardAccordion>
  );
}
