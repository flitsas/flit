'use client';

import { useEffect, useRef, useState } from 'react';
import { ScanFace } from 'lucide-react';
import { Modal } from '@flit/ui/Modal';
import { UiStateBoundary } from '@flit/ui/UiStateBoundary';
import { useWizardFocusTrap } from '@/components/operacion/use-wizard-focus-trap';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import type { ManualDetail } from '@/lib/api/types/manual-review';
import { formatEspera, manualOriginLabel } from '@/lib/identidad/manual-review-meta';
import { formatFechaHora } from '@/lib/format/date';
import { ManualStatusBadge } from './ManualStatusBadge';

/**
 * Detalle de una validación manual (modal normal, no lateral). HU-C5 lo abre con los datos del
 * registro; HU-C6 añade las 4 capturas y la constancia de consentimiento; HU-C7, las acciones.
 */

type Carga = 'loading' | 'error' | 'ready';

export function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-[#59677D] dark:text-white/70">{label}</dt>
      <dd className="mt-0.5 text-sm font-semibold text-[#162744] dark:text-white">{children}</dd>
    </div>
  );
}

export function ManualReviewDetailModal({
  id,
  client,
  onClose,
}: {
  id: string | null;
  client: ManualReviewClient;
  onClose: () => void;
}) {
  const [detail, setDetail] = useState<ManualDetail | null>(null);
  const [carga, setCarga] = useState<Carga>('loading');
  const [intento, setIntento] = useState(0);
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!id) return;
    const ctrl = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- inicio de la carga asíncrona
    setCarga('loading');
    client
      .getManualDetail(id, ctrl.signal)
      .then((d) => {
        if (ctrl.signal.aborted) return;
        setDetail(d);
        setCarga('ready');
      })
      .catch(() => {
        if (!ctrl.signal.aborted) setCarga('error');
      });
    return () => ctrl.abort();
  }, [id, client, intento]);

  if (!id) return null;

  return (
    <Modal open onClose={onClose} title="Detalle de validación manual" icon={ScanFace} size="xl">
      <DetalleCuerpo
        panelRef={panelRef}
        onClose={onClose}
        detail={carga === 'ready' ? detail : null}
        carga={carga}
        onRetry={() => setIntento((n) => n + 1)}
      />
    </Modal>
  );
}

function DetalleCuerpo({
  panelRef,
  onClose,
  detail,
  carga,
  onRetry,
}: {
  panelRef: React.RefObject<HTMLDivElement | null>;
  onClose: () => void;
  detail: ManualDetail | null;
  carga: Carga;
  onRetry: () => void;
}) {
  useWizardFocusTrap(panelRef, { active: true });
  return (
    <div ref={panelRef} tabIndex={-1} className="flex flex-col gap-4 outline-none">
      <UiStateBoundary
        status={carga === 'ready' && detail ? 'ready' : carga}
        errorMessage="No se pudo cargar el detalle."
        onRetry={onRetry}
        skeletonRows={3}
      >
        {detail && (
          <dl className="grid gap-4 sm:grid-cols-2">
            <Dato label="Nombre">{detail.fullName}</Dato>
            <Dato label="Documento">
              <span className="font-mono">{detail.documentNumber}</span>
            </Dato>
            <Dato label="Compañía">{detail.tenantName}</Dato>
            <Dato label="Origen">{manualOriginLabel(detail.origin)}</Dato>
            <Dato label="Estado">
              <ManualStatusBadge status={detail.status} />
            </Dato>
            <Dato label="Fecha de activación">{formatFechaHora(detail.activatedAt)}</Dato>
            <Dato label="Tiempo en espera">{formatEspera(detail.waitingMinutes)}</Dato>
          </dl>
        )}
      </UiStateBoundary>
      <div className="flex justify-end">
        <button
          type="button"
          onClick={onClose}
          className="rounded-xl border border-[#DFE5ED] px-4 py-2 text-sm font-medium text-[#162744] transition hover:bg-[#162744]/[0.04] focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:text-white"
        >
          Cerrar
        </button>
      </div>
    </div>
  );
}
