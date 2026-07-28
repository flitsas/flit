'use client';

import { useState } from 'react';
import { AlertCircle, AlertTriangle, CheckCircle2, ExternalLink, Loader2, Lock, X } from 'lucide-react';
import { tramitesClient, TramitesApiError } from '@/lib/api/tramites-client';
import type {
  EditarPrevalidacionRequest,
  EditarPrevalidacionResult,
  TenantBiometricValidation,
} from '@/lib/api/types/procedure-runtime';

/**
 * Formulario de edición de una prevalidación STANDALONE (HU #10944 — Feature #10864, CF-03).
 *
 * Solo `nombre` y `correo` (titular) y, opcionalmente, `nombre`/`correo` del representante legal
 * son editables (D7). El tipo/número de documento se muestran DESHABILITADOS con la razón visible
 * (definen la identidad) — nunca se envían al backend desde esta pantalla.
 *
 * Límite de contrato conocido (documentado, no inventado): el listado tenant-wide
 * (`TenantBiometricValidationDto` / GET /biometric-validations) no expone el correo actual ni si
 * la persona es jurídica (personType/legalRep* no viajan en esa fila). Por eso el campo de correo
 * arranca VACÍO ("dejar en blanco = no cambiarlo", igual semántica que el backend) y la sección de
 * representante legal es SIEMPRE un bloque opcional (no condicionado a un personType que el
 * frontend no puede conocer con los contratos actuales): si la persona es natural, el backend
 * ignora legalRepName/legalRepEmail sin efecto (ver EditarPrevalidacionHandler, D7/D8). Se
 * recomienda al architecture-agent sumar personType/email a la fila del listado en una futura
 * ampliación de contrato para precargar estos campos.
 */
export interface PrevalidacionEditFormProps {
  row: TenantBiometricValidation;
  onClose: () => void;
  onSaved: (result: EditarPrevalidacionResult) => void;
  /** HU #10944 AC5 — informa al padre que se detectó un 429 (cooldown/tope) para reflejarlo en la fila. */
  onRateLimited?: (info: RateLimitInfo) => void;
  /** HU #10944 AC4/D9 — informa al padre que la identidad ya está aprobada (409), para ofrecer "Nueva prevalidación". */
  onIdentidadAprobada?: () => void;
}

export interface RateLimitInfo {
  cooldownMinutes?: number;
  maxedOut?: boolean;
}

const EMAIL_RE = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

/**
 * Extrae del texto de error (ya en español, tal cual lo entrega el backend vía ProblemDetails.detail
 * — D10) los minutos de cooldown restantes o si se agotó el tope de reenvíos. Fuente única del
 * mensaje: el backend; esta función solo detecta la SEÑAL para poder deshabilitar el botón, no
 * reformula el texto mostrado al operador.
 */
export function parseRateLimitDetail(message: string): RateLimitInfo {
  const cooldownMatch = /espera\s+(\d+)\s+minuto/i.exec(message);
  if (cooldownMatch) {
    return { cooldownMinutes: Number(cooldownMatch[1]) };
  }
  if (/agotaron los reenv[ií]os/i.test(message)) {
    return { maxedOut: true };
  }
  return {};
}

