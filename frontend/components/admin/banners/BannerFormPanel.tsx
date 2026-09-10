"use client";

import { useEffect, useRef, useState } from "react";
import { AlertTriangle, Loader2, UploadCloud } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import {
  bannerDateInputValue,
  bannerImageUrl,
  type Banner,
  type BannerFormInput,
} from "@/lib/api/admin-banners";

const INPUT_CLS =
  "w-full rounded-xl border bg-transparent px-3 py-2.5 text-xs outline-none focus:border-[#557EFF] disabled:opacity-60";

const ALLOWED_IMAGE_TYPES = ["image/png", "image/jpeg", "image/webp"];
const MAX_IMAGE_BYTES = 2 * 1024 * 1024;

export interface BannerFormPanelProps {
  open: boolean;
  /** Banner a editar; `null` = alta. */
  editing: Banner | null;
  onClose: () => void;
  onSubmit: (input: BannerFormInput) => Promise<Banner>;
  onSaved: (saved: Banner) => void;
}

interface FormState {
  name: string;
  linkUrl: string;
  validFrom: string;
  validUntil: string;
  file: File | null;
}

const EMPTY: FormState = { name: "", linkUrl: "", validFrom: "", validUntil: "", file: null };

function fromEditing(b: Banner): FormState {
  return {
    name: b.name,
    linkUrl: b.linkUrl ?? "",
    validFrom: bannerDateInputValue(b.validFrom),
    validUntil: bannerDateInputValue(b.validUntil),
    file: null,
  };
}

/**
 * Panel de alta/edición de un banner (HU #12241 AC2/AC4): nombre, enlace opcional, vigencia
 * (DD/MM/AAAA, opcional) e imagen con vista previa en vivo. La vista previa usa
 * `URL.createObjectURL` sobre el archivo local ANTES de guardar (AC2); tras guardar/editar sin
 * archivo nuevo, cae al endpoint público de imagen (`bannerImageUrl`). AC4: guía de tamaño
 * recomendado visible junto al campo de imagen — puramente informativa, no bloquea el envío.
 */
