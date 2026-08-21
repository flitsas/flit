"use client";

import { useEffect, useRef, useState } from "react";
import { Building2, FileText, Loader2, UploadCloud } from "lucide-react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import type { DeedFormInput, DeedSaved } from "@/lib/api/admin-deeds";

/** Compañía fija (de contexto) para la que se crea/edita la escritura. Se muestra como dato de solo lectura. */
export interface DeedFormCompany {
  /** `representedCompanyId` que la escritura persiste en `representedCompanyIds`. */
  id: string;
  name: string;
  /** NIT (PII, Ley 1581); opcional, solo para mostrar. */
  nit?: string;
}

/**
 * Escritura a editar (referencia ligera). Basta con los campos del formulario: la compañía llega por
 * contexto (fija), así que no se necesita el `DeedItem` completo. Tanto `DeedItem` como
 * `RepresentativeDeed` satisfacen esta forma estructuralmente.
 */
export interface DeedEditingRef {
  id: string;
  description: string;
  /** Vigencia (YYYY-MM-DD). */
  vigenciaDesde: string;
  vigenciaHasta: string;
}

export interface DeedsFormPanelProps {
  open: boolean;
  /** Escritura a editar; `null` = alta. */
  editing: DeedEditingRef | null;
  /**
   * Compañía FIJA para la que se crea/edita la escritura (HU #10929). Llega por contexto desde la
   * pantalla del representante legal: la escritura se crea SIEMPRE para esta única compañía. No hay
   * selector; el nombre se muestra como dato de solo lectura.
   */
  company: DeedFormCompany | null;
  onClose: () => void;
  onSubmit: (input: DeedFormInput) => Promise<DeedSaved>;
  onSaved: (saved: DeedSaved) => void;
  onError: (message: string) => void;
  /**
   * z-index del overlay del panel. El panel se lanza desde el modal del representante (`Modal` en
   * `z-[100]`), por lo que necesita un z mayor para no quedar oculto tras el overlay del modal.
   */
  zClassName?: string;
}

interface FormState {
  description: string;
  vigenciaDesde: string;
  vigenciaHasta: string;
  file: File | null;
}

const EMPTY: FormState = {
  description: "",
  vigenciaDesde: "",
  vigenciaHasta: "",
  file: null,
};

function fromEditing(item: DeedEditingRef): FormState {
  return {
    description: item.description,
    vigenciaDesde: item.vigenciaDesde,
    vigenciaHasta: item.vigenciaHasta,
    file: null,
  };
}

/**
 * Panel de alta/edición de una escritura (HU #10905, ajustes HU #10929): descripción, carga del PDF y
 * vigencia. La escritura aplica SIEMPRE a UNA compañía fija que llega por contexto (desde la pantalla
 * del representante legal), mostrada como dato de solo lectura; al guardar se envía como el único
 * elemento de `representedCompanyIds`. En alta el PDF es obligatorio; en edición es opcional (si no se
 * elige uno nuevo, se conserva el custodiado). Los errores 422 del backend
 * (`description`/`vigenciaHasta`) se muestran por campo.
 */
