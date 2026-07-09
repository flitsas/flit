"use client";

import { useEffect, useState } from "react";
import { FileText, Loader2 } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { ApiValidationError } from "@/lib/api/types";
import type {
  CreateDocumentTypeRequest,
  DocumentType,
  UpdateDocumentTypeRequest,
} from "@/lib/api/types-documents";
import {
  hasLetterOrDigit,
  sanitizeDocCode,
  sanitizeName,
  sanitizeNoAngleBrackets,
  validateReadableName,
} from "@/lib/validation/fieldRules";

// Modal de alta/edición de tipo de documento (HU #10198, AC1). Valida campos
// obligatorios (código, nombre) en cliente antes de enviar y mapea los errores 422
// del backend por campo (error inline). La llamada a la API se inyecta vía
// `onSubmit` (componente presentacional, igual que CreateCompanyDialog).
export interface CreateDocumentTypeDialogProps {
  open: boolean;
  /** Documento a editar; si es null/undefined el modal está en modo alta. */
  editing?: DocumentType | null;
  onClose: () => void;
  /** Persiste el documento. Debe lanzar ApiValidationError ante un 422 del backend. */
  onSubmit: (
    body: CreateDocumentTypeRequest | UpdateDocumentTypeRequest,
  ) => Promise<DocumentType>;
  onSaved: (documentType: DocumentType, mode: "create" | "edit") => void;
}

type FieldErrors = Partial<Record<"codigo" | "nombre" | "descripcion", string>>;

// RF08 — formatos configurables por tipo (los mismos que el respaldo global del backend).
const MIME_OPTIONS: { mime: string; label: string }[] = [
  { mime: "application/pdf", label: "PDF" },
  { mime: "image/jpeg", label: "JPG" },
  { mime: "image/png", label: "PNG" },
  { mime: "image/webp", label: "WEBP" },
];

const BYTES_PER_MB = 1024 * 1024;

