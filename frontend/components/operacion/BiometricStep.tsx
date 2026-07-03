'use client';

import { useCallback, useEffect, useState } from 'react';
import {
  Check,
  ChevronRight,
  Copy,
  Download,
  ExternalLink,
  RefreshCw,
  RotateCcw,
  ShieldCheck,
  XCircle,
} from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { getToken } from '@/lib/api/client';
import { decodeJwtPayload, isSuperAdmin } from '@/lib/auth/jwt';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type {
  BiometricParte,
  BiometricValidation,
  IdentityAuditEvent,
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
   * oculta: ahí la biométrica es una subsección dentro de "Generar FUR".
   */
  hideIntro?: boolean;
}

/** Partes que requieren biométrica por modalidad. */
function partesFor(modalidad: WizardModalidad): BiometricParte[] {
  return modalidad === 'traspaso' ? ['comprador', 'vendedor'] : ['comprador'];
}

const PARTE_LABEL: Record<BiometricParte, string> = {
  comprador: 'Comprador',
  vendedor: 'Vendedor',
};

const KYVERUM = 'kyverum';

/** Formatea una fecha ISO a un texto legible (es-CO). Devuelve el ISO crudo si no parsea. */
function formatFecha(iso: string | null | undefined): string {
  if (!iso) return '';
  const d = new Date(iso);
  if (Number.isNaN(d.getTime())) return iso;
  return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium', timeStyle: 'short' }).format(d);
}

/**
 * ¿El usuario actual es SuperAdmin? Se resuelve del JWT en cliente (tras montar, para no romper la
 * hidratación SSR). Gatea la bitácora técnica de la validación, que trae detalle de soporte
 * (descifrado, error_type, firma) que no debe ver un gestor normal.
 */
function useIsSuperAdmin(): boolean {
  const [is, setIs] = useState(false);
  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect
    setIs(isSuperAdmin(decodeJwtPayload(getToken())));
  }, []);
  return is;
}

/**
 * Paso de validación de identidad. Es provider-aware (HU #10233): con `kyverum` el clic dispara la
 * validación real (Kyverum captura remota + webhook), tomando los datos del actor del trámite y
 * mostrando el enlace de captura (link + QR) que también se envía por correo al cliente; el estado se
 * refresca solo (polling) hasta aprobado/rechazado. Con `mock` el clic simula la validación (score 95).
 * El status/gating lo decide el wizard server-driven: este paso refresca tras iniciar/simular.
 */
export function BiometricStep({ instanceId, modalidad, onRefresh, hideIntro = false }: Props) {
  const partes = partesFor(modalidad);
  // Solo lectura (Track C): sin iniciar/simular validación.
  const readOnly = useWizardReadOnly();

  const [validations, setValidations] = useState<BiometricValidation[] | null>(null);
  const [provider, setProvider] = useState<string>('mock');
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    if (!instanceId) return null;
    try {
      const state = await tramitesClient.getBiometricState(instanceId);
      setValidations(state.validations);
      setProvider(state.provider);
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

  return (
    <div className="space-y-4">
      <div className="flex items-start justify-between gap-3">
        {hideIntro ? (
          <span />
        ) : (
          <p className="text-xs opacity-70">
            Validación de identidad de cada parte. Al iniciarla, el cliente recibe el enlace de captura
            por correo; el resultado se actualiza automáticamente.
          </p>
        )}
        {!readOnly && (
          <button
            type="button"
            onClick={() => void handleRefresh()}
            disabled={loading || !instanceId}
            className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border shrink-0 disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ borderColor: '#557EFF', color: '#557EFF' }}
            aria-label="Actualizar estado biométrico"
          >
            <RefreshCw className={`h-3 w-3 ${loading ? 'animate-spin' : ''}`} aria-hidden />
            Actualizar
          </button>
        )}
      </div>

      {error && (
        <div
          className="rounded-xl p-3 text-xs border"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {error}
        </div>
      )}

      {initialLoading ? (
        <BiometricSkeleton partes={partes} />
      ) : (
        <div className="space-y-4">
          {partes.map((parte) => {
            // Más reciente para la parte (el backend ordena por created_at asc): refleja el estado actual
            // tras posibles reintentos (rechazado → nueva validación).
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
                onChanged={() => void handleRefresh()}
              />
            );
          })}
        </div>
      )}
    </div>
  );
}

/**
 * Estado de carga (AC8): placeholder accesible mientras llega la primera respuesta de
 * `getBiometricState`. Anuncia la carga a lectores de pantalla (role="status" + aria-live).
 */