export function PrevalidacionEditForm({
  row,
  onClose,
  onSaved,
  onRateLimited,
  onIdentidadAprobada,
}: PrevalidacionEditFormProps) {
  const [name, setName] = useState(row.name);
  const [email, setEmail] = useState('');
  const [showLegalRep, setShowLegalRep] = useState(false);
  const [legalRepName, setLegalRepName] = useState('');
  const [legalRepEmail, setLegalRepEmail] = useState('');
  const [submitting, setSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  const [offerNueva, setOfferNueva] = useState(false);

  const emailTrim = email.trim();
  const legalRepEmailTrim = legalRepEmail.trim();
  const emailInvalid = emailTrim !== '' && !EMAIL_RE.test(emailTrim);
  const legalRepEmailInvalid = legalRepEmailTrim !== '' && !EMAIL_RE.test(legalRepEmailTrim);
  const willResend = emailTrim !== '' || legalRepEmailTrim !== '';
  const hasErrors = emailInvalid || legalRepEmailInvalid;

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    if (hasErrors) return;

    setSubmitting(true);
    setApiError(null);
    setOfferNueva(false);
    try {
      const body: EditarPrevalidacionRequest = {};
      const nameTrim = name.trim();
      if (nameTrim !== '' && nameTrim !== row.name) body.name = nameTrim;
      if (emailTrim !== '') body.email = emailTrim;
      if (legalRepName.trim() !== '') body.legalRepName = legalRepName.trim();
      if (legalRepEmailTrim !== '') body.legalRepEmail = legalRepEmailTrim;

      const result = await tramitesClient.editPrevalidacion(row.id, body);
      onSaved(result);
    } catch (err) {
      if (err instanceof TramitesApiError) {
        setApiError(err.message);
        if (err.status === 409 && /aprobada/i.test(err.message)) {
          setOfferNueva(true);
          onIdentidadAprobada?.();
        }
        if (err.status === 429) {
          onRateLimited?.(parseRateLimitDetail(err.message));
        }
      } else {
        setApiError(err instanceof Error ? err.message : 'No se pudo guardar la edición.');
      }
    } finally {
      setSubmitting(false);
    }
  };

  const fieldClass =
    'w-full rounded-xl border px-3 py-2 text-sm outline-none transition focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:opacity-60 border-[#DDE5F0]';

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="pv-edit-title"
    >
      <div className="w-full max-w-lg rounded-2xl bg-white shadow-xl dark:bg-[#0B0F14]">
        <div className="flex items-center justify-between border-b px-6 py-4">
          <h2 id="pv-edit-title" className="text-base font-semibold text-[#162744] dark:text-white">
            Editar prevalidación
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar formulario de edición"
            className="rounded-lg p-1 hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:hover:bg-white/10"
          >
            <X className="h-5 w-5 opacity-60" aria-hidden="true" />
          </button>
        </div>

        <form onSubmit={(e) => void handleSubmit(e)} noValidate>
          <div className="space-y-4 overflow-y-auto max-h-[70vh] px-6 py-5">
            {/* Documento — NO editable (D7) */}
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="pv-edit-docType" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                  Tipo de documento
                </label>
                <input
                  id="pv-edit-docType"
                  type="text"
                  value={row.documentType}
                  disabled
                  readOnly
                  className={fieldClass}
                  aria-describedby="pv-edit-doc-reason"
                />
              </div>
              <div>
                <label htmlFor="pv-edit-docNum" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                  Número de documento
                </label>
                <input
                  id="pv-edit-docNum"
                  type="text"
                  value={row.documentNumber}
                  disabled
                  readOnly
                  className={fieldClass}
                  aria-describedby="pv-edit-doc-reason"
                />
              </div>
            </div>
            <p id="pv-edit-doc-reason" className="flex items-start gap-1.5 text-[11px] opacity-70">
              <Lock className="h-3.5 w-3.5 shrink-0 mt-0.5" aria-hidden="true" />
              El tipo y el número de documento no son editables porque definen la identidad. Para
              corregirlos, anula este registro y crea una prevalidación nueva.
            </p>

            {/* Nombre */}
            <div>
              <label htmlFor="pv-edit-name" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                Nombre
              </label>
              <input
                id="pv-edit-name"
                type="text"
                value={name}
                onChange={(e) => setName(e.target.value)}
                disabled={submitting}
                className={fieldClass}
              />
            </div>

            {/* Correo */}
            <div>
              <label htmlFor="pv-edit-email" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                Nuevo correo electrónico
              </label>
              <input
                id="pv-edit-email"
                type="email"
                value={email}
                onChange={(e) => setEmail(e.target.value)}
                disabled={submitting}
                placeholder="Déjalo en blanco para no cambiarlo"
                autoComplete="off"
                className={fieldClass}
                aria-describedby={emailInvalid ? 'pv-edit-email-err' : 'pv-edit-email-hint'}
                aria-invalid={emailInvalid}
              />
              <p id="pv-edit-email-hint" className="mt-1 text-[11px] opacity-60">
                El correo actual no se muestra aquí por privacidad. Escribe el nuevo correo completo
                solo si quieres cambiarlo.
              </p>
              {emailInvalid && (
                <p id="pv-edit-email-err" className="mt-1 text-[11px] text-[#FF4E00]" role="alert">
                  Correo inválido
                </p>
              )}
            </div>

            {/* Representante legal — opcional, siempre disponible (no hay señal de personType en el listado) */}
            <div>
              <button
                type="button"
                onClick={() => setShowLegalRep((v) => !v)}
                className="text-xs font-semibold text-[#557EFF] underline underline-offset-2 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                aria-expanded={showLegalRep}
                aria-controls="pv-edit-legalrep-fieldset"
              >
                {showLegalRep ? 'Ocultar' : 'Editar'} representante legal (solo persona jurídica)
              </button>
              {showLegalRep && (
                <fieldset id="pv-edit-legalrep-fieldset" className="mt-2 space-y-3 rounded-xl border border-[#DDE5F0] p-4">
                  <legend className="px-1 text-xs font-semibold text-[#162744] dark:text-white">
                    Representante legal
                  </legend>
                  <p className="text-[11px] opacity-70">
                    Solo aplica si esta prevalidación es de una persona jurídica; si es persona
                    natural, estos campos se ignoran.
                  </p>
                  <div>
                    <label htmlFor="pv-edit-rl-name" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                      Nombre del representante legal
                    </label>
                    <input
                      id="pv-edit-rl-name"
                      type="text"
                      value={legalRepName}
                      onChange={(e) => setLegalRepName(e.target.value)}
                      disabled={submitting}
                      className={fieldClass}
                    />
                  </div>
                  <div>
                    <label htmlFor="pv-edit-rl-email" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                      Correo del representante legal
                    </label>
                    <input
                      id="pv-edit-rl-email"
                      type="email"
                      value={legalRepEmail}
                      onChange={(e) => setLegalRepEmail(e.target.value)}
                      disabled={submitting}
                      placeholder="Déjalo en blanco para no cambiarlo"
                      className={fieldClass}
                      aria-describedby={legalRepEmailInvalid ? 'pv-edit-rl-email-err' : undefined}
                      aria-invalid={legalRepEmailInvalid}
                    />
                    {legalRepEmailInvalid && (
                      <p id="pv-edit-rl-email-err" className="mt-1 text-[11px] text-[#FF4E00]" role="alert">
                        Correo inválido
                      </p>
                    )}
                  </div>
                </fieldset>
              )}
            </div>

            {/* Aviso de reenvío ANTES de guardar (AC1) */}
            {willResend && (
              <div
                className="flex items-start gap-2 rounded-xl border p-3 text-xs"
                style={{ borderColor: '#557EFF', background: 'rgba(85,126,255,0.06)', color: '#162744' }}
                role="status"
                aria-live="polite"
              >
                <AlertTriangle className="h-4 w-4 shrink-0 mt-0.5" style={{ color: '#557EFF' }} aria-hidden="true" />
                <span className="dark:text-white">
                  Al guardar, la validación se reenviará al nuevo correo y el enlace anterior dejará de
                  funcionar.
                </span>
              </div>
            )}

            {/* Error de API */}
            {apiError && (
              <div
                className="flex items-start gap-2 rounded-xl border p-3 text-xs"
                style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
                role="alert"
                aria-live="assertive"
              >
                <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
                <div className="space-y-2">
                  <span>{apiError}</span>
                  {offerNueva && (
                    <p className="opacity-80">
                      Para revalidar a esta persona, cierra este formulario y usa la acción
                      &quot;Nueva prevalidación&quot;.
                    </p>
                  )}
                </div>
              </div>
            )}
          </div>

          <div className="flex justify-end gap-3 border-t px-6 py-4">
            <button
              type="button"
              onClick={onClose}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-sm font-medium text-[#162744] transition hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] disabled:opacity-50 dark:text-white dark:hover:bg-white/10"
            >
              Cancelar
            </button>
            <button
              type="submit"
              disabled={submitting || hasErrors}
              className="flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-60 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
              style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
              {submitting ? 'Guardando…' : willResend ? 'Guardar y reenviar' : 'Guardar cambios'}
            </button>
          </div>
        </form>
      </div>
    </div>
  );
}