export function CreateDocumentTypeDialog({
  open,
  editing,
  onClose,
  onSubmit,
  onSaved,
}: CreateDocumentTypeDialogProps) {
  const mode = editing ? "edit" : "create";
  const [codigo, setCodigo] = useState("");
  const [nombre, setNombre] = useState("");
  const [descripcion, setDescripcion] = useState("");
  // RF08/09 — límites por tipo. mimes vacío / MB vacío ⇒ se aplican los globales por defecto.
  const [mimes, setMimes] = useState<string[]>([]);
  const [maxSizeMb, setMaxSizeMb] = useState("");
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  // Sincroniza el formulario al abrir/cambiar el documento en edición.
  useEffect(() => {
    if (open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- reset de formulario al abrir modal (mismo patrón que companies/documents tabs)
      setCodigo(editing?.codigo ?? "");
      setNombre(editing?.nombre ?? "");
      setDescripcion(editing?.descripcion ?? "");
      setMimes(editing?.mimeTypesAllowed ?? []);
      const bytes = editing?.maxSizeBytes ?? 0;
      setMaxSizeMb(bytes > 0 ? String(Math.round((bytes / BYTES_PER_MB) * 100) / 100) : "");
      setErrors({});
    }
  }, [open, editing]);

  const toggleMime = (mime: string) =>
    setMimes((prev) => (prev.includes(mime) ? prev.filter((m) => m !== mime) : [...prev, mime]));

  if (!open) {
    return null;
  }

  const handleClose = () => {
    if (submitting) {
      return;
    }
    onClose();
  };

  // Validación client-side de campos requeridos antes de llamar a la API.
  const validate = (): FieldErrors => {
    const next: FieldErrors = {};
    const cod = codigo.trim();
    const nom = nombre.trim();
    if (!cod) next.codigo = "El código es obligatorio.";
    else if (!hasLetterOrDigit(cod)) next.codigo = "El código debe contener al menos una letra o número.";
    if (!nom) next.nombre = "El nombre es obligatorio.";
    else { const e = validateReadableName(nom, "El nombre"); if (e) next.nombre = e; }
    return next;
  };

  const submit = async () => {
    const clientErrors = validate();
    if (Object.keys(clientErrors).length > 0) {
      setErrors(clientErrors);
      return;
    }

    setSubmitting(true);
    setErrors({});
    try {
      const mb = maxSizeMb.trim() ? Number(maxSizeMb) : NaN;
      const saved = await onSubmit({
        codigo: codigo.trim(),
        nombre: nombre.trim(),
        descripcion: descripcion.trim() || null,
        // Vacío ⇒ null ⇒ el backend aplica los límites globales por defecto.
        mimeTypesAllowed: mimes.length > 0 ? mimes : null,
        maxSizeBytes: Number.isFinite(mb) && mb > 0 ? Math.round(mb * BYTES_PER_MB) : null,
      });
      onSaved(saved, mode);
    } catch (error) {
      if (error instanceof ApiValidationError) {
        const mapped: FieldErrors = {};
        for (const { field, message } of error.errors) {
          if (field === "codigo" || field === "nombre" || field === "descripcion") {
            mapped[field] = message;
          }
        }
        setErrors(
          Object.keys(mapped).length > 0 ? mapped : { codigo: "No se pudo guardar el documento." },
        );
      } else {
        setErrors({ codigo: "No se pudo guardar el documento. Intenta de nuevo." });
      }
    } finally {
      setSubmitting(false);
    }
  };

  const title = mode === "edit" ? "Editar tipo de documento" : "Crear tipo de documento";
  const cta = mode === "edit" ? "Guardar cambios" : "Crear documento";

  return (
    <Modal
      open={open}
      onClose={handleClose}
      busy={submitting}
      icon={FileText}
      title={title}
      titleClassName="text-base font-bold text-[#557EFF]"
    >
      <div className="space-y-3.5">
          <Field label="Código" htmlFor="dt-codigo" error={errors.codigo} hint="Identificador único (máx. 50).">
            <input
              id="dt-codigo"
              type="text"
              value={codigo}
              onChange={(e) => setCodigo(sanitizeDocCode(e.target.value))}
              maxLength={50}
              aria-describedby={errors.codigo ? "dt-codigo-error" : undefined}
              className="w-full rounded-xl border px-3 py-2 font-mono text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.codigo ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <Field label="Nombre" htmlFor="dt-nombre" error={errors.nombre}>
            <input
              id="dt-nombre"
              type="text"
              value={nombre}
              onChange={(e) => setNombre(sanitizeName(e.target.value))}
              maxLength={150}
              aria-describedby={errors.nombre ? "dt-nombre-error" : undefined}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.nombre ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <Field label="Descripción" htmlFor="dt-desc" error={errors.descripcion} hint="Opcional (máx. 500).">
            <textarea
              id="dt-desc"
              value={descripcion}
              onChange={(e) => setDescripcion(sanitizeNoAngleBrackets(e.target.value))}
              maxLength={500}
              rows={3}
              aria-describedby={errors.descripcion ? "dt-desc-error" : undefined}
              className="w-full resize-none rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.descripcion ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <div>
            <label className="mb-1 block text-xs font-semibold">Formatos permitidos</label>
            <div className="flex flex-wrap gap-3">
              {MIME_OPTIONS.map(({ mime, label }) => (
                <label key={mime} className="flex items-center gap-1.5 text-xs">
                  <input
                    type="checkbox"
                    checked={mimes.includes(mime)}
                    onChange={() => toggleMime(mime)}
                    className="h-3.5 w-3.5 accent-[#557EFF]"
                  />
                  {label}
                </label>
              ))}
            </div>
            <p className="mt-1 text-[10px] opacity-60">
              Sin marcar ninguno ⇒ se aplican los formatos globales por defecto (PDF, JPG, PNG, WEBP).
            </p>
          </div>

          <Field label="Tamaño máximo (MB)" htmlFor="dt-maxsize" hint="Opcional; vacío ⇒ tamaño global por defecto (20 MB).">
            <input
              id="dt-maxsize"
              type="number"
              min={0}
              step="0.5"
              value={maxSizeMb}
              onChange={(e) => setMaxSizeMb(e.target.value)}
              className="w-full rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
            />
          </Field>

          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={handleClose}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={submit}
              disabled={submitting}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              {submitting && <Loader2 className="h-3.5 w-3.5 animate-spin" />}
              {submitting ? "Guardando…" : cta}
            </button>
          </div>
        </div>
    </Modal>
  );
}

function Field({
  label,
  htmlFor,
  error,
  hint,
  children,
}: {
  label: string;
  htmlFor: string;
  error?: string;
  hint?: string;
  children: React.ReactNode;
}) {
  return (
    <div>
      <label htmlFor={htmlFor} className="mb-1 block text-xs font-semibold">
        {label}
      </label>
      {children}
      {hint && !error && <p className="mt-1 text-[10px] opacity-60">{hint}</p>}
      {error && (
        <p id={`${htmlFor}-error`} className="mt-1 text-[10px] font-medium" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
