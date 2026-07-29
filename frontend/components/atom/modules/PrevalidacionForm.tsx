'use client';

import { useState } from 'react';
import { AlertCircle, CheckCircle2, ExternalLink, Loader2, X } from 'lucide-react';
import { tramitesClient } from '@/lib/api/tramites-client';
import type {
  IniciarPrevalidacionRequest,
  IniciarPrevalidacionResult,
} from '@/lib/api/types/procedure-runtime';
import { TramitesApiError } from '@/lib/api/tramites-client';

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

export function PrevalidacionForm({ onClose, onSuccess, initialValues }: PrevalidacionFormProps) {
  const [values, setValues] = useState<FormValues>(() => ({ ...EMPTY_FORM, ...initialValues }));
  const [touched, setTouched] = useState<Partial<Record<keyof FormValues, boolean>>>({});
  const [submitting, setSubmitting] = useState(false);
  const [apiError, setApiError] = useState<string | null>(null);

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
  };

  const touchAll = () => {
    const all: Partial<Record<keyof FormValues, boolean>> = {};
    for (const k of Object.keys(EMPTY_FORM) as Array<keyof FormValues>) {
      all[k] = true;
    }
    setTouched(all);
  };

  const handleSubmit = async (e: React.FormEvent) => {
    e.preventDefault();
    touchAll();
    if (hasErrors) return;

    setSubmitting(true);
    setApiError(null);
    try {
      // CF-01 (D1) — no se envía personType/legalRep*: el backend asume "natural" por defecto y
      // rechaza cualquier otro valor. El formulario ya no ofrece la opción jurídica.
      const body: IniciarPrevalidacionRequest = {
        documentType: values.documentType,
        documentNumber: values.documentNumber.trim(),
        name: values.name.trim(),
        email: values.email.trim(),
      };
      const result = await tramitesClient.createPrevalidacion(body);
      onSuccess(result);
    } catch (err) {
      if (err instanceof TramitesApiError && err.status === 409) {
        setApiError(
          'Ya existe una prevalidación activa o pendiente para este documento. Revísala en el listado.',
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
      <div className="w-full max-w-lg rounded-2xl bg-white shadow-xl dark:bg-[#0B0F14]">
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
                  onChange={(e) => set('documentType', e.target.value)}
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
                  value={values.documentNumber}
                  onChange={(e) => set('documentNumber', e.target.value)}
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
            <button
              type="submit"
              disabled={submitting}
              className="flex items-center gap-2 rounded-xl px-4 py-2 text-sm font-semibold text-white transition disabled:opacity-60 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[#557EFF]"
              style={{ background: 'linear-gradient(90deg, #4FD4CC 0%, #557EFF 100%)' }}
            >
              {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
              {submitting ? 'Creando…' : 'Crear prevalidación'}
            </button>
          </div>
        </form>
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
