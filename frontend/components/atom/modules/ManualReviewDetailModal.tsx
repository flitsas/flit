'use client';

import { useEffect, useRef, useState } from 'react';
import { AlertTriangle, CheckCircle2, Clock, Eye, ScanFace, ShieldCheck, XCircle } from 'lucide-react';
import { Modal } from '@flit/ui/Modal';
import { UiStateBoundary } from '@flit/ui/UiStateBoundary';
import { useWizardFocusTrap } from '@/components/operacion/use-wizard-focus-trap';
import type { ManualReviewClient } from '@/lib/api/manual-review-client';
import { ApiError } from '@/lib/api/types';
import type { ManualDetail } from '@/lib/api/types/manual-review';
import { formatEspera, manualOriginLabel } from '@/lib/identidad/manual-review-meta';
import { etiquetaMotivoRechazoManual } from '@/lib/identidad/motivos-rechazo-manual';
import { formatFechaHora } from '@/lib/format/date';
import { MANUAL_AVISO, MANUAL_AVISO_BASE, MANUAL_BTN_SECUNDARIO } from './manual-field-styles';
import { CertificadoIdentidadAccion } from './CertificadoIdentidadAccion';
import { ManualImageGallery } from './ManualImageGallery';
import { ManualReviewActions } from './ManualReviewActions';
import { ManualStatusBadge } from './ManualStatusBadge';

/**
 * Detalle de una validación manual (modal ancho, no lateral). HU-C5 lo abre con los datos del registro;
 * HU-C6 añade las 4 capturas con visor y la constancia de consentimiento; HU-C7, aprobar y rechazar (solo con
 * el registro pendiente de revisión), actualizando el detalle y la fila sin recargar.
 */

type Carga = 'loading' | 'error' | 'ready';

export function Dato({ label, children }: { label: string; children: React.ReactNode }) {
  return (
    <div className="min-w-0">
      <dt className="text-xs font-medium text-[#59677D] dark:text-white/70">{label}</dt>
      <dd className="mt-0.5 text-sm font-semibold text-flit-primary dark:text-white">{children}</dd>
    </div>
  );
}

export function ManualReviewDetailModal({
  id,
  client,
  onClose,
  onChanged,
  onNotFound,
}: {
  id: string | null;
  client: ManualReviewClient;
  onClose: () => void;
  /** El registro cambió (aprobado o rechazado): la tabla refresca su fila. */
  onChanged?: () => void;
  /**
   * El id no existe, no es una validación manual o no se puede ver (404/403/400): el modal se retira y quien lo
   * abrió por enlace profundo avisa en su pantalla en vez de dejar un modal vacío.
   */
  onNotFound?: () => void;
}) {
  const [detail, setDetail] = useState<ManualDetail | null>(null);
  const [carga, setCarga] = useState<Carga>('loading');
  const [intento, setIntento] = useState(0);
  const [visorAbierto, setVisorAbierto] = useState(false);
  const [accionAbierta, setAccionAbierta] = useState(false);
  const [mensaje, setMensaje] = useState<string | null>(null);

  const onNotFoundRef = useRef(onNotFound);
  useEffect(() => {
    onNotFoundRef.current = onNotFound;
  });

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
      .catch((err: unknown) => {
        if (ctrl.signal.aborted) return;
        if (onNotFoundRef.current && err instanceof ApiError && [400, 403, 404].includes(err.status)) {
          onNotFoundRef.current();
          return;
        }
        setCarga('error');
      });
    return () => ctrl.abort();
  }, [id, client, intento]);

  if (!id) return null;

  /** Vuelve a pedir el detalle sin pasar por el estado «cargando»: el registro se actualiza en su sitio. */
  const refrescar = async () => {
    try {
      setDetail(await client.getManualDetail(id));
    } catch {
      /* se conserva el detalle anterior; la acción ya se informó */
    }
  };

  return (
    // Con el visor o un diálogo de confirmación abiertos, Escape cierra solo ese: el detalle queda `busy`.
    <Modal open onClose={onClose} busy={visorAbierto || accionAbierta} title="Detalle de validación manual" icon={ScanFace} size="xl">
      <DetalleCuerpo
        id={id}
        client={client}
        onClose={onClose}
        detail={carga === 'ready' ? detail : null}
        carga={carga}
        onRetry={() => setIntento((n) => n + 1)}
        onViewerChange={setVisorAbierto}
        mensaje={mensaje}
        onDialogChange={setAccionAbierta}
        onDone={(m) => {
          setMensaje(m);
          void refrescar();
          onChanged?.();
        }}
        onStale={() => {
          setMensaje(null);
          void refrescar();
          onChanged?.();
        }}
      />
    </Modal>
  );
}