function BiometricSkeleton({ partes }: { partes: BiometricParte[] }) {
  return (
    <div className="space-y-4" role="status" aria-live="polite" aria-busy="true">
      <span className="sr-only">Cargando validaciones de identidad…</span>
      {partes.map((parte) => (
        <div
          key={parte}
          className="rounded-xl border p-4"
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
  onChanged,
}: {
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  validation: BiometricValidation | null;
  onChanged: () => void;
}) {
  const estado = validation?.status;
  const isAdmin = useIsSuperAdmin();
  // La bitácora solo aplica a validaciones Kyverum (mock no genera eventos) y solo para soporte.
  const showAudit = isAdmin && validation != null && validation.provider === KYVERUM;
  return (
    <fieldset
      className="rounded-xl border p-4"
      style={{ borderColor: '#DFE5ED' }}
      aria-label={`Biométrica ${PARTE_LABEL[parte]}`}
    >
      <legend className="px-1 text-xs font-bold">{PARTE_LABEL[parte]}</legend>

      {estado === 'aprobado' ? (
        <VerifiedView validation={validation!} instanceId={instanceId} />
      ) : estado === 'en_proceso' && validation?.captureUrl ? (
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

      {showAudit && (
        <IdentityAuditPanel instanceId={instanceId} validationId={validation!.id} />
      )}
    </fieldset>
  );
}

/** Tarjeta verde "Identidad verificada — {score}/100" con el nombre de la parte. */
function VerifiedView({
  validation: v,
  instanceId,
}: {
  validation: BiometricValidation;
  instanceId: string | null;
}) {
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
          <p className="text-[11px] opacity-70">{v.name}</p>
        </div>
      </div>
      {/* El certificado oficial (PDF) solo existe para Kyverum; el mock no lo emite. */}
      {v.provider === KYVERUM && (
        <CertificadoButton instanceId={instanceId} validationId={v.id} />
      )}
    </div>
  );
}

/**
 * Descarga best-effort del certificado oficial (PDF) de una validación aprobada. Reusa el patrón
 * blob → objectURL → anchor de ExpedienteVisor. Si Kyverum no tiene el certificado (404) o falla
 * el proveedor, se muestra el mensaje inline sin bloquear el resto del paso.
 */
function CertificadoButton({
  instanceId,
  validationId,
}: {
  instanceId: string | null;
  validationId: string;
}) {
  const [downloading, setDownloading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const handleDownload = async () => {
    if (!instanceId) return;
    setError(null);
    setDownloading(true);
    try {
      const { blob, filename } = await tramitesClient.downloadBiometricCertificado(
        instanceId,
        validationId,
      );
      const url = URL.createObjectURL(blob);
      const a = document.createElement('a');
      a.href = url;
      a.download = filename;
      document.body.appendChild(a);
      a.click();
      a.remove();
      URL.revokeObjectURL(url);
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo descargar el certificado.',
      );
    } finally {
      setDownloading(false);
    }
  };

  return (
    <div className="space-y-1.5">
      <button
        type="button"
        onClick={() => void handleDownload()}
        disabled={downloading || !instanceId}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ borderColor: '#5B8A1F', color: '#5B8A1F' }}
        aria-label="Descargar certificado de identidad en PDF"
      >
        {downloading ? (
          <RefreshCw className="h-3 w-3 animate-spin" aria-hidden />
        ) : (
          <Download className="h-3 w-3" aria-hidden />
        )}
        {downloading ? 'Descargando…' : 'Descargar certificado'}
      </button>
      {error && (
        <p className="text-[11px]" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
          {error}
        </p>
      )}
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
          className="rounded-xl p-2.5 text-[11px]"
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

      <p className="text-[11px] opacity-70">
        Enviamos el enlace de captura al correo del cliente ({v.email}). También puedes compartirlo:
      </p>

      <div className="flex flex-col gap-3 sm:flex-row sm:items-center">
        <div className="rounded-xl border bg-white p-2" style={{ borderColor: '#DFE5ED' }}>
          <QRCodeSVG value={captureUrl} size={120} aria-label="Código QR del enlace de captura" />
        </div>
        <div className="min-w-0 flex-1 space-y-2">
          {/* AC2: CTA explícito para abrir la captura en una pestaña nueva (target=_blank, rel=noopener). */}
          <a
            href={captureUrl}
            target="_blank"
            rel="noopener noreferrer"
            className="flex w-fit items-center gap-1.5 rounded-xl px-3 py-1.5 text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ background: '#557EFF' }}
          >
            <ExternalLink className="h-3 w-3" aria-hidden />
            Abrir captura Kyverum
          </a>
          <div className="flex flex-wrap items-center gap-2">
            <button
              type="button"
              onClick={() => void copy()}
              className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
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
            className="block truncate text-[11px] underline focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
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
    <div className="space-y-1.5 border-t pt-3" style={{ borderColor: '#DFE5ED' }}>
      <p className="text-[11px] opacity-60">
        ¿La captura ya se completó y sigue en espera? Consulta el estado directamente al proveedor.
      </p>
      <button
        type="button"
        onClick={() => void handleReconcile()}
        disabled={submitting || !instanceId}
        className="flex items-center gap-1.5 px-3 py-1.5 rounded-xl text-[11px] font-semibold border disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ borderColor: '#557EFF', color: '#557EFF' }}
        aria-label="Actualizar estado de la validación consultando al proveedor"
      >
        <RotateCcw className={`h-3 w-3 ${submitting ? 'animate-spin' : ''}`} aria-hidden />
        {submitting ? 'Consultando…' : 'Actualizar estado'}
      </button>
      {info && (
        <p className="text-[11px] opacity-70" role="status" aria-live="polite">
          {info}
        </p>
      )}
      {error && (
        <p className="text-[11px]" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
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
          <p className="text-[11px] font-semibold" style={{ color: '#FF4E00' }}>
            {expirado
              ? 'El enlace de validación expiró.'
              : `Validación no aprobada${v.score != null ? ` (${v.score}/100)` : ''}.`}
          </p>
          {/* AC5 — expiración: muestra cuándo venció el enlace. */}
          {expirado && v.expiresAt && (
            <p className="text-[11px] opacity-80">Venció el {formatFecha(v.expiresAt)}.</p>
          )}
          {/* AC4 — rechazo con motivo: detalle sanitizado del proveedor (sin PII). */}
          {!expirado && v.rejectionReason && (
            <p className="text-[11px] opacity-80">Motivo: {v.rejectionReason}</p>
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
}: {
  parte: BiometricParte;
  instanceId: string | null;
  provider: string;
  onStarted: () => void;
  label?: string;
}) {
  const [submitting, setSubmitting] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const readOnly = useWizardReadOnly();

  const isKyverum = provider === KYVERUM;
  const buttonLabel =
    label ?? (isKyverum ? 'Validar identidad' : 'Simular validación de identidad');

  const handleStart = async () => {
    if (!instanceId) return;
    setError(null);
    setSubmitting(true);
    try {
      if (isKyverum) {
        // Solo la parte: el backend resuelve nombre/documento/email del actor del trámite.
        await tramitesClient.iniciarBiometric(instanceId, { parte });
      } else {
        await tramitesClient.simulateBiometric(instanceId, { parte });
      }
      onStarted();
    } catch (err) {
      setError(
        err instanceof Error ? err.message : 'No se pudo iniciar la validación.',
      );
    } finally {
      setSubmitting(false);
    }
  };

  // En solo lectura no se inicia: solo se informa que la identidad quedó pendiente.
  if (readOnly) {
    return (
      <p className="text-[11px] opacity-60">Validación de identidad pendiente.</p>
    );
  }

  return (
    <div className="space-y-3">
      {/* AC8 — estado vacío explícito: sin label (no es reintento) aún no hay validación para la parte. */}
      {!label && (
        <p className="text-[11px] font-medium opacity-70">
          Aún no se ha iniciado la validación de identidad de esta parte.
        </p>
      )}
      <p className="text-[11px] opacity-60">
        {isKyverum
          ? 'Inicia la validación: el cliente recibirá el enlace de captura por correo y aquí podrás compartir el enlace/QR.'
          : 'Mock de esta iteración: simula la validación biométrica de esta parte.'}
      </p>

      {error && (
        <p className="text-[11px] font-medium" style={{ color: '#FF4E00' }} role="alert">
          {error}
        </p>
      )}

      <button
        type="button"
        onClick={() => void handleStart()}
        disabled={submitting || !instanceId}
        className="flex items-center gap-2 px-5 py-2 rounded-xl text-xs font-semibold text-white disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        style={{ background: '#557EFF' }}
        aria-label={buttonLabel}
      >
        {submitting ? (
          <RefreshCw className="h-3.5 w-3.5 animate-spin" aria-hidden />
        ) : (
          <ShieldCheck className="h-3.5 w-3.5" aria-hidden />
        )}
        {submitting ? 'Procesando…' : buttonLabel}
      </button>
    </div>
  );
}

/** Texto del resultado de un evento de bitácora, anexando el código HTTP si lo hay. */
function auditOutcomeLabel(e: IdentityAuditEvent): string {
  return e.httpStatus != null ? `${e.outcome} (HTTP ${e.httpStatus})` : e.outcome;
}

/**
 * Bitácora técnica (solo soporte/SuperAdmin) del ciclo de una validación de identidad. Disclosure
 * colapsable que carga los eventos bajo demanda (`GET .../audit`): envío, llegada del webhook, si
 * descifró el secreto, firma, resultado y reconciliaciones. Diagnóstico de "qué pasó" sin entrar a la
 * BD ni a los logs del pod. Sin PII ni secretos (el backend ya sanea).
 */
function IdentityAuditPanel({
  instanceId,
  validationId,
}: {
  instanceId: string | null;
  validationId: string;
}) {
  const [open, setOpen] = useState(false);
  const [events, setEvents] = useState<IdentityAuditEvent[] | null>(null);
  const [loading, setLoading] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const loadAudit = useCallback(async () => {
    if (!instanceId) return;
    setLoading(true);
    setError(null);
    try {
      const res = await tramitesClient.getBiometricAudit(instanceId, validationId);
      setEvents(res.events);
    } catch (err) {
      setError(err instanceof Error ? err.message : 'No se pudo cargar la bitácora.');
    } finally {
      setLoading(false);
    }
  }, [instanceId, validationId]);

  const toggle = () => {
    const next = !open;
    setOpen(next);
    // Carga perezosa: solo la primera vez que se abre (o si quedó sin datos por un error previo).
    if (next && events === null && !loading) void loadAudit();
  };

  return (
    <div className="mt-3 border-t pt-3" style={{ borderColor: '#DFE5ED' }}>
      <button
        type="button"
        onClick={toggle}
        className="flex items-center gap-1.5 text-[11px] font-semibold opacity-70 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
        aria-expanded={open}
        aria-label="Historial técnico de la validación (soporte)"
      >
        <ChevronRight
          className={`h-3 w-3 transition-transform ${open ? 'rotate-90' : ''}`}
          aria-hidden
        />
        Historial técnico (soporte)
      </button>

      {open && (
        <div className="mt-2 space-y-2">
          {loading && (
            <p className="text-[11px] opacity-60" role="status" aria-live="polite">
              Cargando bitácora…
            </p>
          )}
          {error && (
            <p className="text-[11px]" style={{ color: '#FF4E00' }} role="alert" aria-live="polite">
              {error}
            </p>
          )}
          {events && events.length === 0 && (
            <p className="text-[11px] opacity-60">Sin eventos registrados todavía.</p>
          )}
          {events && events.length > 0 && (
            <div className="overflow-x-auto">
              <table className="w-full text-left text-[10.5px]">
                <thead>
                  <tr className="opacity-60">
                    <th className="py-1 pr-2 font-semibold">Fecha</th>
                    <th className="py-1 pr-2 font-semibold">Etapa</th>
                    <th className="py-1 pr-2 font-semibold">Resultado</th>
                    <th className="py-1 pr-2 font-semibold">Cifrado</th>
                    <th className="py-1 pr-2 font-semibold">Detalle</th>
                  </tr>
                </thead>
                <tbody>
                  {events.map((e, i) => (
                    <tr
                      key={i}
                      className="border-t align-top"
                      style={{ borderColor: '#EEF1F6' }}
                    >
                      <td className="whitespace-nowrap py-1 pr-2 opacity-70">
                        {formatFecha(e.occurredAt)}
                      </td>
                      <td className="py-1 pr-2 font-medium">{e.stage}</td>
                      <td className="py-1 pr-2">{auditOutcomeLabel(e)}</td>
                      <td className="py-1 pr-2">
                        {e.decryptOk == null ? '—' : e.decryptOk ? 'OK' : 'Falló'}
                      </td>
                      <td className="py-1 pr-2 opacity-70">
                        {e.errorType ?? e.providerStatus ?? e.message ?? ''}
                      </td>
                    </tr>
                  ))}
                </tbody>
              </table>
            </div>
          )}
          <button
            type="button"
            onClick={() => void loadAudit()}
            disabled={loading || !instanceId}
            className="flex items-center gap-1 text-[10.5px] font-semibold disabled:opacity-50 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
            style={{ color: '#557EFF' }}
            aria-label="Refrescar bitácora"
          >
            <RefreshCw className={`h-2.5 w-2.5 ${loading ? 'animate-spin' : ''}`} aria-hidden />
            Refrescar
          </button>
        </div>
      )}
    </div>
  );
}
