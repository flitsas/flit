"use client";

import { useRef, useState } from "react";
import { ImageUp, Loader2 } from "lucide-react";
import { brandingErrorMessage } from "@/lib/brand/error-messages";
import { logoDimensionsHint, validateLogoFile } from "@/lib/brand/validate-logo";
import { brandingErrorCode, uploadBrandLogo, type BrandLogoResponse, type BrandingSource } from "@/lib/api/branding";

export interface BrandingLogoUploaderProps {
  source: BrandingSource;
  tenantId?: string;
  /** URL del logotipo vigente en el borrador (o publicado si no hay borrador con logo propio). */
  currentLogoUrl: string | null;
  onUploaded: (logo: BrandLogoResponse) => void;
  disabled?: boolean;
}

/** HU #12414 AC2 — carga del logotipo con validación en cliente y en servidor (mismo texto). */
export function BrandingLogoUploader({
  source,
  tenantId,
  currentLogoUrl,
  onUploaded,
  disabled = false,
}: BrandingLogoUploaderProps) {
  const inputRef = useRef<HTMLInputElement>(null);
  const [uploading, setUploading] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [localPreview, setLocalPreview] = useState<string | null>(null);

  async function handleFileChange(e: React.ChangeEvent<HTMLInputElement>) {
    const file = e.target.files?.[0];
    e.target.value = "";
    if (!file) return;

    setError(null);
    const clientResult = await validateLogoFile(file);
    if (!clientResult.ok) {
      setError(brandingErrorMessage(clientResult.code));
      return;
    }

    const previewUrl = URL.createObjectURL(file);
    setLocalPreview(previewUrl);
    setUploading(true);
    try {
      const uploaded = await uploadBrandLogo(source, file, tenantId);
      onUploaded(uploaded);
    } catch (err) {
      setError(brandingErrorMessage(brandingErrorCode(err)) || "No se pudo cargar el logotipo.");
    } finally {
      setUploading(false);
    }
  }

  const previewSrc = localPreview ?? currentLogoUrl;

  return (
    <div className="flex flex-col gap-2">
      <span className="text-xs font-semibold" style={{ color: "#162744" }}>
        Logotipo
      </span>
      <div className="flex items-center gap-4">
        <div
          className="flex h-16 w-16 shrink-0 items-center justify-center overflow-hidden rounded-xl border bg-white"
          style={{ borderColor: "#DFE5ED" }}
        >
          {previewSrc ? (
            // eslint-disable-next-line @next/next/no-img-element
            <img src={previewSrc} alt="Logotipo de la marca" className="h-full w-full object-contain" />
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
            style={{ borderColor: "#557EFF", color: "#557EFF" }}
          >
            {uploading ? (
              <>
                <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden /> Cargando…
              </>
            ) : (
              "Cambiar logotipo"
            )}
          </button>
          <p className="text-[11px] opacity-60">{logoDimensionsHint()}</p>
        </div>
      </div>
      <input
        ref={inputRef}
        type="file"
        accept="image/png,image/jpeg,image/webp"
        aria-label="Cargar logotipo de marca"
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
