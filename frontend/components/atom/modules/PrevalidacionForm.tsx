'use client';

import { useState } from 'react';
import { AlertCircle, CheckCircle2, ExternalLink, Loader2, X } from 'lucide-react';
import { tramitesClient, getIdentitySendConflict, TramitesApiError } from '@/lib/api/tramites-client';
import type {
  IniciarPrevalidacionRequest,
  IniciarPrevalidacionResult,
} from '@/lib/api/types/procedure-runtime';
import { sanitizeDocNumber } from '@/lib/validation/fieldRules';

/**
 * Resultado de REUTILIZAR una validación existente en vez de crear una nueva (módulo unificado de
 * Identidad). Un documento solo puede tener una validación en vuelo por tenant: si ya existe, no se
 * crea otra fila.
 *   - `email_actualizado` — el correo del formulario difiere del registrado: se actualiza y el backend
 *     reenvía el enlace al correo nuevo (PATCH con auto-reenvío, D8).
 *   - `reenviado` — el correo es el mismo: no se actualiza nada y solo se reenvía el enlace.
 */
export interface PrevalidacionReuseInfo {
  kind: 'email_actualizado' | 'reenviado';
  validationId: string;
  email: string;
  captureUrl: string | null;
  queued?: boolean;
}

/**
 * Formulario modal/drawer para crear una prevalidación de identidad standalone (HU #10868).
 * CF-01 (Feature #11004, HU #11006, D1) — la prevalidación es SOLO persona natural: el backend
 * rechaza `personType=juridical` con 422 (`prevalidacion_solo_natural`); el formulario ya no ofrece
 * el selector natural/jurídica ni los campos de representante legal, y no envía `personType`/
 * `legalRep*` en el body. Datos mínimos: tipo + número de documento, nombre completo, correo.
 * WCAG 2.1 AA: todos los inputs con <label> asociado, focus ring, role="dialog".
 *
 * Props:
 *   onClose  — cierra el formulario (ESC o ×).
 *   onSuccess — callback con el resultado; la pantalla padre muestra el enlace.
 */
export interface PrevalidacionFormProps {
  onClose: () => void;
  onSuccess: (result: IniciarPrevalidacionResult) => void;
  /**
   * El documento ya tenía una validación en vuelo: no se creó ninguna fila nueva y el enlace se
   * reenvió (actualizando antes el correo si venía distinto). La pantalla padre cierra el formulario,
   * refresca el listado y muestra el resultado del reenvío.
   */
  onReused?: (info: PrevalidacionReuseInfo) => void;
  /**
   * HU #10944 (CF-03, D9/borde) — precarga documento/nombre al ofrecer "Nueva prevalidación" para
   * la misma persona desde un registro `aprobado` y vencido (revalidar exige un registro nuevo, no
   * reenviar el viejo). El correo NO se precarga (no viaja en el listado ni en las respuestas de
   * error); el operador lo escribe de nuevo.
   */
  initialValues?: Partial<Pick<FormValues, 'documentType' | 'documentNumber' | 'name'>>;
}

const DOCUMENT_TYPES = [
  { value: 'CC', label: 'Cédula de ciudadanía' },
  { value: 'CE', label: 'Cédula de extranjería' },
  { value: 'NIT', label: 'NIT (persona jurídica)' },
  { value: 'PAS', label: 'Pasaporte' },
  { value: 'TI', label: 'Tarjeta de identidad' },
  { value: 'PPT', label: 'Permiso de permanencia' },
];

interface FormValues {
  documentType: string;
  documentNumber: string;
  name: string;
  email: string;
}

const EMPTY_FORM: FormValues = {
  documentType: 'CC',
  documentNumber: '',
  name: '',
  email: '',
};

function required(v: string) {
  return v.trim().length > 0;
}

function validEmail(v: string) {
  return /^[^\s@]+@[^\s@]+\.[^\s@]+$/.test(v.trim());
}