/** Fotos que no pasaron la revisión en el dispositivo (la firma no se revisa). */
function contarNoVerificadas(detail: ManualDetail): number {
  const fotos = detail.validacionFotos;
  if (!fotos) return 0;
  return [fotos.rostro, fotos.anverso, fotos.reverso].filter((f) => f?.estado === 'no_verificada').length;
}

/** Lo primero que lee quien abre el detalle: en qué va el caso y qué sigue. */
function EstadoDelCaso({ detail, sinCapturas }: { detail: ManualDetail; sinCapturas: boolean }) {
  if (detail.status === 'aprobado') {
    return (
      <div className={`flex flex-wrap items-center justify-between gap-2 rounded-2xl border px-4 py-2.5 text-sm ${MANUAL_AVISO.success}`}>
        <p className="flex items-center gap-2 font-semibold">
          <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden /> Identidad aprobada
        </p>
        <CertificadoIdentidadAccion
          validation={{ id: detail.id, status: 'aprobado', provider: 'manual', cartaDisponible: detail.cartaDisponible }}
        />
      </div>
    );
  }
  if (detail.status === 'rechazado') {
    return (
      <div className={`${MANUAL_AVISO_BASE} ${MANUAL_AVISO.danger}`}>
        <XCircle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
        <div className="min-w-0">
          {detail.rejectionReasonCode && (
            <>
              <p className="text-xs">Motivo del rechazo</p>
              <p className="font-semibold">
                {etiquetaMotivoRechazoManual(detail.rejectionReasonCode) ?? detail.rejectionReasonCode}
              </p>
            </>
          )}
          {detail.linkExpiresAt && (
            <p className="mt-0.5">Se envió un enlace nuevo al cliente. Vence el {formatFechaHora(detail.linkExpiresAt)}</p>
          )}
        </div>
      </div>
    );
  }
  if (sinCapturas) {
    return (
      <div role="status" className={`${MANUAL_AVISO_BASE} ${MANUAL_AVISO.info}`}>
        <Clock className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
        <div className="min-w-0">
          <p className="font-semibold">El cliente aún no ha capturado.</p>
          {detail.linkExpiresAt && (
            <p className="mt-0.5">El enlace caduca el {formatFechaHora(detail.linkExpiresAt)}.</p>
          )}
          {detail.status === 'manual_activo' && (
            <p className="mt-0.5">Si no le llegó el correo, copia el enlace desde «Acciones» en la fila.</p>
          )}
        </div>
      </div>
    );
  }
  if (detail.status === 'pendiente_revision_manual') {
    const noVerificadas = contarNoVerificadas(detail);
    return (
      <div className={`${MANUAL_AVISO_BASE} ${noVerificadas > 0 ? MANUAL_AVISO.warning : MANUAL_AVISO.info}`}>
        {noVerificadas > 0 ? (
          <AlertTriangle className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
        ) : (
          <Eye className="mt-0.5 h-4 w-4 shrink-0" aria-hidden />
        )}
        <div className="min-w-0">
          <p className="font-semibold">Revisa las fotos y decide</p>
          <p className="mt-0.5">
            {noVerificadas > 0
              ? `${noVerificadas === 1 ? '1 foto no pasó' : `${noVerificadas} fotos no pasaron`} la revisión automática: mírala${noVerificadas === 1 ? '' : 's'} con más cuidado antes de aprobar.`
              : 'Compara el rostro con el del documento y, al final, aprueba o rechaza.'}
          </p>
        </div>
      </div>
    );
  }
  return null;
}

