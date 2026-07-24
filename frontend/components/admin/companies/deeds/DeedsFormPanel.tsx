"use client";

import { useEffect, useRef, useState } from "react";
import { FileText, Loader2, UploadCloud } from "lucide-react";
import { OtSidePanel } from "@/components/admin/transit-offices/OtSidePanel";
import { OT_INPUT_CLS } from "@/components/admin/transit-offices/ot-form-styles";
import { ApiValidationError } from "@/lib/api/types";
import type {
  DeedFormInput,
  DeedItem,
  DeedSaved,
  RepresentedCompany,
} from "@/lib/api/admin-deeds";

export interface DeedsFormPanelProps {
  open: boolean;
  /** Escritura a editar; `null` = alta. */
  editing: DeedItem | null;
  companies: RepresentedCompany[];
  companiesLoading: boolean;
  onClose: () => void;
  onSubmit: (input: DeedFormInput) => Promise<DeedSaved>;
  onSaved: (saved: DeedSaved) => void;
  onError: (message: string) => void;
}

interface FormState {
  description: string;
  vigenciaDesde: string;
  vigenciaHasta: string;
  companyIds: string[];
  file: File | null;
}

const EMPTY: FormState = {
  description: "",
  vigenciaDesde: "",
  vigenciaHasta: "",
  companyIds: [],
  file: null,
};

function fromItem(item: DeedItem): FormState {
  return {
    description: item.description,
    vigenciaDesde: item.vigenciaDesde,
    vigenciaHasta: item.vigenciaHasta,
    companyIds: [...item.representedCompanyIds],
    file: null,
  };
}

/**
 * Panel de alta/edición de una escritura (HU #10905): descripción, carga del PDF, vigencia y
 * multi-selección de compañías registradas. En alta el PDF es obligatorio; en edición es opcional
 * (si no se elige uno nuevo, se conserva el custodiado). Los errores 422 del backend
 * (`description`/`vigenciaHasta`/`representedCompanyIds`) se muestran por campo.
 */
export function DeedsFormPanel({
  open,
  editing,
  companies,
  companiesLoading,
  onClose,
  onSubmit,
  onSaved,
  onError,
}: DeedsFormPanelProps) {
  const [form, setForm] = useState<FormState>(EMPTY);
  const [fieldErrors, setFieldErrors] = useState<Record<string, string>>({});
  const [banner, setBanner] = useState<string | null>(null);
  const [submitting, setSubmitting] = useState(false);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    // Reinicia el formulario al abrir (alta en blanco o edición precargada).
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el formulario al abrir el panel
    setForm(editing ? fromItem(editing) : EMPTY);
    setFieldErrors({});
    setBanner(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, [open, editing]);

  const patch = (p: Partial<FormState>) => setForm((f) => ({ ...f, ...p }));

  const toggleCompany = (id: string) =>
    setForm((f) => ({
      ...f,
      companyIds: f.companyIds.includes(id)
        ? f.companyIds.filter((x) => x !== id)
        : [...f.companyIds, id],
    }));

  // Validación en cliente (los mismos requeridos que valida el backend). En alta, el PDF es obligatorio.
  const isValid =
    form.description.trim() !== "" &&
    form.vigenciaDesde !== "" &&
    form.vigenciaHasta !== "" &&
    form.companyIds.length > 0 &&
    (editing !== null || form.file !== null);

  const canSubmit = isValid && !submitting;

  const handleSubmit = async () => {
    setSubmitting(true);
    setBanner(null);
    setFieldErrors({});
    try {
      const saved = await onSubmit({
        description: form.description.trim(),
        vigenciaDesde: form.vigenciaDesde,
        vigenciaHasta: form.vigenciaHasta,
        companyIds: form.companyIds,
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

        <Field id="deed-description" label="Descripción" error={fieldErrors.description}>
          <input
            id="deed-description"
            value={form.description}
            onChange={(e) => patch({ description: e.target.value })}
            className={OT_INPUT_CLS}
            style={errStyle("description")}
            placeholder="Escritura de constitución, poder general…"
          />
        </Field>

        <div>
          <span className="mb-1 block text-xs font-semibold">
            Documento PDF{editing ? " (opcional: reemplaza el actual)" : ""}
          </span>
          <label
            htmlFor="deed-file"
            className="flex cursor-pointer items-center gap-2 rounded-xl border border-dashed px-3 py-3 text-xs"
            style={form.file ? { borderColor: "#557EFF" } : undefined}
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
          />
        </div>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field id="deed-desde" label="Vigencia desde" error={fieldErrors.vigenciaDesde}>
            <input
              id="deed-desde"
              type="date"
              value={form.vigenciaDesde}
              onChange={(e) => patch({ vigenciaDesde: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("vigenciaDesde")}
            />
          </Field>
          <Field id="deed-hasta" label="Vigencia hasta" error={fieldErrors.vigenciaHasta}>
            <input
              id="deed-hasta"
              type="date"
              value={form.vigenciaHasta}
              onChange={(e) => patch({ vigenciaHasta: e.target.value })}
              className={OT_INPUT_CLS}
              style={errStyle("vigenciaHasta")}
            />
          </Field>
        </div>

        <fieldset className="space-y-2">
          <legend className="text-[11px] font-bold uppercase tracking-wide opacity-60">
            Compañías a las que aplica
          </legend>
          {fieldErrors.representedCompanyIds && (
            <p className="text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
              {fieldErrors.representedCompanyIds}
            </p>
          )}
          {companiesLoading ? (
            <p className="text-[11px] opacity-60">Cargando compañías…</p>
          ) : companies.length === 0 ? (
            <p className="text-[11px] opacity-60">
              No hay compañías registradas. Registra representantes legales para poder asociarlas.
            </p>
          ) : (
            <div className="grid grid-cols-1 gap-2">
              {companies.map((c) => {
                const checked = form.companyIds.includes(c.id);
                return (
                  <label
                    key={c.id}
                    className="flex cursor-pointer items-center gap-2 rounded-xl border px-3 py-2 text-xs"
                    style={checked ? { borderColor: "#557EFF" } : undefined}
                  >
                    <input
                      type="checkbox"
                      checked={checked}
                      onChange={() => toggleCompany(c.id)}
                      className="h-3.5 w-3.5 accent-[#557EFF]"
                    />
                    <span className="min-w-0">
                      <span className="block truncate font-semibold">{c.name}</span>
                      <span className="block font-mono opacity-60">{c.nit}</span>
                    </span>
                  </label>
                );
              })}
            </div>
          )}
        </fieldset>
      </div>
    </OtSidePanel>
  );
}

function Field({
  id,
  label,
  error,
  children,
}: {
  id: string;
  label: string;
  error?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={id} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {error && (
        <p className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
