'use client';

import { useRef, useState } from 'react';
import { AlertTriangle, Paperclip } from 'lucide-react';
import { tramitesClient, TramitesApiError } from '@/lib/api/tramites-client';
import type { RequestRevocationResult } from '@/lib/api/types/procedure-runtime';
import { WizardModal } from './WizardModal';
import { InlineAlert } from '@/components/atom/InlineAlert';
import { WIZARD_CTA_GRADIENT } from './wizard-field-styles';

type Step = 'warning' | 'form';

interface FieldErrors {
  reason?: string;
  file?: string;
  confirmAccuracy?: string;
  confirmConsequences?: string;
}

/**
 * Código de negocio (RFC 7807 `title`, ver `RequestRevocationHandler`/`RevocationRequestGate` del
 * backend) → campo del Paso 2 que debe señalarse. Los códigos sin entrada aquí (tramite_no_aprobado,
 * solicitud_activa_existente, ventana_vencida, origen_no_soportado, not_found) no son de un campo
 * del formulario: se muestran como error general (`submitError`).
 */
const ERROR_FIELD_BY_CODE: Partial<Record<string, keyof FieldErrors>> = {
  motivo_requerido: 'reason',
  confirmacion_exactitud_requerida: 'confirmAccuracy',
  confirmacion_consecuencias_requerida: 'confirmConsequences',
  documento_requerido: 'file',
  documento_formato_invalido: 'file',
  documento_muy_grande: 'file',
};

export interface RevocationRequestModalProps {
  instanceId: string;
  /**
   * Bug reportado por el usuario (2026-09-16): un SuperAdmin abriendo el trámite de OTRA compañía
   * desde `TramitesTable`/`TramiteDetalleModal` (no la ruta del wizard `?t=`) recibía 404
   * "Procedure instance not found" al enviar la solicitud. Causa: `tramitesClient.requestRevocation`
   * ya soporta un `tenantId` explícito, pero nadie se lo pasaba — el header `X-Tenant-Id` caía al
   * fallback de `tenantHeader()` (tenant activo → JWT), y el JWT de un SuperAdmin trae SU PROPIO
   * tenant, no el del trámite que está viendo. Sin este prop, la llamada quedaba tenant-scoped al
   * tenant equivocado.
   */
  tenantId?: string;
  onClose: () => void;
  /** 201 exitoso (HU #12572). El caller cierra el modal y refleja el envío (AC3). */
  onSuccess: (result: RequestRevocationResult) => void;
}

/**
 * HU #12574 (Feature #12565) — flujo de 2 pasos de "Solicitar revocatoria" (Epic #12534,
 * decisiones de producto 2026-09-15).
 *
 * <p>
 * <b>Paso 1 (AC1):</b> SOLO la advertencia "no tiene reversa", sin campos de captura — confirmar
 * para avanzar. <b>Paso 2 (AC2):</b> el formulario real — motivo, documento de soporte (PDF, "el
 * certificado" es el mismo adjunto obligatorio, no un segundo documento) y los 2 checks de
 * confirmación; el envío se bloquea señalando cada campo faltante. <b>AC3:</b> tras un 201, el
 * caller cierra el modal — no hay opción de retirar la solicitud dentro de este componente.
 * </p>
 *
 * Consume `POST /instances/{id}/revocation-requests` (HU #12572, multipart). Contenedor:
 * `WizardModal` (B5/B6 del guardián de diseño) — mismo patrón que "Anular trámite" en
 * `TramiteWizard.tsx`, con foco atrapado + Escape + overlay ya resueltos ahí.
 */