function DetalleCuerpo({
  id,
  client,
  onClose,
  detail,
  carga,
  onRetry,
  onViewerChange,
  mensaje,
  onDialogChange,
  onDone,
  onStale,
}: {
  id: string;
  client: ManualReviewClient;
  onClose: () => void;
  detail: ManualDetail | null;
  carga: Carga;
  onRetry: () => void;
  onViewerChange: (open: boolean) => void;
  mensaje: string | null;
  onDialogChange: (open: boolean) => void;
  onDone: (message: string) => void;
  onStale: () => void;
}) {
  const panelRef = useRef<HTMLDivElement>(null);
  useWizardFocusTrap(panelRef, { active: true, initialFocusRef: panelRef });
  // La acción se aplicó pero con una salvedad (p. ej. el correo no salió): tono de advertencia, no de éxito.
  const avisoConProblema = Boolean(mensaje && /no pudo enviarse/i.test(mensaje));
  const sinCapturas = detail ? !detail.images.some((i) => i.available) : false;

  return (
    <div ref={panelRef} tabIndex={-1} className="flex flex-col gap-4 outline-none">
      <UiStateBoundary
        status={carga === 'ready' && detail ? 'ready' : carga}
        errorMessage="No se pudo cargar el detalle."
        onRetry={onRetry}
        skeletonRows={3}
      >
        {detail && (
          <>
            <section
              aria-label="Datos del caso"
              className="rounded-2xl border border-[#DFE5ED] p-4 dark:border-white/10"
            >
              <div className="flex flex-wrap items-start justify-between gap-3">
                <div className="min-w-0">
                  <p className="text-lg font-bold text-flit-primary dark:text-white">{detail.fullName}</p>
                  <p className="font-mono text-sm text-[#59677D] dark:text-white/70">{detail.documentNumber}</p>
                </div>
                <ManualStatusBadge status={detail.status} />
              </div>
              <dl className="mt-3 grid gap-3 border-t border-[#DFE5ED] pt-3 sm:grid-cols-2 lg:grid-cols-4 dark:border-white/10">
                <Dato label="Compañía">{detail.tenantName}</Dato>
                <Dato label="Origen">{manualOriginLabel(detail.origin)}</Dato>
                <Dato label="Fecha de activación">{formatFechaHora(detail.activatedAt)}</Dato>
                {detail.waitingMinutes != null && (
                  <Dato label="Tiempo en espera">{formatEspera(detail.waitingMinutes)}</Dato>
                )}
              </dl>
              <div className="mt-3 flex items-start gap-2 text-sm">
                <ShieldCheck className="mt-0.5 h-4 w-4 shrink-0 text-flit-brand" aria-hidden />
                <div className="min-w-0">
                  <p className="text-xs font-medium text-[#59677D] dark:text-white/70">Consentimiento de tratamiento de datos</p>
                  <p className="mt-0.5 font-semibold text-flit-primary dark:text-white">
                    {detail.consentAt
                      ? `Aceptado el ${formatFechaHora(detail.consentAt)}${detail.consentTextVersion ? ` · texto ${detail.consentTextVersion}` : ''}`
                      : 'Sin constancia: el cliente aún no ha aceptado.'}
                  </p>
                </div>
              </div>
            </section>

            {mensaje && (
              <p
                role="status"
                className={`${MANUAL_AVISO_BASE} items-center font-semibold ${avisoConProblema ? MANUAL_AVISO.warning : MANUAL_AVISO.success}`}
              >
                {avisoConProblema ? (
                  <AlertTriangle className="h-4 w-4 shrink-0" aria-hidden />
                ) : (
                  <CheckCircle2 className="h-4 w-4 shrink-0" aria-hidden />
                )}{' '}
                {mensaje}
              </p>
            )}

            <EstadoDelCaso detail={detail} sinCapturas={sinCapturas} />

            {!sinCapturas && (
              <section aria-labelledby={`${id}-capturas`}>
                <div className="mb-2 flex flex-wrap items-baseline justify-between gap-2">
                  <h3 id={`${id}-capturas`} className="text-sm font-bold text-flit-primary dark:text-white">
                    Capturas del cliente
                  </h3>
                  <p className="text-xs text-[#59677D] dark:text-white/70">Toca una foto para verla en grande.</p>
                </div>
                <ManualImageGallery client={client} id={detail.id} images={detail.images} onViewerChange={onViewerChange} validacionFotos={detail.validacionFotos} />
              </section>
            )}
          </>
        )}
      </UiStateBoundary>
      <div className="sticky bottom-0 flex flex-wrap items-center justify-between gap-3 border-t border-[#DFE5ED] bg-white px-1 pb-1 pt-3 dark:border-white/10 dark:bg-[#162744]">
        <button type="button" onClick={onClose} className={MANUAL_BTN_SECUNDARIO}>
          Cerrar
        </button>
        {detail && (
          <ManualReviewActions
            detail={detail}
            client={client}
            onDialogChange={onDialogChange}
            onDone={(m) => {
              onDone(m);
              panelRef.current?.focus();
            }}
            onStale={() => {
              onStale();
              panelRef.current?.focus();
            }}
          />
        )}
      </div>
    </div>
  );
}
