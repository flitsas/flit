'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  Check,
  Copy,
  ExternalLink,
  FileSignature,
  RefreshCw,
  RotateCcw,
  XCircle,
} from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { tramitesClient, getIdentitySendConflict } from '@/lib/api/tramites-client';
import { IdentityValidationTrackingPanel } from '@/components/atom/IdentityValidationTrackingPanel';
import { StatusBadge, type StatusTone } from '@/components/atom/StatusBadge';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type {
  BiometricEstado,
  BiometricParte,
  BiometricValidation,
  WizardModalidad,
} from '@/lib/api/types/procedure-runtime';

interface Props {
  instanceId: string | null;
  modalidad: WizardModalidad;
  /** Re-consulta el estado del wizard tras iniciar/refrescar (server-driven). */
  onRefresh?: () => void;
  /**
   * Oculta el párrafo introductorio cuando el contenedor ya describe el paso
   * (paso `identidad`: el h2 + subtítulo del wizard lo cubren). En `fur` NO se
   * oculta: ahí la biométrica es una subsección dentro de "Resumen del trámite".
   */
  hideIntro?: boolean;
  /**
   * Si se indica, solo renderiza las tarjetas de esas partes (p. ej. embeber
   * Comprador o Vendedor dentro del resumen del trámite).
   */
  onlyPartes?: BiometricParte[];
  /**
   * Paso Identidad: título + subtítulo dentro del mismo panel blanco que la captura.
   */
  heading?: string;
  headingSubtitle?: string;
  /**
   * HU #10646 — partes (NIT/jurídicas) cuya identidad quedó cubierta por la firma electrónica del baúl,
   * capturadas del outcome `firma_baul` de ensureIdentity durante el registro.
   *
   * <p>Refuerzo optimista, NO la fuente de verdad. Lo era hasta que se descubrió que, al reabrir el
   * trámite desde el listado, este estado en memoria llega vacío y la parte se rotulaba como «Identidad
   * verificada» aunque hubiera firmado por el baúl. La fuente es `firmaBaulPartes` del propio estado
   * biométrico, que desde la HU #11014 lo expone por parte y desde el Bug #11141 respeta además el
   * mecanismo elegido por el gestor. Se unen porque durante el registro la respuesta del servidor puede
   * ir un paso por detrás del outcome que el wizard acaba de recibir.</p>
   */
  vaultCoveredPartes?: BiometricParte[];
}

/**
 * Partes que requieren biométrica por modalidad.
 *
 * HU21 — saliente antes que entrante: el vendedor se muestra primero, igual que el resumen de
 * firmas del paso FUR (HU #11019), el expediente y el listado. Antes el resumen de identidad
 * era el único que invertía el orden.
 */
function partesFor(modalidad: WizardModalidad): BiometricParte[] {
  return modalidad === 'traspaso' ? ['vendedor', 'comprador'] : ['comprador'];
}

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

const KYVERUM = 'kyverum';

// CF-08 (Feature #11004, HU #11009) — etiquetas del historial (mismo vocabulario que Validaciones/
// Prevalidaciones e IdentityStatusPanel).
const ESTADO_LABEL: Record<BiometricEstado, string> = {
  enviado: 'Enviado',
  en_proceso: 'En proceso',
  aprobado: 'Aprobado',
  rechazado: 'Rechazado',
  expirado: 'Expirado',
  pendiente_envio: 'Pendiente de envío',
  error_envio: 'Error de envío',
};

const ESTADO_TONE: Record<BiometricEstado, StatusTone> = {
  enviado: 'info',
  en_proceso: 'warning',
  aprobado: 'success',
  rechazado: 'danger',
  expirado: 'neutral',
  pendiente_envio: 'info',
  error_envio: 'danger',
};

