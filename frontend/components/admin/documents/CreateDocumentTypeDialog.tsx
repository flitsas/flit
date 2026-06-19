"use client";

import { useEffect, useState } from "react";
import { FileText, Loader2, X } from "lucide-react";
import { ApiValidationError } from "@/lib/api/types";
import type {
  CreateDocumentTypeRequest,
  DocumentType,
  UpdateDocumentTypeRequest,
} from "@/lib/api/types-documents";

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
  const [errors, setErrors] = useState<FieldErrors>({});
  const [submitting, setSubmitting] = useState(false);

  // Sincroniza el formulario al abrir/cambiar el documento en edición.
  useEffect(() => {
    if (open) {
      setCodigo(editing?.codigo ?? "");
      setNombre(editing?.nombre ?? "");
      setDescripcion(editing?.descripcion ?? "");
      setErrors({});
    }
  }, [open, editing]);

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
    if (!codigo.trim()) next.codigo = "El código es obligatorio.";
    if (!nombre.trim()) next.nombre = "El nombre es obligatorio.";
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
      const saved = await onSubmit({
        codigo: codigo.trim(),
        nombre: nombre.trim(),
        descripcion: descripcion.trim() || null,
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
    <div
      className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
      role="dialog"
      aria-modal="true"
      aria-labelledby="dt-dialog-title"
    >
      <div className="w-full max-w-md rounded-2xl border bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ borderColor: "#DFE5ED" }}>
        <div className="mb-4 flex items-start justify-between">
          <div className="flex items-center gap-2">
            <span className="flex h-9 w-9 items-center justify-center rounded-xl" style={{ background: "#557EFF" }}>
              <FileText className="h-4.5 w-4.5 text-white" />
            </span>
            <h2 id="dt-dialog-title" className="text-base font-bold" style={{ color: "#557EFF" }}>
              {title}
            </h2>
          </div>
          <button type="button" aria-label="Cerrar" onClick={handleClose} className="text-slate-400 hover:text-slate-700">
            <X className="h-5 w-5" />
          </button>
        </div>

        <div className="space-y-3.5">
          <Field label="Código" htmlFor="dt-codigo" error={errors.codigo} hint="Identificador único (máx. 50).">
            <input
              id="dt-codigo"
              type="text"
              value={codigo}
              onChange={(e) => setCodigo(e.target.value)}
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
              onChange={(e) => setNombre(e.target.value)}
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
              onChange={(e) => setDescripcion(e.target.value)}
              maxLength={500}
              rows={3}
              aria-describedby={errors.descripcion ? "dt-desc-error" : undefined}
              className="w-full resize-none rounded-xl border px-3 py-2 text-xs outline-none focus:border-[#557EFF] focus:ring-2 focus:ring-[#557EFF]/20"
              style={{ borderColor: errors.descripcion ? "#FF4E00" : "#DFE5ED" }}
            />
          </Field>

          <div className="flex justify-end gap-2 pt-1">
            <button
              type="button"
              onClick={handleClose}
              disabled={submitting}
              className="rounded-xl border px-4 py-2 text-xs font-semibold disabled:opacity-50"
              style={{ borderColor: "#DFE5ED" }}
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
      </div>
    </div>
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
