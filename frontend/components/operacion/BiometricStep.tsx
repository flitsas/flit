'use client';

import { useCallback, useEffect, useState } from 'react';
import { Check, Copy, ExternalLink, RefreshCw, ShieldCheck, XCircle } from 'lucide-react';
import { QRCodeSVG } from 'qrcode.react';
import { tramitesClient } from '@/lib/api/tramites-client';
import { useWizardReadOnly } from './WizardReadOnlyContext';
import type {
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
  return (
    <fieldset
      className="rounded-xl border p-4"
      style={{ borderColor: '#DFE5ED' }}
      aria-label={`Biométrica ${PARTE_LABEL[parte]}`}
    >
      <legend className="px-1 text-xs font-bold">{PARTE_LABEL[parte]}</legend>

      {estado === 'aprobado' ? (
        <VerifiedView validation={validation!} />
      ) : estado === 'en_proceso' && validation?.captureUrl ? (
        <KyverumPendingView validation={validation} />
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
    </fieldset>
  );
}

/** Tarjeta verde "Identidad verificada — {score}/100" con el nombre de la parte. */
function VerifiedView({ validation: v }: { validation: BiometricValidation }) {
  return (
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
  );
}

/**
 * Validación Kyverum en curso: el enlace de captura ya se envió por correo al cliente. Se muestra
 * también aquí (link copiable + QR) para que el gestor pueda reenviarlo/mostrarlo si el correo no
 * llega. El estado se actualiza solo (polling) cuando llegue el webhook.
 */
function KyverumPendingView({ validation: v }: { validation: BiometricValidation }) {
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