export function BannerFormPanel({ open, editing, onClose, onSubmit, onSaved }: BannerFormPanelProps) {
  const [form, setForm] = useState<FormState>(EMPTY);
  const [attempted, setAttempted] = useState(false);
  const [submitting, setSubmitting] = useState(false);
  const [banner, setBanner] = useState<string | null>(null);
  const [previewUrl, setPreviewUrl] = useState<string | null>(null);
  const fileInputRef = useRef<HTMLInputElement>(null);

  useEffect(() => {
    if (!open) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza el formulario al abrir el panel
    setForm(editing ? fromEditing(editing) : EMPTY);
    setAttempted(false);
    setBanner(null);
    if (fileInputRef.current) fileInputRef.current.value = "";
  }, [open, editing]);

  // Vista previa en vivo (AC2): blob local mientras no se ha guardado; si se edita sin elegir un
  // archivo nuevo, cae a la imagen ya custodiada (endpoint público). El objectURL se revoca al
  // cambiar de archivo o desmontar, para no acumular memoria.
  useEffect(() => {
    if (!form.file) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- sincroniza la vista previa con el archivo local elegido (o su ausencia).
      setPreviewUrl(editing ? bannerImageUrl(editing.id) : null);
      return;
    }
    const url = URL.createObjectURL(form.file);
    setPreviewUrl(url);
    return () => URL.revokeObjectURL(url);
  }, [form.file, editing]);

  const patch = (p: Partial<FormState>) => setForm((f) => ({ ...f, ...p }));

  const fileRequired = editing === null;
  const missingName = attempted && form.name.trim() === "";
  const missingFile = attempted && fileRequired && form.file === null;
  const missingDatePair =
    attempted && (form.validFrom !== "") !== (form.validUntil !== "");
  const invalidDateRange =
    attempted &&
    form.validFrom !== "" &&
    form.validUntil !== "" &&
    form.validUntil <= form.validFrom;
  const invalidImageType =
    attempted && form.file !== null && !ALLOWED_IMAGE_TYPES.includes(form.file.type);
  const invalidImageSize =
    attempted && form.file !== null && form.file.size > MAX_IMAGE_BYTES;

  const isValid =
    form.name.trim() !== "" &&
    (!fileRequired || form.file !== null) &&
    (form.validFrom !== "") === (form.validUntil !== "") &&
    !(form.validFrom !== "" && form.validUntil !== "" && form.validUntil <= form.validFrom) &&
    (form.file === null || (ALLOWED_IMAGE_TYPES.includes(form.file.type) && form.file.size <= MAX_IMAGE_BYTES));

  const handleSubmit = async () => {
    if (!isValid) {
      setAttempted(true);
      return;
    }
    setSubmitting(true);
    setBanner(null);
    try {
      const saved = await onSubmit({
        name: form.name.trim(),
        linkUrl: form.linkUrl.trim(),
        validFrom: form.validFrom,
        validUntil: form.validUntil,
        file: form.file,
      });
      onSaved(saved);
    } catch (err) {
      setBanner(err instanceof Error ? err.message : "No se pudo guardar el banner. Intenta de nuevo.");
    } finally {
      setSubmitting(false);
    }
  };

  return (
    <Modal
      open={open}
      onClose={onClose}
      title={editing ? "Editar banner" : "Nuevo banner"}
      titleClassName="text-base font-bold text-[#557EFF]"
      busy={submitting}
      size="lg"
    >
      <div className="space-y-5">
        {banner && (
          <p
            role="alert"
            className="flex items-start gap-2 rounded-xl border px-3 py-2 text-[11px] font-medium"
            style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
          >
            <AlertTriangle className="mt-0.5 h-3.5 w-3.5 shrink-0" aria-hidden="true" />
            <span>{banner}</span>
          </p>
        )}

        <Field
          id="banner-name"
          label="Nombre"
          required
          error={missingName ? "El nombre del banner es obligatorio." : undefined}
        >
          <input
            id="banner-name"
            value={form.name}
            onChange={(e) => patch({ name: e.target.value })}
            className={INPUT_CLS}
            style={missingName ? { borderColor: "#FF4E00" } : { borderColor: "#DFE5ED" }}
            placeholder="Promo verano 2026"
            aria-required="true"
            aria-invalid={missingName}
            aria-describedby={missingName ? "banner-name-error" : undefined}
          />
        </Field>

        <Field id="banner-link" label="Enlace (opcional)">
          <input
            id="banner-link"
            type="url"
            value={form.linkUrl}
            onChange={(e) => patch({ linkUrl: e.target.value })}
            className={INPUT_CLS}
            style={{ borderColor: "#DFE5ED" }}
            placeholder="https://flitsas.com/promo"
          />
        </Field>

        <div className="grid grid-cols-1 gap-3 sm:grid-cols-2">
          <Field
            id="banner-desde"
            label="Fecha inicio (opcional)"
            error={
              missingDatePair
                ? "Indica ambas fechas de vigencia, o ninguna."
                : invalidDateRange
                  ? "La fecha de fin debe ser posterior a la de inicio."
                  : undefined
            }
          >
            <input
              id="banner-desde"
              type="date"
              value={form.validFrom}
              onChange={(e) => patch({ validFrom: e.target.value })}
              className={INPUT_CLS}
              style={
                missingDatePair || invalidDateRange ? { borderColor: "#FF4E00" } : { borderColor: "#DFE5ED" }
              }
              aria-invalid={missingDatePair || invalidDateRange}
            />
          </Field>
          <Field id="banner-hasta" label="Fecha fin (opcional)">
            <input
              id="banner-hasta"
              type="date"
              value={form.validUntil}
              onChange={(e) => patch({ validUntil: e.target.value })}
              className={INPUT_CLS}
              style={
                missingDatePair || invalidDateRange ? { borderColor: "#FF4E00" } : { borderColor: "#DFE5ED" }
              }
              aria-invalid={missingDatePair || invalidDateRange}
            />
          </Field>
        </div>
        <p className="text-[11px] opacity-60">
          Si no defines fechas, el banner queda vigente desde ya y sin fecha de expiración
          (&ldquo;Sin fecha programada&rdquo; en el listado).
        </p>

        <div>
          <span className="mb-1 block text-xs font-semibold">
            Imagen del banner
            {fileRequired ? (
              <span aria-hidden="true"> *</span>
            ) : (
              " (opcional: reemplaza la actual)"
            )}
          </span>
          <label
            htmlFor="banner-file"
            className="flex cursor-pointer items-center gap-2 rounded-xl border border-dashed px-3 py-3 text-xs"
            style={{
              borderColor:
                missingFile || invalidImageType || invalidImageSize
                  ? "#FF4E00"
                  : form.file
                    ? "#557EFF"
                    : "#DFE5ED",
            }}
          >
            <UploadCloud className="h-4 w-4 shrink-0 opacity-60" aria-hidden="true" />
            <span className="truncate font-medium">
              {form.file
                ? form.file.name
                : editing
                  ? "Selecciona una imagen para reemplazar el banner"
                  : "Selecciona la imagen del banner"}
            </span>
          </label>
          <input
            id="banner-file"
            ref={fileInputRef}
            type="file"
            accept="image/png,image/jpeg,image/webp"
            className="sr-only"
            onChange={(e) => patch({ file: e.target.files?.[0] ?? null })}
            aria-required={fileRequired ? "true" : undefined}
            aria-invalid={missingFile || invalidImageType || invalidImageSize}
            aria-describedby="banner-file-guide"
          />
          {/* AC4 — guía de tamaño recomendado, puramente informativa: no bloquea imágenes con otra
              proporción, siempre que cumplan formato/tamaño (validados también en backend). */}
          <p id="banner-file-guide" className="mt-1.5 text-[11px] opacity-70">
            Tamaño recomendado: 1600 x 400 px (proporción 4:1), formato PNG, JPEG o WEBP, máximo
            2MB. Mantén el contenido importante (texto, logo) centrado: la imagen se recorta
            distinto en el carrusel del gestor y en el del organismo de tránsito.
          </p>
          {missingFile && (
            <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              La imagen del banner es obligatoria.
            </p>
          )}
          {invalidImageType && (
            <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              Formato no permitido. Usa PNG, JPEG o WEBP.
            </p>
          )}
          {invalidImageSize && (
            <p role="alert" className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }}>
              La imagen supera el tamaño máximo de 2MB.
            </p>
          )}
        </div>

        {previewUrl && (
          <div>
            <span className="mb-1 block text-xs font-semibold">Vista previa</span>
            <div className="overflow-hidden rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
              {/* eslint-disable-next-line @next/next/no-img-element -- blob local o binario del endpoint público, no un asset estático */}
              <img src={previewUrl} alt="Vista previa en vivo del banner" className="max-h-40 w-full object-cover" />
            </div>
          </div>
        )}
      </div>

      <div className="mt-5 border-t pt-4" style={{ borderColor: "#DFE5ED" }}>
        <button
          type="button"
          disabled={submitting}
          onClick={() => void handleSubmit()}
          className="flex w-full items-center justify-center gap-2 rounded-xl py-2.5 text-xs font-semibold text-white disabled:opacity-60"
          style={{ background: "linear-gradient(135deg,#557EFF 0%,#00DBD5 100%)" }}
        >
          {submitting && <Loader2 className="h-4 w-4 animate-spin" aria-hidden="true" />}
          {editing ? "Guardar cambios" : "Crear banner"}
        </button>
      </div>
    </Modal>
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
        <p id={`${id}-error`} className="mt-1 text-[11px] font-medium" style={{ color: "#FF4E00" }} role="alert">
          {error}
        </p>
      )}
    </div>
  );
}
