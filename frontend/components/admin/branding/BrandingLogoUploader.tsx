"use client";

import { useRef, useState } from "react";
import { ImageUp } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { brandingErrorMessage } from "@/lib/brand/error-messages";
import { logoDimensionsHint, validateLogoFile } from "@/lib/brand/validate-logo";
import { brandingErrorCode, uploadBrandLogo, type BrandLogoResponse, type BrandingSource } from "@/lib/api/branding";

export interface BrandingLogoUploaderProps<T = BrandLogoResponse> {
  /** Marca blanca: base de endpoints. No hace falta cuando se inyecta `upload`. */
  source?: BrandingSource;
  tenantId?: string;
  /** URL del logotipo vigente en el borrador (o publicado si no hay borrador con logo propio). */
  currentLogoUrl: string | null;
  onUploaded: (logo: T) => void;
  disabled?: boolean;
  // Feature #14374 (portal del OT): la pieza se reutiliza con otro cliente de API y otras reglas de archivo.
  // Sin estas propiedades el comportamiento es exactamente el de marca blanca.
  /** Sube el archivo (por defecto, `uploadBrandLogo` de marca blanca). Los errores se muestran con su `message`. */
  upload?: (file: File) => Promise<T>;
  /** Valida el archivo antes de subirlo; devuelve el motivo exacto si no sirve (por defecto, `validateLogoFile`). */
  validate?: (file: File) => Promise<string | null>;
  label?: string;
  buttonLabel?: string;
  hint?: string;
  accept?: string;
  inputAriaLabel?: string;
  imageAlt?: string;
  /** Clases del recuadro de vista previa (por defecto, cuadrado de 64 px). */
  previewClassName?: string;
  /** Feature #14374 (portal del OT): también admite arrastrar el archivo. Sin él, solo el botón de marca blanca. */
  dropzone?: boolean;
}

/** HU #12414 AC2 — carga del logotipo con validación en cliente y en servidor (mismo texto). */
export function BrandingLogoUploader<T = BrandLogoResponse>({
  source = "company",
  tenantId,
  currentLogoUrl,
  onUploaded,
  disabled = false,
  upload,
  validate,
  label = "Logotipo",
  buttonLabel = "Cambiar logotipo",
  hint,
  accept = "image/png,image/jpeg",
  inputAriaLabel = "Cargar logotipo de marca",
  imageAlt = "Logotipo de la marca",
  previewClassName = "h-16 w-16",
  dropzone = false,
}: BrandingLogoUploaderProps<T>) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [localPreview, setLocalPreview] = useState<string | null>(null);
  const [dragging, setDragging] = useState(false);

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (file) await handleFile(file);
  }

  function handleDrop(e: React.DragEvent<HTMLDivElement>) {
    e.preventDefault();
    setDragging(false);
    const file = e.dataTransfer.files?.[0];
    if (file && !disabled && !uploading) void handleFile(file);
  }

  async function handleFile(file: File) {
    setError(null);
    if (validate) {
      const reason = await validate(file);
      if (reason) {
        setError(reason);
        return;
      }
    } else {
      const clientResult = await validateLogoFile(file);
      if (!clientResult.ok) {
        setError(brandingErrorMessage(clientResult.code));
        return;
      }
    }

    const previewUrl = URL.createObjectURL(file);
    setLocalPreview(previewUrl);
    setUploading(true);
    try {
      const uploaded = upload ? await upload(file) : ((await uploadBrandLogo(source, file, tenantId)) as T);
      onUploaded(uploaded);
    } catch (err) {
      // El servidor lo rechazó: no se guardó, así que la vista previa vuelve al logotipo vigente.
      setLocalPreview(null);
      setError(
        upload
          ? err instanceof Error && err.message.trim()
            ? err.message
            : "No se pudo cargar la imagen."
          : brandingErrorMessage(brandingErrorCode(err)) || "No se pudo cargar el logotipo.",
      );
    } finally {
      setUploading(false);
    }
  }

  const previewSrc = localPreview ?? currentLogoUrl;

  return (
    <div className="flex flex-col gap-2">
      <span className="text-xs font-semibold" style={{ color: "var(--color-flit-primary)" }}>
        {label}
      </span>
      <div
        className={dropzone ? "flex items-center gap-4 rounded-xl border border-dashed p-3" : "flex items-center gap-4"}
        style={dropzone ? { borderColor: dragging ? "var(--color-flit-brand)" : "var(--badge-info-border)", background: dragging ? "var(--secondary)" : undefined } : undefined}
        data-testid={dropzone ? "zona-carga" : undefined}
        onDragOver={
          dropzone
            ? (e) => {
                e.preventDefault();
                setDragging(true);
              }
            : undefined
        }
        onDragLeave={dropzone ? () => setDragging(false) : undefined}
        onDrop={dropzone ? handleDrop : undefined}
      >
        <div
          className={`flex ${previewClassName} shrink-0 items-center justify-center overflow-hidden rounded-xl border bg-white`}
          style={{ borderColor: "var(--border)" }}
        >
          {previewSrc ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={previewSrc} alt={imageAlt} className="h-full w-full object-contain" />
          ) : (
            <ImageUp className="h-6 w-6 opacity-40" aria-hidden />
          )}
        </div>
        <div className="flex flex-col gap-1">
          <button
            type="button"
            disabled={disabled || uploading}
            onClick={() => inputRef.current?.click()}
            className="inline-flex w-fit items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
            style={{ borderColor: "var(--color-flit-brand)", color: "var(--color-flit-brand)" }}
          >
            {uploading ? (
              <>
                <Icono nombre="girador" className="h-3.5 w-3.5 animate-spin" /> Cargando…
              </>
            ) : (
              buttonLabel
            )}
          </button>
          {dropzone && <p className="text-xs opacity-70">O arrastra el archivo aquí.</p>}
          <p className="text-[11px] opacity-60">{hint ?? logoDimensionsHint()}</p>
        </div>
      </div>
      <input
        ref={inputRef}
        type="file"
        accept={accept}
        aria-label={inputAriaLabel}
        className="sr-only"
        onChange={(e) => void handleFileChange(e)}
      />
      {error && (
        <p role="alert" className="text-[11px]" style={{ color: "var(--destructive)" }}>
          {error}
        </p>
      )}
    </div>
  );
}
