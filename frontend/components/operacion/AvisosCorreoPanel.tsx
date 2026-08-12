'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { NotificationDispatchItem } from '@/lib/api/types/procedure-runtime';

const STATUS_LABEL: Record<string, string> = {
  pendiente: 'Pendiente',
  enviado: 'Enviado',
  fallido: 'Fallido',
  omitido: 'Omitido',
};

const KIND_LABEL: Record<string, string> = {
  persona: 'Persona',
  empresa: 'Empresa',
  representante_legal: 'Representante legal',
};

/**
 * HU #11470 — panel del gestor: a quién se notificó (o no) y por qué.
 * Mismo patrón disclosure que EstadoTimelinePanel / IdentityStatusPanel.
 */
export function AvisosCorreoPanel({ instanceId }: { instanceId: string | null }) {
  const [open, setOpen] = useState(false);
  const [items, setItems] = useState<NotificationDispatchItem[] | null>(null);
  const [error, setError] = useState<string | null>(null);
  const [loading, setLoading] = useState(false);
  const loadedOnceRef = useRef(false);
  const fetchingRef = useRef(false);

  const load = useCallback(async () => {
    if (!instanceId || fetchingRef.current) return;
    fetchingRef.current = true;
    setLoading(true);
    setError(null);
    try {
      const res = await tramitesClient.getNotificationDispatches(instanceId);
      setItems(res.items ?? []);
      loadedOnceRef.current = true;
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudieron cargar los avisos de correo.');
    } finally {
      fetchingRef.current = false;
      setLoading(false);
    }
  }, [instanceId]);

  useEffect(() => {
    if (!open || !instanceId) return;
    if (!loadedOnceRef.current) void load();
  }, [open, instanceId, load]);

  if (!instanceId) return null;

  return (
    <section
      style={{
        margin: '8px auto 24px',
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
        {open ? '▾' : '▸'} Avisos de correo
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
          role="region"
          aria-label="Avisos de correo del trámite"
        >
          {loading && !items ? (
            <p style={{ margin: 0, fontSize: 13, color: '#64748b' }}>Cargando avisos…</p>
          ) : error ? (
            <p style={{ margin: 0, fontSize: 13, color: '#FF4E00' }} role="alert">
              {error}
            </p>
          ) : !items || items.length === 0 ? (
            <p style={{ margin: 0, fontSize: 13, color: '#64748b' }}>
              Este trámite aún no tiene avisos de correo registrados.
            </p>
          ) : (
            <ul style={{ listStyle: 'none', margin: 0, padding: 0, display: 'flex', flexDirection: 'column', gap: 10 }}>
              {items.map((item) => (
                <li
                  key={item.id}
                  style={{
                    border: '1px solid #e2e8f0',
                    borderRadius: 10,
                    padding: '10px 12px',
                    fontSize: 13,
                    color: '#162744',
                  }}
                >
                  <div style={{ display: 'flex', flexWrap: 'wrap', gap: 8, justifyContent: 'space-between' }}>
                    <strong>
                      {item.recipientRole} · {KIND_LABEL[item.recipientKind] ?? item.recipientKind}
                    </strong>
                    <span style={{ fontWeight: 600, color: statusColor(item.status) }}>
                      {STATUS_LABEL[item.status] ?? item.status}
                    </span>
                  </div>
                  <div style={{ marginTop: 4, color: '#475569' }}>
                    {item.recipientName ? `${item.recipientName} · ` : null}
                    {item.recipientMasked ?? 'Sin correo'}
                  </div>
                  {item.failureReason ? (
                    <div style={{ marginTop: 4, color: '#B45309' }}>{item.failureReason}</div>
                  ) : null}
                </li>
              ))}
            </ul>
          )}
        </div>
      ) : null}
    </section>
  );
}

function statusColor(status: string): string {
  switch (status) {
    case 'enviado':
      return '#2E7D32';
    case 'fallido':
      return '#C62828';
    case 'omitido':
      return '#B45309';
    default:
      return '#475569';
  }
}