/**
 * Panel de resultado tras un reenvío (automático al editar el correo, o manual) — HU #10944, AC1/AC2.
 * Mismo patrón visual que `PrevalidacionSuccessPanel` (HU #10868), adaptado a la forma de
 * `EditarPrevalidacionResult`/`ReenviarPrevalidacionResult` (contrato real: `{ validation, captureUrl,
 * resent/queued }`, no el shape aplanado que usa el panel de creación).
 */
export interface PrevalidacionResendResultPanelProps {
  email: string;
  captureUrl?: string | null;
  queued?: boolean;
  resendCount: number;
  onClose: () => void;
}

export function PrevalidacionResendResultPanel({
  email,
  captureUrl,
  queued,
  resendCount,
  onClose,
}: PrevalidacionResendResultPanelProps) {
  const [copied, setCopied] = useState(false);

  const copiar = async () => {
    if (!captureUrl) return;
    try {
      await navigator.clipboard.writeText(captureUrl);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2500);
    } catch {
      /* sin permiso de clipboard */
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="pv-resend-title"
    >
      <div className="w-full max-w-md rounded-2xl bg-white p-8 shadow-xl dark:bg-[#0B0F14] text-center">
        <CheckCircle2 className="mx-auto h-12 w-12" style={{ color: '#5B8A1F' }} aria-hidden="true" />
        <h2 id="pv-resend-title" className="mt-3 text-base font-semibold text-[#162744] dark:text-white">
          Validación reenviada
        </h2>
        <p className="mt-2 text-sm opacity-70" role="status" aria-live="polite">
          {queued
            ? `El proveedor no respondió en este momento. La validación quedó encolada y se reintentará automáticamente hacia ${email}.`
            : `Se envió un enlace nuevo a ${email}.`}
        </p>

        {captureUrl && (
          <>
            <div className="mt-4 flex items-center gap-2 rounded-xl border p-3 text-left text-xs font-mono break-all">
              <a
                href={captureUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="flex-1 text-[#557EFF] underline underline-offset-2"
                aria-label="Abrir enlace de captura"
              >
                {captureUrl}
              </a>
              <ExternalLink className="h-3.5 w-3.5 shrink-0 text-[#557EFF]" aria-hidden="true" />
            </div>
            <button
              type="button"
              onClick={() => void copiar()}
              className="mt-3 rounded-xl border px-4 py-2 text-sm font-medium text-[#557EFF] transition hover:bg-[#557EFF]/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
              aria-live="polite"
            >
              {copied ? 'Copiado ✓' : 'Copiar enlace'}
            </button>
          </>
        )}

        <p className="mt-4 text-xs opacity-60">
          Reenvíos usados: {resendCount} de 3.
        </p>

        <div className="mt-6 flex justify-center">
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl px-4 py-2 text-sm font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
            style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
          >
            Cerrar
          </button>
        </div>
      </div>
    </div>
  );
}
