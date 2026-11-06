"use client";

import { useRef, useState } from "react";
import { ImageUp, Loader2 } from "lucide-react";
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
}: BrandingLogoUploaderProps<T>) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [localPreview, setLocalPreview] = useState<string | null>(null);

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;

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
      <div className="flex items-center gap-4">
        <div
          className={`flex ${previewClassName} shrink-0 items-center justify-center overflow-hidden rounded-xl border bg-white`}
          style={{ borderColor: "#DFE5ED" }}
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
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden /> Cargando…
              </>
            ) : (
              buttonLabel
            )}
          </button>
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
        <p role="alert" className="text-[11px]" style={{ color: "#FF4E00" }}>
          {error}
        </p>
      )}
    </div>
  );
}