/** Formatea una fecha ISO a un texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/**
 * Paso de validación de identidad. Es provider-aware (HU #10233): con `kyverum` el clic dispara la
 * validación real (Kyverum captura remota + webhook), tomando los datos del actor del trámite y
 * mostrando el enlace de captura (link + QR) que también se envía por correo al cliente; el estado se
 * refresca solo (polling) hasta aprobado/rechazado. Con `mock` el clic simula la validación (score 95).
 * El status/gating lo decide el wizard server-driven: este paso refresca tras iniciar/simular.
 */
export function BiometricStep({
  instanceId,
  modalidad,
  onRefresh,
  hideIntro = false,
  onlyPartes,
  heading,
  headingSubtitle,
  vaultCoveredPartes = [],
}: Props) {
  const partes = onlyPartes?.length
    ? partesFor(modalidad).filter((p) => onlyPartes.includes(p))
    : partesFor(modalidad);
  // Solo lectura (Track C): sin iniciar/simular validación.
  const readOnly = useWizardReadOnly();

  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [provider, setProvider] = useState<string>('mock');
  // Partes cubiertas por el baúl según el BACKEND. Se consulta en vez de depender solo de la prop
  // porque esta última solo existe durante el registro; al reabrir el trámite llegaba vacía.
  const [firmaBaulServidor, setFirmaBaulServidor] = useState<string[]>([]);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!instanceId) return null;
    try {
      const state = await tramitesClient.getBiometricState(instanceId);
      setValidations(state.validations);
      setProvider(state.provider);
      setFirmaBaulServidor(state.firmaBaulPartes ?? []);
      setError(() => null);
      return state;
    } catch (err) {
      setError(() =>
        err instanceof Error ? err.message : 'Error al cargar las validaciones.',
      );
      return null;
    }
  }, [instanceId]);

  useEffect(() => {
    void load();
  }, [load]);

  // Kyverum es asíncrono: el resultado llega por webhook. Mientras haya una validación en proceso se
  // refresca solo cada 5s para reflejar aprobado/rechazado sin que el gestor tenga que recargar. Al
  // resolverse (ya no queda ninguna en_proceso) se notifica al wizard (onRefresh) para que recomponga
  // el gate server-driven y habilite "Continuar" sin requerir un clic manual en "Actualizar".
  useEffect(() => {
    if (provider !== KYVERUM) return;
    const pending = (validations ?? []).some((v) => v.status === 'en_proceso');
    if (!pending) return;
    const timer = setInterval(async () => {
      const state = await load();
      const stillPending = (state?.validations ?? []).some((v) => v.status === 'en_proceso');
      if (state && !stillPending) onRefresh?.();
    }, 5000);
    return () => clearInterval(timer);
  }, [provider, validations, load, onRefresh]);

  const handleRefresh = async () => {
    setLoading(true);
    try {
      await load();
    } finally {
      setLoading(false);
    }
    onRefresh?.();
  };

  // AC8 — 4 estados de la UI a partir de la carga del estado biométrico:
  //  • Cargando: aún no llegó la primera respuesta (validations === null) y no hubo error.
  //  • Error:    `error` con role="alert".
  //  • Lleno/Vacío: ya cargó → se pintan las tarjetas por parte (cada una resuelve su sub-estado:
  //    vacío = acción de iniciar; lleno = verificado/en proceso/rechazado).
  const initialLoading = instanceId != null && validations === null && error === null;

  // Feature #11211 — ocultar "Actualizar" global cuando todas las partes están resueltas
  // (baúl) o no queda biométrica pendiente de aprobación.
  const todasCoveredByVault =
    partes.length > 0 &&
    partes.every(
      (p) => firmaBaulServidor.includes(p) || vaultCoveredPartes.includes(p),
    );
  const algunaPendienteBiometria = partes.some((p) => {
    if (firmaBaulServidor.includes(p) || vaultCoveredPartes.includes(p)) return false;
    const matches = (validations ?? []).filter((v) =>
      modalidad === 'traspaso'
        ? v.partyRole === p
        : v.partyRole === null || v.partyRole === 'comprador',
    );
    const validation = matches.length > 0 ? matches[matches.length - 1] : null;
    return !validation || validation.status !== 'aprobado';
  });
  const showRefreshHeader =
    !readOnly && !todasCoveredByVault && (validations === null || algunaPendienteBiometria);

  // Paso Identidad: un solo panel blanco con título + subtítulo + Actualizar + tarjetas.
  const pagePanel = Boolean(heading);

  const refreshButton = showRefreshHeader ? (
    <button
      type="button"
      onClick={() => void handleRefresh()}
      disabled={loading || !instanceId}
      className="flex shrink-0 items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
      style={{ borderColor: '#557EFF', color: '#557EFF' }}
      aria-label="Actualizar estado biométrico"
    >
      <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} aria-hidden />
      Actualizar
    </button>
  ) : null;

  const partesContent = initialLoading ? (
    <BiometricSkeleton partes={partes} nested={pagePanel} />
  ) : (
    <div className="space-y-4">
      {partes.map((parte) => {
        const matches = (validations ?? []).filter((v) =>
          modalidad === 'traspaso'
            ? v.partyRole === parte
            : v.partyRole === null || v.partyRole === 'comprador',
        );
        const validation = matches.length > 0 ? matches[matches.length - 1] : null;
        return (
          <ParteCard
            key={parte}
            parte={parte}
            instanceId={instanceId}
            provider={provider}
            validation={validation}
            historial={matches}
            vaultCovered={
              firmaBaulServidor.includes(parte) || vaultCoveredPartes.includes(parte)
            }
            onChanged={() => void handleRefresh()}
            nested={pagePanel}
          />
        );
      })}
    </div>
  );

  if (pagePanel) {
    return (
      <div
        className="rounded-2xl border bg-white p-5 dark:bg-[#162744]"
        style={{ borderColor: '#DFE5ED' }}
      >
        <div className="mb-4 flex items-start justify-between gap-3">
          <div className="min-w-0">
            <h2 className="text-base font-bold" style={{ color: '#162744' }}>
              {heading}
            </h2>
            {headingSubtitle ? (
              <p className="mt-1 text-xs opacity-60">{headingSubtitle}</p>
            ) : null}
          </div>
          {refreshButton}
        </div>

        {error && (
          <div
            className="mb-4 rounded-xl border p-3 text-xs"
            style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
            role="alert"
            aria-live="polite"
          >
            {error}
          </div>
        )}

        {partesContent}
      </div>
    );
  }

  return (
    <div className="space-y-4">
      {/* En resumen (hideIntro) no se muestra la franja vacía con solo "Actualizar":
          el polling / "Actualizar estado" por tarjeta bastan. */}
      {!hideIntro && (
        <div
          className="flex items-start justify-between gap-3 rounded-2xl border bg-white px-4 py-3 dark:bg-[#162744]"
          style={{ borderColor: '#DFE5ED' }}
        >
          <p className="text-xs opacity-70">
            Validación de identidad de cada parte. Al iniciarla, el cliente recibe el enlace de captura
            por correo; el resultado se actualiza automáticamente.
          </p>
          {refreshButton}
        </div>
      )}

      {error && (
        <div
          className="rounded-xl border bg-white p-3 text-xs dark:bg-[#162744]"
          style={{ borderColor: '#FF4E00', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {partesContent}
    </div>
  );
}

/**
 * Estado de carga (AC8): placeholder accesible mientras llega la primera respuesta de
 * `getBiometricState`. Anuncia la carga a lectores de pantalla (role="status" + aria-live).
 */
function BiometricSkeleton({
  partes,
  nested = false,
}: {
  partes: BiometricParte[];
  nested?: boolean;
}) {
  return (
    <div className="space-y-4" role="status" aria-live="polite" aria-busy="true">
      <span className="sr-only">Cargando validaciones de identidad…</span>
      {partes.map((parte) => (
        <div
          key={parte}
          className={
            nested
              ? 'rounded-xl border p-4'
              : 'rounded-2xl border bg-white p-4 dark:bg-[#162744]'
          }
          style={{ borderColor: '#DFE5ED' }}
          aria-hidden="true"
        >
          <div className="mb-3 h-3 w-24 animate-pulse rounded bg-black/10 dark:bg-white/10" />
          <div className="h-12 w-full animate-pulse rounded-xl bg-black/5 dark:bg-white/5" />
        </div>
      ))}
    </div>
  );
}

/** Tarjeta por parte: enruta a la vista según el estado de la validación. */
function ParteCard({
  parte,
  instanceId,
  provider,
  validation,
  historial,
  vaultCovered,
  onChanged,
  nested = false,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  validation: BiometricValidation | null;
  /** CF-08 (Feature #11004, HU #11009) — todas las validaciones de la parte, orden cronológico. */
  historial: BiometricValidation[];
  vaultCovered: boolean;
  onChanged: () => void;
  /** Dentro del panel blanco del paso Identidad: sin segundo fondo blanco. */
  nested?: boolean;
}) {
  const estado = validation?.status;
  return (
    <fieldset
      className={
        nested
          ? 'rounded-xl border p-4'
          : 'rounded-2xl border bg-white p-5 dark:bg-[#162744]'
      }
      style={{ borderColor: '#DFE5ED' }}
      aria-label={`Biométrica ${PARTE_LABEL[parte]}`}
    >
      <legend
        className="px-2 text-xs font-bold"
        style={{ color: '#162744' }}
      >
        {PARTE_LABEL[parte]}
      </legend>

      {/* HU #10646 — actor jurídico (NIT) cubierto por la firma del baúl: la identidad ya está
          satisfecha server-side; se presenta como firma electrónica y se omite toda la biométrica. */}
      {vaultCovered ? (
        <VaultCoveredView />
      ) : estado === 'aprobado' ? (
        <VerifiedView validation={validation!} />
      ) : estado === 'en_proceso' && validation?.captureUrl && !validation.expired ? (
        // El enlace de captura solo se muestra si NO está vencido. Un enlace vencido (validation.expired:
        // backend `now > expiresAt`) cae a RejectedView aunque el estado siga en_proceso, para informar el
        // vencimiento y re-habilitar el botón de reenvío de inmediato (sin esperar a que el worker lo cambie).
        <KyverumPendingView
          validation={validation}
          instanceId={instanceId}
          onChanged={onChanged}
        />
      ) : estado === 'rechazado' || estado === 'expirado' || validation?.expired ? (
        <RejectedView
          validation={validation!}
          parte={parte}
          instanceId={instanceId}
          provider={provider}
          onChanged={onChanged}
        />
      ) : (
        <StartAction
          parte={parte}
          instanceId={instanceId}
          provider={provider}
          onStarted={onChanged}
        />
      )}

      {/* CF-08 (Feature #11004, HU #11009) — historial completo de la parte (ya NO se limita a
          matches[matches.length-1]); no aplica a la cobertura por baúl (no hay biométrica que auditar). */}
      {!vaultCovered && <HistorialValidaciones historial={historial} vigenteId={validation?.id ?? null} />}
    </fieldset>
  );
}

/**
 * CF-08 (Feature #11004, HU #11009) — "Historial de validaciones" de una parte: todas las filas que
 * `GET .../biometric` devuelve para esa parte, no solo la vigente/más reciente (que sigue siendo la
 * única con tarjeta de acción arriba). Cada ítem trae su propio tracking (bitácora) cuando es Kyverum
 * — mock no genera eventos de auditoría. Se omite por completo si la parte aún no tiene ninguna
 * validación (estado vacío, cubierto por `StartAction`).
 */
function HistorialValidaciones({
  historial,
  vigenteId,
}: {
  historial: BiometricValidation[];
  vigenteId: string | null;
}) {
  if (historial.length === 0) return null;

  // Con una sola validación no hay "historial" real que anunciar (es la misma que ya se ve en la
  // tarjeta de acción de arriba): se conserva únicamente el acceso a su tracking, igual que antes de
  // esta HU, sin el encabezado ni la fila de estado/fecha duplicada.
  if (historial.length === 1) {
    const [v] = historial;
    return v.provider === KYVERUM ? <IdentityValidationTrackingPanel validationId={v.id} /> : null;
  }

  return (
    <div className="mt-3 space-y-2 border-t pt-3">
      <p className="text-xs font-semibold opacity-70">
        Historial de validaciones ({historial.length})
      </p>
      <ul className="space-y-2">
        {historial.map((v) => (
          <li key={v.id} className="rounded-lg border p-2 text-xs" style={{ borderColor: '#EEF1F6' }}>
            <div className="flex flex-wrap items-center justify-between gap-2">
              <div className="flex items-center gap-1.5">
                <StatusBadge label={ESTADO_LABEL[v.status] ?? v.status} tone={ESTADO_TONE[v.status] ?? 'neutral'} />
                {v.id === vigenteId && (
                  <span
                    className="rounded-full px-1.5 py-0.5 text-xs font-semibold"
                    style={{ background: 'rgba(85,126,255,0.12)', color: '#557EFF' }}
                  >
                    Vigente
                  </span>
                )}
                {v.score != null && <span className="opacity-60">{v.score}/100</span>}
              </div>
              <span className="opacity-60">
                {formatFecha(v.validatedAt ?? v.expiresAt)}
              </span>
            </div>
            {v.provider === KYVERUM && <IdentityValidationTrackingPanel validationId={v.id} />}
          </li>
        ))}
      </ul>
    </div>
  );
}

/**
 * HU #10646 — estado "cubierto por el baúl": la parte es un actor jurídico (NIT) con firma electrónica
 * vigente en el baúl, así que su identidad queda satisfecha con esa firma y NO requiere biométrica. Se
 * presenta como "Firma electrónica (baúl)" — sin botones de iniciar/simular/reintentar validación.
 */
function VaultCoveredView() {
  return (
    <div
      className="flex items-center gap-3 rounded-xl p-3"
      style={{ background: 'rgba(85,126,255,0.10)', border: '1px solid rgba(85,126,255,0.35)' }}
      role="status"
      aria-live="polite"
    >
      <span
        className="flex h-9 w-9 items-center justify-center rounded-full shrink-0"
        style={{ background: '#557EFF', color: 'white' }}
        aria-hidden
      >
        <FileSignature className="h-5 w-5" />
      </span>
      <div className="space-y-0.5">
        <p className="text-xs font-bold" style={{ color: '#557EFF' }}>
          Firma electrónica (baúl)
        </p>
        <p className="text-xs opacity-70">
          Identidad cubierta por la firma electrónica del baúl. No requiere validación biométrica.
        </p>
      </div>
    </div>
  );
}

/** Tarjeta verde "Identidad verificada — {score}/100" con el nombre de la parte. */
function VerifiedView({ validation: v }: { validation: BiometricValidation }) {
  return (
    <div className="space-y-3">
      <div
        className="flex items-center gap-3 rounded-xl p-3"
        style={{ background: 'rgba(140,198,63,0.12)', border: '1px solid rgba(140,198,63,0.4)' }}
      >
        <span
          className="flex h-9 w-9 items-center justify-center rounded-full shrink-0"
          style={{ background: '#5B8A1F', color: 'white' }}
          aria-hidden
        >
          <Check className="h-5 w-5" />
        </span>
        <div className="space-y-0.5">
          <p className="text-xs font-bold" style={{ color: '#5B8A1F' }}>
            Identidad verificada — {v.score ?? 95}/100
          </p>
          <p className="text-xs opacity-70">{v.name}</p>
        </div>
      </div>
      {/*
       * El certificado oficial (PDF) NO se descarga aquí: la descarga vive en el flujo de
       * Generar/Re-generar FUR y de Consolidar (FirmaFurStep), donde el certificado es uno de los
       * documentos producidos con su propio botón de descarga.
       */}
    </div>
  );
}

/**
 * Validación Kyverum en curso: el enlace de captura ya se envió por correo al cliente. Se muestra
 * también aquí (link copiable + QR) para que el gestor pueda reenviarlo/mostrarlo si el correo no
 * llega. El estado se actualiza solo (polling) cuando llegue el webhook.
 */
function KyverumPendingView({
  validation: v,
  instanceId,
  onChanged,
}: {
  validation: BiometricValidation;
  instanceId: string | null;
  onChanged: () => void;
}) {
  const [copied, setCopied] = useState(false);
  const captureUrl = v.captureUrl!;

  const copy = async () => {
    try {
      await navigator.clipboard?.writeText(captureUrl);
      setCopied(true);
      setTimeout(() => setCopied(false), 2000);
    } catch {
      /* clipboard no disponible: el gestor puede copiar el link manualmente */
    }
  };

  return (
    <div className="space-y-3">
      <div className="flex items-center gap-2">
        <RefreshCw className="h-3.5 w-3.5 animate-spin" style={{ color: '#557EFF' }} aria-hidden />
        <p className="text-xs font-semibold" style={{ color: '#557EFF' }}>
          Esperando validación de {v.name}
        </p>
      </div>

      {/* Un intento no pasó pero la validación SIGUE abierta (Kyverum permite reintentar): se informa el motivo
          real y que el cliente puede volver a intentar en su móvil, sin marcar la validación como rechazada. */}
      {v.ultimoIntentoMotivo && (
        <div
          className="rounded-xl p-2.5 text-xs"
          style={{ background: 'rgba(178,106,0,0.08)', border: '1px solid rgba(178,106,0,0.3)', color: '#B26A00' }}
          role="status"
          aria-live="polite"
        >
          <span className="font-semibold">
            Intento {v.intentos} de {v.maxIntentos} no pasó.
          </span>{' '}
          {v.ultimoIntentoMotivo}{' '}
          {v.maxIntentos - v.intentos > 0
            ? `Quedan ${v.maxIntentos - v.intentos} intento(s): el cliente puede reintentar en su móvil (aquí se actualiza al aprobar o al agotar los intentos).`
            : ''}
        </div>
      )}

      <p className="text-xs opacity-70">
        Enviamos el enlace de captura al correo del cliente ({v.email}). También puedes compartirlo:
      </p>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="rounded-xl border bg-white p-2">
          <QRCodeSVG value={captureUrl} size={120} aria-label="Código QR del enlace de captura" />
        </div>
        <div className="min-w-0 flex-1 space-y-2">
          {/* AC2: CTA explícito para abrir la captura en una pestaña nueva (target=_blank, rel=noopener). */}
          <a
            href={captureUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="flex w-fit items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ background: '#557EFF' }}
          >
            <ExternalLink className="h-3 w-3" aria-hidden />
            Abrir captura Kyverum
          </a>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => void copy()}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
              style={{ borderColor: '#557EFF', color: '#557EFF' }}
              aria-label="Copiar enlace de captura"
            >
              <Copy className="h-3 w-3" aria-hidden />
              {copied ? 'Copiado' : 'Copiar enlace'}
            </button>
          </div>
          {/* El enlace literal queda como referencia (también se envió al correo del cliente). */}
          <a
            href={captureUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="block truncate text-xs underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ color: '#557EFF' }}
            title={captureUrl}
          >
            {captureUrl}
          </a>
        </div>
      </div>

      <ReconcileAction instanceId={instanceId} validationId={v.id} onReconciled={onChanged} />
    </div>
  );
}

/**
 * Acción "Actualizar estado": reconcilia la validación consultando su estado real a Kyverum (self-heal
 * si el webhook no llegó). El resultado normalmente llega solo por webhook en segundos, así que el botón
 * aparece únicamente si la validación sigue colgada tras ~15s, para no invitar a consultas innecesarias.
 * Si la consulta la resuelve (`updated`), notifica al wizard para recomponer el gate.
 */
function ReconcileAction({
  instanceId,
  validationId,
  onReconciled,
}: {
  instanceId: string | null;
  validationId: string;
  onReconciled: () => void;
}) {
  const readOnly = useWizardReadOnly();
  const [visible, setVisible] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [info, setInfo] = useState<string | null>(null);

  useEffect(() => {
    const timer = setTimeout(() => setVisible(true), 15000);
    return () => clearTimeout(timer);
  }, []);

  if (readOnly || !visible) return null;

  const handleReconcile = async () => {
    if (!instanceId) return;
    setError(null);
    setInfo(null);
    setSubmitting(true);
    try {
      const res = await tramitesClient.reconcileBiometric(instanceId, validationId);
      if (res.updated) onReconciled();
      else setInfo('El proveedor aún no resuelve la validación. Intenta de nuevo en unos segundos.');
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo actualizar el estado.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <div className="space-y-1.5 border-t pt-3">
      <p className="text-xs opacity-60">
        ¿La captura ya se completó y sigue en espera? Consulta el estado directamente al proveedor.
      </p>
      <button
        type="button"
        onClick={() => void handleReconcile()}
        disabled={submitting || !instanceId}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-xs font-semibold border disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label="Actualizar estado de la validación consultando al proveedor"
      >
        <RotateCcw className={`h-3 w-3 ${submitting ? 'animate-spin' : ''}`} aria-hidden />
        {submitting ? 'Consultando…' : 'Actualizar estado'}
      </button>
      {info && (
        <p className="text-xs opacity-70" role="status" aria-live="polite">
          {info}
        </p>
      )}
      {error && (
        <p className="text-xs" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
          {error}
        </p>
      )}
    </div>
  );
}

/** Validación rechazada/expirada: explica el motivo y permite reenviar (nueva validación). */
function RejectedView({
  validation: v,
  parte,
  instanceId,
  provider,
  onChanged,
}: {
  validation: BiometricValidation;
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onChanged: () => void;
}) {
  const expirado = v.status === 'expirado' || v.expired;
  return (
    <div className="space-y-3">
      <div
        className="flex items-start gap-2 rounded-xl p-3"
        style={{ background: 'rgba(255,78,0,0.06)', border: '1px solid rgba(255,78,0,0.3)' }}
        role="alert"
        aria-live="polite"
      >
        <XCircle className="mt-0.5 h-4 w-4 shrink-0" style={{ color: '#FF4E00' }} aria-hidden />
        <div className="space-y-1">
          <p className="text-xs font-semibold" style={{ color: '#FF4E00' }}>
            {expirado
              ? 'El enlace de validación expiró.'
              : `Validación no aprobada${v.score != null ? ` (${v.score}/100)` : ''}.`}
          </p>
          {/* AC5 — expiración: muestra cuándo venció el enlace. */}
          {expirado && v.expiresAt && (
            <p className="text-xs opacity-80">Venció el {formatFecha(v.expiresAt)}.</p>
          )}
          {/* AC4 — rechazo con motivo: detalle sanitizado del proveedor (sin PII). */}
          {!expirado && v.rejectionReason && (
            <p className="text-xs opacity-80">Motivo: {v.rejectionReason}</p>
          )}
        </div>
      </div>
      <StartAction
        parte={parte}
        instanceId={instanceId}
        provider={provider}
        onStarted={onChanged}
        // AC4: rechazo → "Reintentar validación". AC5: expiración → "Reiniciar validación".
        label={expirado ? 'Reiniciar validación' : 'Reintentar validación'}
      />
    </div>
  );
}

/**
 * Acción provider-aware para (re)iniciar la validación de una parte. Kyverum → validación real (toma
 * los datos del actor del trámite, solo se envía la parte). Mock → simula la validación (score 95).
 */
function StartAction({
  parte,
  instanceId,
  provider,
  onStarted,
  label,
  actorEmail,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onStarted: () => void;
  label?: string;
  /** HU #11267 AC3 — correo del destinatario mostrado en la confirmación. */
  actorEmail?: string | null;
}) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [conflictMsg, setConflictMsg] = useState<string | null>(null);
  const readOnly = useWizardReadOnly();

  const isKyverum = provider === KYVERUM;
  const buttonLabel =
    label ?? (isKyverum ? 'Validar identidad' : 'Simular validación de identidad');

  const doStart = async () => {
    if (!instanceId) return;
    setError(null);
    setConflictMsg(null);
    setSubmitting(true);
    try {
      if (isKyverum) {
        await tramitesClient.iniciarBiometric(instanceId, { parte });
      } else {
        await tramitesClient.simulateBiometric(instanceId, { parte });
      }
      setConfirmOpen(false);
      onStarted();
    } catch (err) {
      const conflict = getIdentitySendConflict(err);
      if (conflict) {
        setConfirmOpen(false);
        const hasta = conflict.validUntil
          ? new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(new Date(conflict.validUntil))
          : null;
        setConflictMsg(
          conflict.motivo === 'identidad_vigente'
            ? `Ya validada${hasta ? ` · vigente hasta el ${hasta}` : ''}. Se reutiliza la identidad existente.`
            : 'Ya hay una validación en curso para esta persona. No se envió un correo nuevo.',
        );
      } else {
        setError(
          err instanceof Error ? err.message : 'No se pudo iniciar la validación.',
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const handleStart = () => {
    if (!instanceId) return;
    // AC3 — confirmación solo en disparadores con UI (no en el auto del wizard).
    setConfirmOpen(true);
  };

  // En solo lectura no se inicia: solo se informa que la identidad quedó pendiente.
  if (readOnly) {
    return (
      <p className="text-xs opacity-60">Validación de identidad pendiente.</p>
    );
  }

  return (
    <div className="space-y-3">
      {!label && (
        <p className="text-xs font-medium opacity-70">
          Aún no se ha iniciado la validación de identidad de esta parte.
        </p>
      )}
      <p className="text-xs opacity-60">
        {isKyverum
          ? 'Inicia la validación: el cliente recibirá el enlace de captura por correo y aquí podrás compartir el enlace/QR.'
          : 'Mock de esta iteración: simula la validación biométrica de esta parte.'}
      </p>
      {conflictMsg && (
        <p className="rounded-lg border px-2 py-1.5 text-xs" style={{ borderColor: '#5B8A1F', color: '#3F5F14' }} role="status">
          {conflictMsg}
        </p>
      )}
      {error && (
        <p className="text-xs text-[#FF4E00]" role="alert">{error}</p>
      )}
      {!conflictMsg && (
        <button
          type="button"
          onClick={handleStart}
          disabled={submitting || !instanceId}
          className="rounded-xl px-3 py-2 text-xs font-semibold text-white disabled:opacity-50"
          style={{ background: '#557EFF' }}
        >
          {buttonLabel}
        </button>
      )}
      {confirmOpen && (
        <div
          className="rounded-xl border p-3 text-xs"
          style={{ borderColor: '#557EFF' }}
          role="alertdialog"
          aria-label="Confirmar envío de validación"
        >
          <p>
            Se enviará el enlace a{' '}
            <strong>{actorEmail?.trim() || 'el correo registrado de la parte'}</strong>. ¿Continuar?
          </p>
          <div className="mt-2 flex gap-2">
            <button type="button" className="rounded-lg border px-2 py-1" onClick={() => setConfirmOpen(false)} disabled={submitting}>
              Cancelar
            </button>
            <button
              type="button"
              className="rounded-lg px-2 py-1 font-semibold text-white disabled:opacity-50"
              style={{ background: '#557EFF' }}
              onClick={() => void doStart()}
              disabled={submitting}
            >
              {submitting ? 'Enviando…' : 'Confirmar y enviar'}
            </button>
          </div>
        </div>
      )}
    </div>
  );
}