export function DeedsFormPanel({
  open,
  editing,
  company,
  onClose,
  onSubmit,
  onSaved,
  onError,
  zClassName,
}: DeedsFormPanelProps) {
  const [form, setForm] = useState<FormState>(EMPTY);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  // Marca que el usuario intentó enviar el formulario: habilita los mensajes de error
  // por campo obligatorio sin necesidad de deshabilitar el botón de envío.
  const [attempted, setAttempted] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    // Reinicia el formulario al abrir (alta en blanco o edición precargada).
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el formulario al abrir el panel
    setForm(editing ? fromEditing(editing) : EMPTY);
    setFieldErrors({});
    setBanner(null);
    setAttempted(false);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, [open, editing]);

  const patch = (p: Partial<FormState>) => setForm((f) => ({ ...f, ...p }));

  // Validación en cliente (los mismos requeridos que valida el backend). En alta, el PDF es obligatorio.
  const isValid =
    form.description.trim() !== "" &&
    form.vigenciaDesde !== "" &&
    form.vigenciaHasta !== "" &&
    company !== null &&
    (editing !== null || form.file !== null);

  // El PDF solo es obligatorio en alta (en edición se conserva el custodiado si no se reemplaza).
  const filaRequired = editing === null;
  const missingDescription = attempted && form.description.trim() === "";
  const missingDesde = attempted && form.vigenciaDesde === "";
  const missingHasta = attempted && form.vigenciaHasta === "";
  const missingFile = attempted && filaRequired && form.file === null;

  const canSubmit = !submitting;

  const handleSubmit = async () => {
    if (!company) return;
    if (!isValid) {
      // No hay envío al backend: solo revela los mensajes de obligatoriedad por campo.
      setAttempted(true);
      return;
    }
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      const saved = await onSubmit({
        description: form.description.trim(),
        vigenciaDesde: form.vigenciaDesde,
        vigenciaHasta: form.vigenciaHasta,
        // La escritura aplica SIEMPRE a la única compañía fija de contexto.
        companyIds: [company.id],
        file: form.file,
      });
      onSaved(saved);
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const mapped: Record<string, string> = {};
        for (const e of err.errors) {
          if (e.field) mapped[e.field] = e.message;
        }
        setFieldErrors(mapped);
        setBanner("Revisa los campos marcados: hay valores inválidos.");
      } else {
        onError(
          editing
            ? "No se pudo actualizar la escritura. Intenta de nuevo."
            : "No se pudo registrar la escritura. Intenta de nuevo.",
        );
      }
    } finally {
      setSubmitting(false);
    }
  };

  const footer = (
    <button
      type="button"
      disabled={!canSubmit}
      onClick={() => void handleSubmit()}
      className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-50"
      style={{ background: "#557EFF" }}
    >
      {submitting && <Loader2 className="h-4 w-4 animate-spin" />}
      {editing ? "Guardar cambios" : "Registrar escritura"}
    </button>
  );

  const errStyle = (field: string) =>
    fieldErrors[field] ? { borderColor: "#FF4E00" } : undefined;

  return (
    <OtSidePanel
      open={open}
      title={editing ? "Editar escritura" : "Nueva escritura"}
      ariaLabel={editing ? "Editar escritura" : "Registrar escritura"}
      onClose={onClose}
      disabled={submitting}
      footer={footer}
      zClassName={zClassName}
    >
      <div className="space-y-5">
        {banner && (
          <p
            role="alert"
            className="rounded-xl border px-3 py-2 text-[11px] font-medium"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            {banner}
          </p>
        )}

        {/* Compañía fija (contexto del representante): dato de solo lectura, sin selector. */}
        <div>
          <span className="mb-1 block text-xs font-semibold">Compañía</span>
          <div
            className="flex items-center gap-2 rounded-xl border px-3 py-2.5 text-xs"
            style={{ borderColor: "#DFE5ED" }}
          >
            <Building2 className="h-4 w-4 shrink-0" style={{ color: "#557EFF" }} />
            <span className="min-w-0">
              <span className="block truncate font-semibold">{company?.name ?? "—"}</span>
              {company?.nit && <span className="block font-mono opacity-60">{company.nit}</span>}
            </span>
          </div>
        </div>

        <Field
          id="deed-description"
          label="Descripción"
          required
          error={fieldErrors.description || (missingDescription ? "La descripción es obligatoria." : undefined)}
        >
          <input
            id="deed-description"
            value={form.description}
            onChange={(e) => patch({ description: e.target.value })}
            className={OT_INPUT_CLS}
            style={errStyle("description") || (missingDescription ? { borderColor: "#FF4E00" } : undefined)}
            placeholder="Escritura de constitución, poder general…"
            aria-required="true"
            aria-invalid={Boolean(fieldErrors.description) || missingDescription}
            aria-describedby={
              fieldErrors.description || missingDescription ? "deed-description-error" : undefined
            }
          />
        </Field>

        <div>
          <span className="mb-1 block text-xs font-semibold">
            Documento PDF
            {filaRequired ? (
              <span aria-hidden="true"> *</span>
            ) : (
              " (opcional: reemplaza el actual)"
            )}
          </span>
          <label
            htmlFor="deed-file"
            className="flex cursor-pointer items-center gap-2 rounded-xl border border-dashed px-3 py-3 text-xs"
            style={
              form.file
                ? { borderColor: "#557EFF" }
                : missingFile
                  ? { borderColor: "#FF4E00" }
                  : undefined
            }
          >
            {form.file ? (
              <FileText className="h-4 w-4 shrink-0" style={{ color: "#557EFF" }} />
            ) : (
              <UploadCloud className="h-4 w-4 shrink-0 opacity-60" />
            )}
            <span className="truncate font-medium">
              {form.file
                ? form.file.name
                : editing
                  ? "Selecciona un PDF para reemplazar el documento"
                  : "Selecciona el documento PDF de la escritura"}
            </span>
          </label>
          <input
            id="deed-file"
            ref={fileInputRef}
            type="file"
            accept="application/pdf"
            className="sr-only"
            onChange={(e) => patch({ file: e.target.files?.[0] ?? null })}
            aria-required={filaRequired ? "true" : undefined}
            aria-invalid={missingFile}
            aria-describedby={missingFile ? "deed-file-error" : undefined}
          />
          {missingFile && (
            <p id="deed-file-error" role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              El documento PDF es obligatorio.
            </p>
          )}
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field
            id="deed-desde"
            label="Vigencia desde"
            required
            error={fieldErrors.vigenciaDesde || (missingDesde ? "La vigencia desde es obligatoria." : undefined)}
          >
            <input
              id="deed-desde"
              type="date"
              value={form.vigenciaDesde}
              onChange={(e) => patch({ vigenciaDesde: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("vigenciaDesde") || (missingDesde ? { borderColor: "#FF4E00" } : undefined)}
              aria-required="true"
              aria-invalid={Boolean(fieldErrors.vigenciaDesde) || missingDesde}
              aria-describedby={
                fieldErrors.vigenciaDesde || missingDesde ? "deed-desde-error" : undefined
              }
            />
          </Field>
          <Field
            id="deed-hasta"
            label="Vigencia hasta"
            required
            error={fieldErrors.vigenciaHasta || (missingHasta ? "La vigencia hasta es obligatoria." : undefined)}
          >
            <input
              id="deed-hasta"
              type="date"
              value={form.vigenciaHasta}
              onChange={(e) => patch({ vigenciaHasta: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("vigenciaHasta") || (missingHasta ? { borderColor: "#FF4E00" } : undefined)}
              aria-required="true"
              aria-invalid={Boolean(fieldErrors.vigenciaHasta) || missingHasta}
              aria-describedby={
                fieldErrors.vigenciaHasta || missingHasta ? "deed-hasta-error" : undefined
              }
            />
          </Field>
        </div>
      </div>
    </OtSidePanel>
  );
}

function Field({
  id,
  label,
  error,
  required,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  /** Marca visualmente el campo como obligatorio (asterisco junto al label). */
  required?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
        {required && (
          <span aria-hidden="true" style={{ color: "#FF4E00" }}>
            {" "}
            *
          </span>
        )}
      </label>
      {children}
      {error && (
        <p
          id={`${id}-error`}
          className="mt-1 text-[11px] font-medium"
          style={{ color: "#FF4E00" }}
          role="alert"
        >
          {error}
        </p>
      )}
    </div>
  );
}
