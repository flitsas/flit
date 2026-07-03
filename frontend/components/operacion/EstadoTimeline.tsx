'use client';

import { useCallback, useEffect, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { StatusHistoryItem } from '@/lib/api/types/procedure-runtime';
import { estadoChipStyle, estadoLabel } from '@/lib/tramites/estados';

/**
 * HU-2 (N03, RF05) — timeline vertical del historial de transiciones de estado del trámite
 * (GET /instances/{id}/status-history). Más reciente arriba; cada entrada muestra el chip del
 * estado destino, el estado origen, la fecha, el usuario que ejecutó la transición y el motivo.
 * Tolera el vocabulario viejo (draft/submitted) vía estadoLabel (fallback titlecase).
 */

const PAGE_SIZE = 20;

const fmtFecha = new Intl.DateTimeFormat('es-CO', {
  dateStyle: 'medium',
  timeStyle: 'short',
});

function formatFecha(iso: string): string {
  const date = new Date(iso);
  return Number.isNaN(date.getTime()) ? iso : fmtFecha.format(date);
}

export function EstadoTimeline({ instanceId }: { instanceId: string }) {
  const [items, setItems] = useState<StatusHistoryItem[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  const [loading, setLoading] = useState(true);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async (nextPage: number) => {
    setLoading(true);
    setError(null);
    try {
      const res = await tramitesClient.getStatusHistory(instanceId, nextPage, PAGE_SIZE);
      setItems((prev) =>
        nextPage === 1 ? (res?.items ?? []) : [...prev, ...(res?.items ?? [])],
      );
      setTotal(res?.total ?? 0);
      setPage(nextPage);
    } catch {
      setError('No se pudo cargar el historial de estados.');
    } finally {
      setLoading(false);
    }
  }, [instanceId]);

  useEffect(() => {
    void load(1);
  }, [load]);

  if (loading && items.length === 0) {
    return (
      <p style={{ color: '#64748b', fontSize: 13, margin: 0 }}>Cargando historial…</p>
    );
  }

  if (error) {
    return (
      <p role="alert" style={{ color: '#c2410c', fontSize: 13, margin: 0 }}>
        {error}
      </p>
    );
  }

  if (items.length === 0) {
    return (
      <p style={{ color: '#64748b', fontSize: 13, margin: 0 }}>
        Este trámite aún no tiene cambios de estado.
      </p>
    );
  }

  return (
    <div>
      <ol style={{ listStyle: 'none', margin: 0, padding: 0 }}>
        {items.map((item, index) => {
          const chip = estadoChipStyle(item.toStatus);
          const isLast = index === items.length - 1;
          return (
            <li
              key={item.id}
              style={{
                position: 'relative',
                paddingLeft: 22,
                paddingBottom: isLast ? 0 : 16,
                borderLeft: isLast ? '2px solid transparent' : '2px solid #e2e8f0',
                marginLeft: 6,
              }}
            >
              <span
                aria-hidden
                style={{
                  position: 'absolute',
                  left: -6,
                  top: 4,
                  width: 10,
                  height: 10,
                  borderRadius: '50%',
                  background: chip.color,
                }}
              />
              <div style={{ display: 'flex', alignItems: 'center', gap: 8, flexWrap: 'wrap' }}>
                <span
                  style={{
                    background: chip.bg,
                    color: chip.color,
                    border: `1px solid ${chip.border}`,
                    borderRadius: 999,
                    padding: '2px 10px',
                    fontSize: 12,
                    fontWeight: 600,
                  }}
                >
                  {estadoLabel(item.toStatus)}
                </span>
                {item.fromStatus ? (
                  <span style={{ color: '#94a3b8', fontSize: 12 }}>
                    desde {estadoLabel(item.fromStatus)}
                  </span>
                ) : (
                  <span style={{ color: '#94a3b8', fontSize: 12 }}>estado inicial</span>
                )}
              </div>
              <div style={{ color: '#475569', fontSize: 12, marginTop: 4 }}>
                {formatFecha(item.changedAt)} · {item.changedByName ?? '—'}
              </div>
              {item.reason ? (
                <div style={{ color: '#334155', fontSize: 12, marginTop: 2, fontStyle: 'italic' }}>
                  Motivo: {item.reason}
                </div>
              ) : null}
            </li>
          );
        })}
      </ol>
      {items.length < total ? (
        <button
          type="button"
          onClick={() => void load(page + 1)}
          disabled={loading}
          style={{
            marginTop: 12,
            background: 'transparent',
            border: '1px solid #cbd5e1',
            borderRadius: 8,
            padding: '4px 12px',
            fontSize: 12,
            color: '#475569',
            cursor: 'pointer',
          }}
        >
          {loading ? 'Cargando…' : `Ver más (${items.length} de ${total})`}
        </button>
      ) : null}
    </div>
  );
}

/**
 * Panel colapsable del historial para incrustar bajo el wizard del detalle del trámite
 * sin interferir con el modo inmersivo (cerrado por defecto; carga al abrir).
 */
export function EstadoTimelinePanel({ instanceId }: { instanceId: string }) {
  const [open, setOpen] = useState(false);

  return (
    <section
      style={{
        margin: '16px auto 24px',
        maxWidth: 960,
        padding: '0 16px',
      }}
    >
      <button
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-expanded={open}
        style={{
          background: 'transparent',
          border: 'none',
          color: '#475569',
          fontSize: 13,
          fontWeight: 600,
          cursor: 'pointer',
          padding: 0,
        }}
      >
        {open ? '▾' : '▸'} Historial de estados
      </button>
      {open ? (
        <div
          style={{
            marginTop: 12,
            background: '#fff',
            border: '1px solid #e2e8f0',
            borderRadius: 12,
            padding: 16,
          }}
        >
          <EstadoTimeline instanceId={instanceId} />
        </div>
      ) : null}
    </section>
  );
}