export function PrevalidacionForm({
  onClose,
  onSuccess,
  onReused,
  initialValues,
}: PrevalidacionFormProps) {
  const [values, setValues] = useState<FormValues>(() => ({ ...EMPTY_FORM, ...initialValues }));
  const [touched, setTouched] = useState<Partial<Record<keyof FormValues, boolean>>>({});
  const [submitting, setSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);
  // HU #11267 — confirmación con destinatario (AC3) y aviso de identidad existente (AC1).
  const [confirmOpen, setConfirmOpen] = useState(false);
  const [existingConflict, setExistingConflict] = useState<ReturnType<typeof getIdentitySendConflict>>(null);

  const errors: Partial<Record<keyof FormValues, string>> = {};
  if (!required(values.documentType)) errors.documentType = 'Requerido';
  if (!required(values.documentNumber)) errors.documentNumber = 'Requerido';
  if (!required(values.name)) errors.name = 'Requerido';
  if (!required(values.email)) errors.email = 'Requerido';
  else if (!validEmail(values.email)) errors.email = 'Correo inválido';

  const hasErrors = Object.keys(errors).length > 0;

  const set = (field: keyof FormValues, value: string) => {
    setValues((prev) => ({ ...prev, [field]: value }));
    setTouched((prev) => ({ ...prev, [field]: true }));
    setApiError(null);
    setExistingConflict(null);
  };

  const touchAll = () => {
    const all: Partial<Record<keyof FormValues, boolean>> = {};
    for (const k of Object.keys(EMPTY_FORM) as Array<keyof FormValues>) {
      all[k] = true;
    }
    setTouched(all);
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    touchAll();
    if (hasErrors) return;
    // AC3 — mostrar destinatario y pedir confirmación antes de enviar.
    setConfirmOpen(true);
  };

  /**
   * El documento ya tiene una validación EN VUELO en el tenant (con enlace vigente o vencido): no se
   * crea una fila nueva. Se resuelve sobre la validación existente según el correo capturado:
   *   - correo distinto al registrado → PATCH: actualiza el contacto y el backend reenvía solo (D8);
   *   - correo igual → el PATCH no reenvía (`resent=false`), así que se dispara el reenvío explícito.
   * El PATCH se manda siempre primero porque el correo registrado no viaja en el 409; comparar es
   * responsabilidad del backend, que ya tiene el dato.
   */
  const reusarValidacionExistente = async (validationId: string) => {
    const email = values.email.trim();
    const edited = await tramitesClient.editPrevalidacion(validationId, {
      name: values.name.trim(),
      email,
    });
    if (edited.resent) {
      return {
        kind: 'email_actualizado' as const,
        validationId,
        email: edited.validation.email ?? email,
        captureUrl: edited.captureUrl,
      };
    }
    const resent = await tramitesClient.resendPrevalidacion(validationId);
    return {
      kind: 'reenviado' as const,
      validationId,
      email: resent.validation.email ?? email,
      captureUrl: resent.captureUrl,
      queued: resent.queued,
    };
  };

  const sendPrevalidacion = async () => {
    setSubmitting(true);
    setApiError(null);
    setExistingConflict(null);
    try {
      const body: IniciarPrevalidacionRequest = {
        documentType: values.documentType,
        documentNumber: values.documentNumber.trim(),
        name: values.name.trim(),
        email: values.email.trim(),
      };
      const result = await tramitesClient.createPrevalidacion(body);
      setConfirmOpen(false);
      onSuccess(result);
    } catch (err) {
      const conflict = getIdentitySendConflict(err);
      // Documento ya existente CON validación en vuelo (propia del tenant, no de un trámite): se
      // reutiliza el registro y se reenvía el correo en vez de fallar. Identidad aprobada vigente y
      // cobertura del baúl NO reenvían: no hay nada que capturar, solo se informa el proceso existente.
      const reusable =
        conflict !== null &&
        conflict.validationId !== null &&
        conflict.origen !== 'tramite' &&
        (conflict.motivo === 'validacion_en_vuelo' || conflict.motivo === 'enlace_vencido_reenvio');

      if (reusable) {
        try {
          const info = await reusarValidacionExistente(conflict.validationId!);
          setConfirmOpen(false);
          // Sin `onReused` el reenvío ya ocurrió pero nadie lo mostraría: se cierra el formulario en
          // vez de dejarlo abierto como si no hubiera pasado nada.
          if (onReused) onReused(info);
          else onClose();
        } catch (reuseErr) {
          setConfirmOpen(false);
          setApiError(
            reuseErr instanceof Error
              ? `Ya existe una validación para este documento en este tenant, pero no se pudo reenviar el correo: ${reuseErr.message}`
              : 'Ya existe una validación para este documento en este tenant y no se pudo reenviar el correo. Intenta de nuevo.',
          );
        }
      } else if (conflict) {
        setConfirmOpen(false);
        setExistingConflict(conflict);
        setApiError(null);
      } else if (err instanceof TramitesApiError && err.status === 409) {
        setApiError(
          'Ya existe una validación activa o pendiente para este documento en este tenant. Revísala en el listado.',
        );
      } else {
        setApiError(
          err instanceof Error
            ? err.message
            : 'No se pudo crear la prevalidación. Intenta de nuevo.',
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const formatVigencia = (iso: string | null) => {
    if (!iso) return null;
    const d = new Date(iso);
    if (Number.isNaN(d.getTime())) return iso;
    return new Intl.DateTimeFormat('es-CO', { dateStyle: 'medium' }).format(d);
  };

  const fieldClass = (field: keyof FormValues) =>
    `w-full rounded-xl border px-3 py-2 text-sm outline-none transition focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20 disabled:opacity-50 ${
      touched[field] && errors[field] ? 'border-[#FF4E00]' : 'border-[#DDE5F0]'
    }`;

  return (
    /* Overlay */
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="prevalidacion-form-title"
    >
      <div className="relative w-full max-w-lg rounded-2xl bg-white shadow-xl dark:bg-[#0B0F14]">
        {/* Header */}
        <div className="flex items-center justify-between border-b px-6 py-4">
          <h2 id="prevalidacion-form-title" className="text-base font-semibold text-[#162744] dark:text-white">
            Nueva prevalidación de identidad
          </h2>
          <button
            type="button"
            onClick={onClose}
            aria-label="Cerrar formulario"
            className="rounded-lg p-1 hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:hover:bg-white/10"
          >
            <X className="h-5 w-5 opacity-60" aria-hidden="true" />
          </button>
        </div>

        {/* Body */}
        <form onSubmit={(e) => void handleSubmit(e)} noValidate>
          <div className="space-y-4 overflow-y-auto max-h-[70vh] px-6 py-5">
            {/* Documento */}
            <div className="grid grid-cols-2 gap-3">
              <div>
                <label htmlFor="pv-docType" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                  Tipo de documento <span aria-hidden="true" className="text-[#FF4E00]">*</span>
                </label>
                <select
                  id="pv-docType"
                  value={values.documentType}
                  onChange={(e) => {
                    const nextType = e.target.value;
                    set('documentType', nextType);
                    set(
                      'documentNumber',
                      sanitizeDocNumber(values.documentNumber, nextType),
                    );
                  }}
                  disabled={submitting}
                  className={fieldClass('documentType')}
                  aria-describedby={touched.documentType && errors.documentType ? 'pv-docType-err' : undefined}
                  aria-invalid={!!(touched.documentType && errors.documentType)}
                >
                  {DOCUMENT_TYPES.map((d) => (
                    <option key={d.value} value={d.value}>
                      {d.label}
                    </option>
                  ))}
                </select>
                {touched.documentType && errors.documentType && (
                  <p id="pv-docType-err" className="mt-1 text-[11px] text-[#FF4E00]" role="alert">
                    {errors.documentType}
                  </p>
                )}
              </div>
              <div>
                <label htmlFor="pv-docNum" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                  Número de documento <span aria-hidden="true" className="text-[#FF4E00]">*</span>
                </label>
                <input
                  id="pv-docNum"
                  type="text"
                  inputMode={values.documentType === 'PAS' ? 'text' : 'numeric'}
                  value={values.documentNumber}
                  onChange={(e) =>
                    set('documentNumber', sanitizeDocNumber(e.target.value, values.documentType))
                  }
                  disabled={submitting}
                  placeholder="Ej. 1234567890"
                  autoComplete="off"
                  className={fieldClass('documentNumber')}
                  aria-describedby={touched.documentNumber && errors.documentNumber ? 'pv-docNum-err' : undefined}
                  aria-invalid={!!(touched.documentNumber && errors.documentNumber)}
                />
                {touched.documentNumber && errors.documentNumber && (
                  <p id="pv-docNum-err" className="mt-1 text-[11px] text-[#FF4E00]" role="alert">
                    {errors.documentNumber}
                  </p>
                )}
              </div>
            </div>

            {/* Nombre */}
            <div>
              <label htmlFor="pv-name" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                Nombre completo <span aria-hidden="true" className="text-[#FF4E00]">*</span>
              </label>
              <input
                id="pv-name"
                type="text"
                value={values.name}
                onChange={(e) => set('name', e.target.value)}
                disabled={submitting}
                placeholder="Nombre completo de la persona"
                className={fieldClass('name')}
                aria-describedby={touched.name && errors.name ? 'pv-name-err' : undefined}
                aria-invalid={!!(touched.name && errors.name)}
              />
              {touched.name && errors.name && (
                <p id="pv-name-err" className="mt-1 text-[11px] text-[#FF4E00]" role="alert">
                  {errors.name}
                </p>
              )}
            </div>

            {/* Correo */}
            <div>
              <label htmlFor="pv-email" className="mb-1 block text-xs font-medium text-[#162744] dark:text-white">
                Correo electrónico <span aria-hidden="true" className="text-[#FF4E00]">*</span>
              </label>
              <input
                id="pv-email"
                type="email"
                value={values.email}
                onChange={(e) => set('email', e.target.value)}
                disabled={submitting}
                placeholder="correo@ejemplo.com"
                autoComplete="email"
                className={fieldClass('email')}
                aria-describedby={touched.email && errors.email ? 'pv-email-err' : 'pv-email-hint'}
                aria-invalid={!!(touched.email && errors.email)}
              />
              <p id="pv-email-hint" className="mt-1 text-[11px] opacity-60">
                A este correo se enviará el enlace de captura biométrica.
              </p>
              {touched.email && errors.email && (
                <p id="pv-email-err" className="mt-1 text-[11px] text-[#FF4E00]" role="alert">
                  {errors.email}
                </p>
              )}
            </div>

            {/* HU #11267 AC1 — identidad existente: aviso + Ver proceso (sin reenviar) */}
            {existingConflict && (
              <div
                className="space-y-2 rounded-xl border p-3 text-xs"
                style={{ borderColor: '#5B8A1F', background: 'rgba(91,138,31,0.08)', color: '#3F5F14' }}
                role="status"
                aria-live="polite"
              >
                <p className="font-semibold">
                  {existingConflict.motivo === 'identidad_vigente'
                    ? 'Ya validada'
                    : existingConflict.motivo === 'validacion_en_vuelo'
                      ? 'Validación en curso'
                      : 'No se puede crear una prevalidación nueva'}
                  {existingConflict.validUntil
                    ? ` · vigente hasta el ${formatVigencia(existingConflict.validUntil)}`
                    : ''}
                </p>
                <p className="opacity-80">
                  Estado: {existingConflict.status ?? 'disponible'}.{' '}
                  {existingConflict.origen === 'tramite'
                    ? 'La validación pertenece a un trámite: gestiónala desde ese trámite.'
                    : 'Usa el proceso existente en lugar de reenviar.'}
                </p>
                {existingConflict.validationId && (
                  <button
                    type="button"
                    onClick={onClose}
                    className="rounded-lg px-3 py-1.5 text-[11px] font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2"
                    style={{ background: '#5B8A1F' }}
                  >
                    Ver el proceso existente
                  </button>
                )}
              </div>
            )}

            {/* API error */}
            {apiError && (
              <div
                className="flex items-start gap-2 rounded-xl border p-3 text-xs"
                style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
                role="alert"
                aria-live="polite"
              >
                <AlertCircle className="h-4 w-4 shrink-0 mt-0.5" aria-hidden="true" />
                <span>{apiError}</span>
              </div>
            )}
          </div>

          {/* Footer */}
          <div className="flex justify-end gap-3 border-t px-6 py-4">
            <button
              type="button"
              onClick={onClose}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-sm font-medium text-[#162744] transition hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] disabled:opacity-50 dark:text-white dark:hover:bg-white/10"
            >
              Cancelar
            </button>
            {!existingConflict && (
              <button
                type="submit"
                disabled={submitting}
                className="flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-60 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
                style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
              >
                Crear prevalidación
              </button>
            )}
          </div>
        </form>

        {/* HU #11267 AC3 — confirmación con destinatario */}
        {confirmOpen && (
          <div
            className="absolute inset-0 z-10 flex items-center justify-center rounded-2xl bg-black/40 px-4"
            role="alertdialog"
            aria-modal="true"
            aria-labelledby="pv-confirm-title"
          >
            <div className="w-full max-w-sm rounded-2xl bg-white p-5 shadow-lg dark:bg-[#0B0F14]">
              <h3 id="pv-confirm-title" className="text-sm font-semibold text-[#162744] dark:text-white">
                Confirmar envío
              </h3>
              <p className="mt-2 text-xs opacity-80">
                Se enviará el enlace de captura a <strong>{values.email.trim()}</strong>. ¿Continuar?
              </p>
              <div className="mt-4 flex justify-end gap-2">
                <button
                  type="button"
                  onClick={() => setConfirmOpen(false)}
                  disabled={submitting}
                  className="rounded-lg border px-3 py-1.5 text-xs font-medium disabled:opacity-50"
                >
                  Volver
                </button>
                <button
                  type="button"
                  onClick={() => void sendPrevalidacion()}
                  disabled={submitting}
                  className="flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-xs font-semibold text-white disabled:opacity-60"
                  style={{ background: '#557EFF' }}
                >
                  {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />}
                  {submitting ? 'Enviando…' : 'Confirmar y enviar'}
                </button>
              </div>
            </div>
          </div>
        )}
      </div>
    </div>
  );
}

/**
 * Panel de éxito mostrado tras crear la prevalidación. Muestra el enlace de captura
 * o el estado "encolada" si el proveedor tuvo un fallo transitorio.
 */
export function PrevalidacionSuccessPanel({
  result,
  onClose,
  onNew,
}: {
  result: IniciarPrevalidacionResult;
  onClose: () => void;
  onNew: () => void;
}) {
  const [copied, setCopied] = useState(false);

  const copiar = async () => {
    if (!result.captureUrl) return;
    try {
      await navigator.clipboard.writeText(result.captureUrl);
      setCopied(true);
      window.setTimeout(() => setCopied(false), 2500);
    } catch {
      /* sin permiso de clipboard — el enlace sigue visible */
    }
  };

  return (
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-black/40 px-4"
      role="dialog"
      aria-modal="true"
      aria-labelledby="pv-success-title"
    >
      <div className="w-full max-w-md rounded-2xl bg-white p-8 shadow-xl dark:bg-[#0B0F14] text-center">
        <CheckCircle2
          className="mx-auto h-12 w-12"
          style={{ color: '#5B8A1F' }}
          aria-hidden="true"
        />
        <h2 id="pv-success-title" className="mt-3 text-base font-semibold text-[#162744] dark:text-white">
          Prevalidación creada
        </h2>
        {result.enqueued ? (
          <p className="mt-2 text-sm opacity-70">
            El proveedor no respondió en este momento. La validación quedó encolada y el sistema la
            reintentará automáticamente. Revisa el listado en unos minutos.
          </p>
        ) : result.captureUrl ? (
          <>
            <p className="mt-2 text-sm opacity-70">
              Comparte el enlace de captura con la persona para que complete la validación biométrica.
            </p>
            <div className="mt-4 flex items-center gap-2 rounded-xl border p-3 text-left text-xs font-mono break-all">
              <a
                href={result.captureUrl}
                target="_blank"
                rel="noopener noreferrer"
                className="flex-1 text-[#557EFF] underline underline-offset-2"
                aria-label="Abrir enlace de captura"
              >
                {result.captureUrl}
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
        ) : (
          <p className="mt-2 text-sm opacity-70">
            La validación fue creada. El enlace de captura será generado en breve por el proveedor.
          </p>
        )}

        <div className="mt-6 flex justify-center gap-3">
          <button
            type="button"
            onClick={onNew}
            className="rounded-xl border px-4 py-2 text-sm font-medium text-[#162744] hover:bg-black/5 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF] dark:text-white dark:hover:bg-white/10"
          >
            Crear otra
          </button>
          <button
            type="button"
            onClick={onClose}
            className="rounded-xl px-4 py-2 text-sm font-semibold text-white focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
            style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
          >
            Ver listado
          </button>
        </div>
      </div>
    </div>
  );
}
