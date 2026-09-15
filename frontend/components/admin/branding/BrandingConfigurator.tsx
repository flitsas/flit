"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { Eye, Rocket, Save, Undo2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { contrastRatio, isValidHexColor, meetsMinimumContrast } from "@/lib/brand/contrast";
import {
  brandingErrorCode,
  getBranding,
  isBrandingNotFound,
  publishBranding,
  retireBranding,
  upsertBrandingDraft,
  type BrandColors,
  type BrandLogoResponse,
  type BrandingSource,
  type TenantBrandingResponse,
} from "@/lib/api/branding";
import { brandingErrorMessage } from "@/lib/brand/error-messages";
import { BrandingColorPicker } from "./BrandingColorPicker";
import { BrandingLogoUploader } from "./BrandingLogoUploader";
import { BrandingPreview } from "./BrandingPreview";
import { BrandingPublishDialog } from "./BrandingPublishDialog";

export interface BrandingConfiguratorProps {
  /** Fuente de datos: `company` = autogestión de la propia cabeza; `admin` = SuperAdmin sobre `tenantId`. */
  source: BrandingSource;
  /** Requerido cuando `source === "admin"`. */
  tenantId?: string;
}

const DEFAULT_COLORS: BrandColors = { primary: "#557EFF", secondary: "#00DBD5", onPrimary: "#FFFFFF" };

/**
 * HU #12414 — Configurador de marca (AC6: un único componente para la cabeza de red y para el
 * SuperAdmin en la ficha de compañía; `source` decide la base de endpoints y las capacidades —
 * retirar solo existe del lado `admin`, AC1/AC7).
 */
