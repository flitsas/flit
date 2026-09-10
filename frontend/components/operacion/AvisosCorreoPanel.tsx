'use client';

import { useCallback, useEffect, useRef, useState } from 'react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { WizardAccordion } from './WizardAccordion';
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
    // Acordeón compartido del wizard. En modo CONTROLADO: `open` sigue viviendo aquí porque el
    // efecto de carga en diferido depende de él (los avisos se piden al abrir, no al montar).
    <WizardAccordion
      title="Avisos de correo"
      regionLabel="Avisos de correo del trámite"
      open={open}
      onOpenChange={setOpen}
    >
      {loading && !items ? (
        <p className="text-xs opacity-70">Cargando avisos…</p>
      ) : error ? (
        <p className="text-xs" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      ) : !items || items.length === 0 ? (
        <p className="text-xs opacity-70">
          Este trámite aún no tiene avisos de correo registrados.
        </p>
      ) : (
        <ul className="flex flex-col gap-2.5">
          {items.map((item) => (
            <li
              key={item.id}
              className="rounded-xl border px-3 py-2.5 text-xs text-[#162744] dark:text-white/85"
            >
              <div className="flex flex-wrap justify-between gap-2">
                <strong>
                  {item.recipientRole} · {KIND_LABEL[item.recipientKind] ?? item.recipientKind}
                </strong>
                <span className="font-semibold" style={{ color: statusColor(item.status) }}>
                  {STATUS_LABEL[item.status] ?? item.status}
                </span>
              </div>
              <div className="mt-1 opacity-70">
                {item.recipientName ? `${item.recipientName} · ` : null}
                {item.recipientMasked ?? 'Sin correo'}
              </div>
              {item.failureReason ? (
                <div className="mt-1" style={{ color: '#B45309' }}>
                  {item.failureReason}
                </div>
              ) : null}
            </li>
          ))}
        </ul>
      )}
    </WizardAccordion>
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
