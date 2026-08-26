'use client';

import { useEffect, useState } from 'react';
import { Modal } from '@/components/atom/Modal';
import ExpedienteTimeline from '@/components/operacion/ExpedienteTimeline';
import { SeccionCargando, SeccionError } from '@/components/operacion/detalle/primitivos';
import { tramitesClient } from '@/lib/api/tramites-client';
import type { StatusHistory } from '@/lib/api/types/procedure-runtime';

/**
 * Modal de trazabilidad cronológica del trámite (listado → click en badge Estado).
 * Reutiliza `ExpedienteTimeline` + `statusHistory` de `getInstance` — sin backend nuevo.
 */
export function TramiteTrackingModal({
  open,
  instanceId,
  tenantId,
  titleHint,
  onClose,
}: {
  open: boolean;
  instanceId: string | null;
  tenantId?: string | null;
  /** Radicado / placa para el título accesible. */
  titleHint?: string | null;
  onClose: () => void;
}) {
  const [history, setHistory] = useState<StatusHistory[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [reloadKey, setReloadKey] = useState(0);

  useEffect(() => {
    if (!open || !instanceId) return;
    let cancelled = false;
    const load = async () => {
      setLoading(true);
      setError(null);
      try {
        const d = await tramitesClient.getInstance(instanceId, tenantId ?? undefined);
        if (!cancelled) setHistory(d.statusHistory ?? []);
      } catch (e: unknown) {
        if (!cancelled) {
          setError(e instanceof Error ? e.message : 'No se pudo cargar la trazabilidad del trámite.');
          setHistory([]);
        }
      } finally {
        if (!cancelled) setLoading(false);
      }
    };
    void load();
    return () => {
      cancelled = true;
    };
  }, [open, instanceId, tenantId, reloadKey]);

  const title = titleHint
    ? `Línea de tiempo del trámite · ${titleHint}`
    : 'Línea de tiempo del trámite';

  return (
    <Modal open={open} onClose={onClose} title={title} size="lg">
      {loading ? <SeccionCargando etiqueta="Cargando trazabilidad" filas={3} /> : null}
      {!loading && error ? (
        <SeccionError
          mensaje={error}
          contexto="la trazabilidad del trámite"
          onReintentar={() => setReloadKey((k) => k + 1)}
        />
      ) : null}
      {!loading && !error ? <ExpedienteTimeline statusHistory={history} /> : null}
    </Modal>
  );
}