export function BrandingConfigurator({ source, tenantId }: BrandingConfiguratorProps) {
  const [status, setStatus] = useState<UiStatus>("loading");
  const [branding, setBranding] = useState<TenantBrandingResponse | null>(null);
  const [isNew, setIsNew] = useState(false);

  const [draftName, setDraftName] = useState("");
  const [draftColors, setDraftColors] = useState<BrandColors>(DEFAULT_COLORS);
  const [draftLogoId, setDraftLogoId] = useState<string | null>(null);
  const [draftLogoUrl, setDraftLogoUrl] = useState<string | null>(null);

  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [retiring, setRetiring] = useState(false);
  const [confirmingRetire, setConfirmingRetire] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [previewOpen, setPreviewOpen] = useState(false);
  const [formError, setFormError] = useState<string | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      setFormError(null);
      try {
        const data = await getBranding(source, tenantId, signal);
        if (signal?.aborted) return;
        setBranding(data);
        setIsNew(false);
        setDraftName(data.draft.platformName ?? "");
        setDraftColors(data.draft.colors ?? DEFAULT_COLORS);
        setDraftLogoId(data.draft.logoId ?? null);
        setDraftLogoUrl(data.logoUrl ?? null);
        setStatus("ready");
      } catch (err) {
        if (signal?.aborted) return;
        if (isBrandingNotFound(err)) {
          // Aún sin configuración inicial — se muestra el formulario en blanco para crearla
          // (mismo patrón que la ficha de compañía con `isNew`), no un estado vacío que oculte el form.
          setBranding(null);
          setIsNew(true);
          setDraftName("");
          setDraftColors(DEFAULT_COLORS);
          setDraftLogoId(null);
          setDraftLogoUrl(null);
          setStatus("ready");
          return;
        }
        setStatus("error");
      }
    },
    [source, tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const contrastResult = useMemo(() => {
    if (!isValidHexColor(draftColors.primary) || !isValidHexColor(draftColors.onPrimary)) {
      return { ratio: null, ok: false };
    }
    const ratio = contrastRatio(draftColors.primary, draftColors.onPrimary);
    return { ratio, ok: meetsMinimumContrast(ratio) };
  }, [draftColors.primary, draftColors.onPrimary]);

  const missing = useMemo(() => {
    const list: string[] = [];
    if (!draftName.trim()) list.push("nombre de la plataforma");
    if (!draftLogoId) list.push("logotipo");
    if (
      !isValidHexColor(draftColors.primary) ||
      !isValidHexColor(draftColors.secondary) ||
      !isValidHexColor(draftColors.onPrimary)
    ) {
      list.push("colores");
    }
    return list;
  }, [draftName, draftLogoId, draftColors]);

  const canPublish = missing.length === 0 && contrastResult.ok;

  async function persistDraft(): Promise<TenantBrandingResponse> {
    setFormError(null);
    const updated = await upsertBrandingDraft(
      source,
      {
        platformName: draftName.trim() || null,
        colors: draftColors,
        logoId: draftLogoId,
        rowVersion: branding?.rowVersion ?? null,
      },
      tenantId,
    );
    setBranding(updated);
    setIsNew(false);
    setDraftName(updated.draft.platformName ?? "");
    setDraftColors(updated.draft.colors ?? DEFAULT_COLORS);
    setDraftLogoId(updated.draft.logoId ?? null);
    setDraftLogoUrl(updated.logoUrl ?? null);
    return updated;
  }

  async function handleSaveDraft() {
    setSaving(true);
    setSuccessMessage(null);
    try {
      await persistDraft();
      setSuccessMessage("Borrador guardado.");
    } catch (err) {
      setFormError(brandingErrorMessage(brandingErrorCode(err)) || "No se pudo guardar el borrador.");
    } finally {
      setSaving(false);
    }
  }

  async function handleConfirmPublish() {
    setPublishing(true);
    setFormError(null);
    try {
      const saved = await persistDraft();
      const published = await publishBranding(source, saved.rowVersion, tenantId);
      setBranding(published);
      setDraftName(published.draft.platformName ?? "");
      setDraftColors(published.draft.colors ?? DEFAULT_COLORS);
      setDraftLogoId(published.draft.logoId ?? null);
      setDraftLogoUrl(published.logoUrl ?? null);
      setPublishOpen(false);
      setSuccessMessage("Identidad de marca publicada.");
    } catch (err) {
      setFormError(brandingErrorMessage(brandingErrorCode(err)) || "No se pudo publicar la marca.");
    } finally {
      setPublishing(false);
    }
  }

  async function handleRetire() {
    if (source !== "admin" || !tenantId) return;
    setRetiring(true);
    setFormError(null);
    try {
      const updated = await retireBranding(tenantId);
      setBranding(updated);
      setConfirmingRetire(false);
      setSuccessMessage("Marca retirada. La red vuelve a la identidad FLIT.");
    } catch (err) {
      setFormError(brandingErrorMessage(brandingErrorCode(err)) || "No se pudo retirar la marca.");
    } finally {
      setRetiring(false);
    }
  }

  function handleLogoUploaded(logo: BrandLogoResponse) {
    setDraftLogoId(logo.logoId);
    setDraftLogoUrl(logo.logoUrl);
    setSuccessMessage(null);
  }

  return (
    <section
      className="rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60"
      aria-labelledby="branding-configurator-title"
    >
      <div className="mb-3 flex flex-wrap items-start justify-between gap-3">
        <div>
          <h2 id="branding-configurator-title" className="text-sm font-bold" style={{ color: "#162744" }}>
            Identidad de marca
          </h2>
          <p className="text-[11px] opacity-60">
            Logotipo, colores y nombre de la plataforma para tu red. Los cambios quedan en borrador
            hasta que publicas.
          </p>
        </div>
        <button
          type="button"
          onClick={() => setPreviewOpen(true)}
          disabled={status !== "ready"}
          className="inline-flex items-center gap-1.5 rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
          style={{ borderColor: "#557EFF", color: "#557EFF" }}
        >
          <Eye className="h-3.5 w-3.5" aria-hidden /> Previsualizar
        </button>
      </div>

      <UiStateBoundary status={status} onRetry={() => void load()} errorMessage="No se pudo cargar la identidad de marca.">
        {isNew && (
          <div
            className="mb-3 rounded-xl border px-3 py-2 text-xs"
            style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)", color: "#8a6000" }}
            role="status"
          >
            Esta red aún no tiene identidad de marca configurada. Define los valores y guarda el
            borrador para empezar.
          </div>
        )}

        <div className="flex flex-col gap-5">
          <div className="flex flex-col gap-1">
            <label htmlFor="branding-platform-name" className="text-xs font-semibold" style={{ color: "#162744" }}>
              Nombre de la plataforma
            </label>
            <input
              id="branding-platform-name"
              type="text"
              value={draftName}
              onChange={(e) => setDraftName(e.target.value)}
              minLength={2}
              maxLength={40}
              placeholder="Nombre visible en el acceso y en el correo"
              className="w-full max-w-md rounded-lg border px-3 py-2 text-sm"
              style={{ borderColor: "#DFE5ED" }}
            />
          </div>

          <BrandingLogoUploader
            source={source}
            tenantId={tenantId}
            currentLogoUrl={draftLogoUrl}
            onUploaded={handleLogoUploaded}
          />

          <BrandingColorPicker colors={draftColors} onChange={setDraftColors} />

          {branding && (
            <div className="rounded-xl border px-3 py-2 text-xs" style={{ borderColor: "#DFE5ED" }}>
              {branding.publishedAt ? (
                <p style={{ color: "#162744" }}>
                  Publicada el {formatDate(branding.publishedAt)}
                  {branding.publishedBy?.userId ? ` por ${branding.publishedBy.userId}` : ""}.
                </p>
              ) : (
                <p className="opacity-70">Aún no se ha publicado ninguna versión.</p>
              )}
              {branding.hasUnpublishedChanges && (
                <p className="mt-1 font-semibold" style={{ color: "#F9AC00" }}>
                  Hay cambios en el borrador sin publicar.
                </p>
              )}
            </div>
          )}

          {missing.length > 0 && (
            <p className="text-[11px] opacity-70">
              Para publicar falta: {missing.join(", ")}.
            </p>
          )}

          {formError && (
            <p role="alert" className="text-xs" style={{ color: "#FF4E00" }}>
              {formError}
            </p>
          )}
          {successMessage && (
            <p role="status" className="text-xs" style={{ color: "#8CC63F" }}>
              {successMessage}
            </p>
          )}

          <div className="flex flex-wrap gap-3">
            <button
              type="button"
              onClick={() => void handleSaveDraft()}
              disabled={saving || publishing}
              className="inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
              style={{ borderColor: "#557EFF", color: "#557EFF" }}
            >
              <Save className="h-3.5 w-3.5" aria-hidden /> {saving ? "Guardando…" : "Guardar borrador"}
            </button>
            <button
              type="button"
              onClick={() => setPublishOpen(true)}
              disabled={!canPublish || saving || publishing}
              aria-disabled={!canPublish}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              <Rocket className="h-3.5 w-3.5" aria-hidden /> Publicar
            </button>

            {source === "admin" && branding?.published && (
              <button
                type="button"
                onClick={() => setConfirmingRetire(true)}
                disabled={retiring}
                className="ml-auto inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
                style={{ borderColor: "#FF4E00", color: "#FF4E00" }}
              >
                <Undo2 className="h-3.5 w-3.5" aria-hidden /> Retirar marca publicada
              </button>
            )}
          </div>

          {confirmingRetire && (
            <div className="rounded-xl border p-3 text-xs" style={{ borderColor: "#FF4E00" }} role="alertdialog" aria-label="Confirmar retiro de marca">
              <p style={{ color: "#162744" }}>
                ¿Retirar la marca publicada? La red volverá a mostrar la identidad FLIT hasta que
                publiques de nuevo. El dato queda conservado en el historial.
              </p>
              <div className="mt-2 flex gap-2">
                <button
                  type="button"
                  onClick={() => setConfirmingRetire(false)}
                  disabled={retiring}
                  className="rounded-lg border px-3 py-1.5 font-medium disabled:opacity-60"
                >
                  Cancelar
                </button>
                <button
                  type="button"
                  onClick={() => void handleRetire()}
                  disabled={retiring}
                  className="rounded-lg px-3 py-1.5 font-semibold text-white disabled:opacity-60"
                  style={{ background: "#FF4E00" }}
                >
                  {retiring ? "Retirando…" : "Retirar"}
                </button>
              </div>
            </div>
          )}
        </div>
      </UiStateBoundary>

      <BrandingPublishDialog
        open={publishOpen}
        busy={publishing}
        onClose={() => setPublishOpen(false)}
        onConfirm={() => void handleConfirmPublish()}
      />

      <BrandingPreview
        open={previewOpen}
        onClose={() => setPreviewOpen(false)}
        platformName={draftName}
        colors={draftColors}
        logoUrl={draftLogoUrl}
      />
    </section>
  );
}

function formatDate(iso: string): string {
  const parsed = new Date(iso);
  if (Number.isNaN(parsed.getTime())) return iso;
  return parsed.toLocaleDateString("es-CO", { year: "numeric", month: "2-digit", day: "2-digit" });
}
