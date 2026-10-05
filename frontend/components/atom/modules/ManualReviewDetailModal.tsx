'use client';

import { useEffect, useRef, useState } from 'react';
import { Clock, ScanFace, ShieldCheck } from 'lucide-react';
import { Modal } from '@flit/ui/Modal';
import { UiStateBoundary } from '@flit/ui/UiStateBoundary';
import { useWizardFocusTrap } from '@/components/operacion/use-wizard-focus-trap';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import type { ManualDetail } from '@/lib/api/types/manual-review';
import { formatEspera, manualOriginLabel } from '@/lib/identidad/manual-review-meta';
import { formatFechaHora } from '@/lib/format/date';
import { ManualImageGallery } from './ManualImageGallery';
import { ManualStatusBadge } from './ManualStatusBadge';

/**
 * Detalle de una validación manual (modal ancho, no lateral). HU-C5 lo abre con los datos del registro;
 * HU-C6 añade las 4 capturas con visor y la constancia de consentimiento; HU-C7, aprobar y rechazar.
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
  const [visorAbierto, setVisorAbierto] = useState(false);

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
    // Con el visor de una imagen abierto, Escape cierra solo el visor: el detalle queda `busy`.
    <Modal open onClose={onClose} busy={visorAbierto} title="Detalle de validación manual" icon={ScanFace} size="xl">
      <DetalleCuerpo
        id={id}
        client={client}
        onClose={onClose}
        detail={carga === 'ready' ? detail : null}
        carga={carga}
        onRetry={() => setIntento((n) => n + 1)}
        onViewerChange={setVisorAbierto}
      />
    </Modal>
  );
}

function DetalleCuerpo({
  id,
  client,
  onClose,
  detail,
  carga,
  onRetry,
  onViewerChange,
}: {
  id: string;
  client: ManualReviewClient;
  onClose: () => void;
  detail: ManualDetail | null;
  carga: Carga;
  onRetry: () => void;
  onViewerChange: (open: boolean) => void;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(panelRef, { active: true });
  const sinCapturas = detail ? !detail.images.some((i) => i.available) : false;

  return (
    <div ref={panelRef} tabIndex={-1} className="flex flex-col gap-5 outline-none">
      <UiStateBoundary
        status={carga === 'ready' && detail ? 'ready' : carga}
        errorMessage="No se pudo cargar el detalle."
        onRetry={onRetry}
        skeletonRows={3}
      >
        {detail && (
          <>
            <dl className="grid gap-4 sm:grid-cols-2 lg:grid-cols-3">
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

            <section aria-labelledby={`${id}-consent`} className="rounded-xl border border-[#DFE5ED] p-3 dark:border-white/10">
              <h3 id={`${id}-consent`} className="flex items-center gap-2 text-sm font-bold text-[#162744] dark:text-white">
                <ShieldCheck className="h-4 w-4 text-[#557EFF]" aria-hidden /> Consentimiento de tratamiento de datos
              </h3>
              <p className="mt-1 text-sm">
                {detail.consentAt
                  ? `Aceptado el ${formatFechaHora(detail.consentAt)}${detail.consentTextVersion ? ` · texto ${detail.consentTextVersion}` : ''}`
                  : 'Sin constancia: el cliente aún no ha aceptado.'}
              </p>
            </section>

            <section aria-labelledby={`${id}-capturas`}>
              <h3 id={`${id}-capturas`} className="mb-2 text-sm font-bold text-[#162744] dark:text-white">
                Capturas del cliente
              </h3>
              {sinCapturas ? (
                <div role="status" className="flex items-start gap-2 rounded-xl border border-[#DFE5ED] bg-[#EEF5FF] p-3 text-sm dark:border-white/10 dark:bg-[#162744]">
                  <Clock className="mt-0.5 h-4 w-4 shrink-0 text-[#557EFF]" aria-hidden />
                  <div>
                    <p className="font-semibold">El cliente aún no ha capturado.</p>
                    {detail.linkExpiresAt && (
                      <p className="mt-0.5 text-xs text-[#59677D] dark:text-white/70">
                        El enlace caduca el {formatFechaHora(detail.linkExpiresAt)}.
                      </p>
                    )}
                  </div>
                </div>
              ) : (
                <ManualImageGallery client={client} id={detail.id} images={detail.images} onViewerChange={onViewerChange} />
              )}
            </section>
          </>
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