export function RevocationRequestModal({ instanceId, tenantId, onClose, onSuccess }: RevocationRequestModalProps) {
  const [step, setStep] = useState<Step>('warning');
  const [reason, setReason] = useState('');
  const [file, setFile] = useState<File | null>(null);
  const [confirmAccuracy, setConfirmAccuracy] = useState(false);
  const [confirmConsequences, setConfirmConsequences] = useState(false);
  const [fieldErrors, setFieldErrors] = useState<FieldErrors>({});
  const [submitError, setSubmitError] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  function validate(): FieldErrors {
    const errors: FieldErrors = {};
    if (!reason.trim()) errors.reason = 'Indica el motivo de la solicitud de revocatoria.';
    if (!file) errors.file = 'Adjunta el documento de soporte (PDF).';
    else if (file.type !== 'application/pdf')
      errors.file = 'El documento de soporte debe ser un archivo PDF.';
    if (!confirmAccuracy)
      errors.confirmAccuracy = 'Debes confirmar que la información registrada es correcta.';
    if (!confirmConsequences)
      errors.confirmConsequences =
        'Debes confirmar que entiendes que la solicitud no se puede retirar.';
    return errors;
  }

  async function handleSubmit() {
    if (submitting) return;
    const errors = validate();
    setFieldErrors(errors);
    // AC2 — el envío se bloquea señalando el/los campo(s) faltante(s); no llama al backend si el
    // formulario está incompleto.
    if (Object.keys(errors).length > 0) return;

    setSubmitError(null);
    setSubmitting(true);
    try {
      const result = await tramitesClient.requestRevocation(
        instanceId,
        {
          reason: reason.trim(),
          confirmAccuracy,
          confirmConsequences,
          file: file!,
        },
        tenantId,
      );
      onSuccess(result);
    } catch (err) {
      const code =
        err instanceof TramitesApiError && typeof err.problem?.title === 'string'
          ? err.problem.title
          : undefined;
      const field = code ? ERROR_FIELD_BY_CODE[code] : undefined;
      const message = err instanceof Error ? err.message : 'No se pudo enviar la solicitud de revocatoria.';
      if (field) {
        setFieldErrors((prev) => ({ ...prev, [field]: message }));
      } else {
        setSubmitError(message);
      }
    } finally {
      setSubmitting(false);
    }
  }

  return (
    <WizardModal
      title={step === 'warning' ? 'Solicitar revocatoria' : 'Solicitar revocatoria — motivo y soporte'}
      onClose={onClose}
    >
      {step === 'warning' ? (
        <>
          <div
            className="flex items-start gap-2 rounded-xl border p-3"
            style={{ borderColor: '#F9AC00', background: 'rgba(249,172,0,0.08)' }}
            role="alert"
          >
            <AlertTriangle
              className="mt-0.5 h-4 w-4 shrink-0"
              style={{ color: 'var(--badge-warning-fg)' }}
              aria-hidden="true"
            />
            <p className="text-xs leading-relaxed" style={{ color: '#162744' }}>
              <strong>Esta acción no tiene reversa.</strong> Al continuar iniciarás la solicitud de
              revocatoria de este trámite Aprobado. En el siguiente paso deberás indicar el motivo,
              adjuntar el documento de soporte y confirmar que entiendes las consecuencias: una vez
              enviada, la solicitud no se puede retirar.
            </p>
          </div>
          <div className="mt-6 flex justify-end gap-2">
            <button
              type="button"
              onClick={onClose}
              className="rounded-xl border px-4 py-2 text-xs font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF]"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => setStep('form')}
              className="rounded-xl px-5 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-[#557EFF]"
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              Continuar
            </button>
          </div>
        </>
      ) : (
        <div>
          <div className="space-y-4">
            <div>
              <label htmlFor="revocation-reason" className="block text-xs font-semibold" style={{ color: '#162744' }}>
                Motivo de la solicitud
              </label>
              <textarea
                id="revocation-reason"
                value={reason}
                onChange={(e) => {
                  setReason(e.target.value);
                  if (fieldErrors.reason) setFieldErrors((prev) => ({ ...prev, reason: undefined }));
                }}
                rows={3}
                disabled={submitting}
                aria-invalid={!!fieldErrors.reason}
                aria-describedby={fieldErrors.reason ? 'revocation-reason-err' : undefined}
                placeholder="Explica por qué solicitas la revocatoria de este trámite"
                className="mt-1.5 w-full rounded-xl border border-[#DFE5ED] px-3 py-2 text-xs outline-none transition focus:border-[#557EFF] focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50 dark:border-white/15 dark:bg-white/5"
              />
              {fieldErrors.reason ? (
                <p id="revocation-reason-err" role="alert" className="mt-1 text-[11px] font-medium" style={{ color: '#C2410C' }}>
                  {fieldErrors.reason}
                </p>
              ) : null}
            </div>

            <div>
              <span className="block text-xs font-semibold" style={{ color: '#162744' }}>
                Documento de soporte (PDF)
              </span>
              <input
                ref={fileInputRef}
                type="file"
                accept="application/pdf"
                aria-label="Archivo PDF del documento de soporte"
                aria-invalid={!!fieldErrors.file}
                aria-describedby={fieldErrors.file ? 'revocation-file-err' : undefined}
                disabled={submitting}
                onChange={(e) => {
                  const picked = e.target.files?.[0] ?? null;
                  setFile(picked);
                  if (fieldErrors.file) setFieldErrors((prev) => ({ ...prev, file: undefined }));
                  // Permite re-elegir el MISMO archivo dos veces seguidas (mismo criterio que
                  // AdminTramiteAcciones: sin esto el navegador no dispara `onChange` si el value
                  // no cambia).
                  e.target.value = '';
                }}
                className="hidden"
              />
              <button
                type="button"
                onClick={() => fileInputRef.current?.click()}
                disabled={submitting}
                className="mt-1.5 inline-flex items-center gap-1.5 rounded-lg border px-3 py-2 text-xs font-semibold transition hover:bg-[#557EFF]/[0.06] disabled:cursor-not-allowed disabled:opacity-60"
                style={{ borderColor: '#557EFF', color: '#557EFF' }}
              >
                <Paperclip className="h-3.5 w-3.5" aria-hidden="true" />
                {file ? 'Cambiar archivo' : 'Elegir archivo PDF'}
              </button>
              {file ? (
                <p className="mt-1 truncate text-xs" style={{ color: '#59677D' }}>
                  Archivo seleccionado: <span className="font-medium" style={{ color: '#162744' }}>{file.name}</span>
                </p>
              ) : (
                <p className="mt-1 text-xs" style={{ color: '#94A3B8' }}>Ningún archivo elegido todavía.</p>
              )}
              {fieldErrors.file ? (
                <p id="revocation-file-err" role="alert" className="mt-1 text-[11px] font-medium" style={{ color: '#C2410C' }}>
                  {fieldErrors.file}
                </p>
              ) : null}
            </div>

            <RevocationCheckbox
              id="revocation-confirm-accuracy"
              checked={confirmAccuracy}
              onChange={(checked) => {
                setConfirmAccuracy(checked);
                if (fieldErrors.confirmAccuracy)
                  setFieldErrors((prev) => ({ ...prev, confirmAccuracy: undefined }));
              }}
              disabled={submitting}
              label="Confirmo que la información registrada en esta solicitud es correcta."
              error={fieldErrors.confirmAccuracy}
            />
            <RevocationCheckbox
              id="revocation-confirm-consequences"
              checked={confirmConsequences}
              onChange={(checked) => {
                setConfirmConsequences(checked);
                if (fieldErrors.confirmConsequences)
                  setFieldErrors((prev) => ({ ...prev, confirmConsequences: undefined }));
              }}
              disabled={submitting}
              label="Entiendo que esta solicitud de revocatoria no se puede retirar una vez enviada."
              error={fieldErrors.confirmConsequences}
            />
          </div>

          {submitError ? (
            <div className="mt-4">
              <InlineAlert tone="error">{submitError}</InlineAlert>
            </div>
          ) : null}

          <div className="mt-6 flex justify-end gap-2">
            <button
              type="button"
              onClick={() => setStep('warning')}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-xs font-medium focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] disabled:opacity-50"
            >
              Atrás
            </button>
            <button
              type="button"
              onClick={() => void handleSubmit()}
              disabled={submitting}
              className="rounded-xl px-5 py-2 text-xs font-semibold text-white focus:outline-none focus-visible:ring-2 focus-visible:ring-offset-2 focus-visible:ring-[#557EFF] disabled:opacity-50"
              style={{ background: WIZARD_CTA_GRADIENT }}
            >
              {submitting ? 'Enviando…' : 'Enviar solicitud'}
            </button>
          </div>
        </div>
      )}
    </WizardModal>
  );
}

function RevocationCheckbox({
  id,
  checked,
  onChange,
  disabled,
  label,
  error,
}: {
  id: string;
  checked: boolean;
  onChange: (checked: boolean) => void;
  disabled?: boolean;
  label: string;
  error?: string;
}) {
  return (
    <div>
      <div className="flex items-start gap-2">
        <input
          id={id}
          type="checkbox"
          checked={checked}
          disabled={disabled}
          aria-invalid={!!error}
          aria-describedby={error ? `${id}-err` : undefined}
          onChange={(e) => onChange(e.target.checked)}
          className="mt-0.5 h-4 w-4 shrink-0 cursor-pointer accent-[#557EFF] disabled:opacity-60"
        />
        <label htmlFor={id} className="cursor-pointer text-xs leading-snug" style={{ color: '#162744' }}>
          {label}
        </label>
      </div>
      {error ? (
        <p id={`${id}-err`} role="alert" className="mt-1 text-[11px] font-medium" style={{ color: '#C2410C' }}>
          {error}
        </p>
      ) : null}
    </div>
  );
}
