"use client";

import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { Rocket, Save } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { BrandingColorPicker } from "@/components/admin/branding/BrandingColorPicker";
import { BrandingLogoUploader } from "@/components/admin/branding/BrandingLogoUploader";
import { BrandingPublishDialog } from "@/components/admin/branding/BrandingPublishDialog";
import { BRAND_COLOR_PRESETS } from "@/components/admin/branding/brand-color-presets";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Modal } from "@/components/atom/Modal";
import {
  portalMarcaErrorCode,
  portalMarcaErrorMessage,
  type PortalMarca,
} from "@/lib/api/admin-ot-portal-marca";
import type { BrandColors } from "@/lib/api/branding";
import { isValidHexColor } from "@/lib/brand/contrast";
import {
  ajustarColoresPortal,
  coloresPortalLegibles,
  MENSAJE_COLORES_ILEGIBLES,
  parTitulos,
} from "@/lib/brand/portal-contrast";
import { formatFechaHora } from "@/lib/format/date";
import {
  PORTAL_IMAGE_ACCEPT,
  portalImageHint,
  validatePortalImage,
} from "@/lib/brand/validate-portal-image";
import { BannersSection } from "./BannersSection";
import { CarruselSection } from "./CarruselSection";
import { clienteMarcaOt, type ClienteMarcaPortal } from "./cliente-marca";
import { PortalPnPreview } from "./PortalPnPreview";
import {
  TEXTOS_DISENO_OT,
  TEXTOS_VISTA_PREVIA_OT,
  textoSegunImagenes,
  type TextosDiseno,
  type TextosVistaPrevia,
} from "./textos-diseno";
import { useAuthedImage } from "./useAuthedImage";

const DEFAULT_COLORS: BrandColors = {
  primary: BRAND_COLOR_PRESETS[0].primary,
  secondary: BRAND_COLOR_PRESETS[0].secondary,
  onPrimary: "#FFFFFF",
};

const REGLAS_COLOR = {
  readable: coloresPortalLegibles,
  fix: ajustarColoresPortal,
  message: MENSAJE_COLORES_ILEGIBLES,
  extraPairs: (colors: BrandColors) => [parTitulos(colors)],
};

type Status = "loading" | "error" | "ready";

export interface PortalPnDesignSectionProps {
  /** Organismo al que pertenece la marca. Sin `cliente`, la sección trabaja con la marca de ese organismo. */
  transitOfficeId?: string;
  /** API de marca propia (p. ej. la de la web de persona). Debe ser estable entre renders. */
  cliente?: ClienteMarcaPortal;
  /** Textos propios. Sin ellos, los del portal del organismo. */
  textos?: TextosDiseno;
  textosVistaPrevia?: TextosVistaPrevia;
  /** Ocupa el lugar de los banners cuando la marca no los lleva (el cliente no trae `banners`): p. ej. un acceso a Contenido. */
  accesoBanner?: ReactNode;
  /**
   * Espacio para la sección de dominio del portal (llega con otra HU). Se pinta debajo de la marca, con su propio
   * encabezado; sin él, la vista de Diseño es solo la marca.
   */
  domain?: ReactNode;
}

/**
 * HU #14377 (Feature #14374) — Diseño del portal Persona Natural del organismo: nombre, colores, logo y banners (hasta 6), con
 * borrador, vista previa, publicar y retirar. Compone las piezas de marca blanca (selector de colores, carga de imagen,
 * confirmación de publicar) con el cliente de `/api/v1/admin/ot/portal-marca`.
 */
export function PortalPnDesignSection({
  transitOfficeId,
  cliente: clienteInyectado,
  textos = TEXTOS_DISENO_OT,
  textosVistaPrevia = TEXTOS_VISTA_PREVIA_OT,
  accesoBanner,
  domain,
}: PortalPnDesignSectionProps) {
  const cliente = useMemo<ClienteMarcaPortal>(
    () => clienteInyectado ?? clienteMarcaOt({ transitOfficeId }),
    [clienteInyectado, transitOfficeId],
  );
  const fieldCopy = useMemo(
    () => ({
      primary: { label: "Color principal", hint: textos.principalAyuda },
      secondary: { label: "Color secundario", hint: textos.secundarioAyuda },
    }),
    [textos],
  );

  const [status, setStatus] = useState<Status>("loading");
  const [loadError, setLoadError] = useState("");
  const [marca, setMarca] = useState<PortalMarca | null>(null);
  const [draftName, setDraftName] = useState("");
  const [draftColors, setDraftColors] = useState<BrandColors>(DEFAULT_COLORS);

  const [saving, setSaving] = useState(false);
  const [publishing, setPublishing] = useState(false);
  const [retiring, setRetiring] = useState(false);
  const [removingLogo, setRemovingLogo] = useState(false);
  const [publishOpen, setPublishOpen] = useState(false);
  const [retireOpen, setRetireOpen] = useState(false);
  const [formError, setFormError] = useState<{ message: string; conflict: boolean } | null>(null);
  const [successMessage, setSuccessMessage] = useState<string | null>(null);

  const [carouselBusy, setCarouselBusy] = useState(false);
  const [bannersBusy, setBannersBusy] = useState(false);
  const carouselFirstSrc = useAuthedImage(
    cliente.carrusel ? (marca?.carousel?.[0]?.imageUrl ?? null) : null,
    cliente.imagen,
  );
  const logoSrc = useAuthedImage(marca?.logoUrl ?? null, cliente.imagen);
  const bannerSrc = useAuthedImage(
    cliente.banners ? (marca?.banners?.[0]?.imageUrl ?? null) : null,
    cliente.imagen,
  );

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      setFormError(null);
      try {
        const data = await cliente.obtener(signal);
        if (signal?.aborted) return;
        setMarca(data);
        setDraftName(data.draft.platformName ?? "");
        setDraftColors(data.draft.colors ?? DEFAULT_COLORS);
        setStatus("ready");
      } catch (err) {
        if (signal?.aborted) return;
        setLoadError(portalMarcaErrorMessage(err, textos.errorCarga));
        setStatus("error");
      }
    },
    [cliente, textos.errorCarga],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const colorsValid = [draftColors.primary, draftColors.secondary, draftColors.onPrimary].every(
    isValidHexColor,
  );
  const contrastOk = useMemo(() => coloresPortalLegibles(draftColors), [draftColors]);

  const missing = useMemo(() => {
    const list: string[] = [];
    if (textos.nombreObligatorio && !draftName.trim()) list.push("nombre");
    if (!colorsValid) list.push("colores");
    return list;
  }, [draftName, colorsValid, textos.nombreObligatorio]);

  const canPublish = missing.length === 0 && contrastOk;
  const busy = saving || publishing || retiring || removingLogo || carouselBusy || bannersBusy;

  function fail(err: unknown, fallback: string) {
    setFormError({
      message: portalMarcaErrorMessage(err, fallback),
      conflict: portalMarcaErrorCode(err) === "PORTAL_MARCA_CONFLICTO",
    });
  }

  /** Mismas reglas que el servidor: se avisa con el motivo y no se envía nada. */
  function blockedReason(): string | null {
    if (!colorsValid) return "Escribe los colores con el formato #RRGGBB, por ejemplo #2563EB.";
    if (!contrastOk) return textos.contrasteMensaje;
    return null;
  }

  async function persistDraft(): Promise<PortalMarca> {
    const saved = await cliente.guardarBorrador({
      platformName: draftName.trim() || null,
      colors: draftColors,
      rowVersion: marca?.rowVersion ?? 0,
    });
    setMarca(saved);
    return saved;
  }

  async function handleSaveDraft() {
    setSuccessMessage(null);
    const blocked = blockedReason();
    if (blocked) {
      setFormError({ message: blocked, conflict: false });
      return;
    }
    setFormError(null);
    setSaving(true);
    try {
      await persistDraft();
      setSuccessMessage(textos.guardado);
    } catch (err) {
      fail(err, "No se pudo guardar el borrador.");
    } finally {
      setSaving(false);
    }
  }

  async function handleConfirmPublish() {
    setSuccessMessage(null);
    setFormError(null);
    setPublishing(true);
    try {
      const saved = await persistDraft();
      const published = await cliente.publicar(saved.rowVersion);
      setMarca(published);
      setPublishOpen(false);
      setSuccessMessage(textos.publicado);
    } catch (err) {
      setPublishOpen(false);
      fail(err, "No se pudo publicar la marca.");
    } finally {
      setPublishing(false);
    }
  }

  async function handleRetire() {
    if (!marca) return;
    setSuccessMessage(null);
    setFormError(null);
    setRetiring(true);
    try {
      const updated = await cliente.retirar(marca.rowVersion);
      setMarca(updated);
      setRetireOpen(false);
      setSuccessMessage(textos.retirado);
    } catch (err) {
      setRetireOpen(false);
      fail(err, "No se pudo retirar la marca.");
    } finally {
      setRetiring(false);
    }
  }

  async function handleRemoveLogo() {
    setSuccessMessage(null);
    setFormError(null);
    setRemovingLogo(true);
    try {
      setMarca(await cliente.quitarLogo());
      setSuccessMessage(textos.logoQuitado);
    } catch (err) {
      fail(err, "No se pudo quitar el logo.");
    } finally {
      setRemovingLogo(false);
    }
  }

  // El carrusel y los banners se guardan en el servidor al instante (como el logo): solo se toma la versión nueva del registro.
  function handleListChanged(updated: PortalMarca, message: string) {
    setMarca(updated);
    setFormError(null);
    setSuccessMessage(message);
  }

  // La imagen subida ya es parte del borrador en el servidor: solo se toma la versión nueva del registro, sin pisar
  // el nombre y los colores que se estén editando.
  function handleImageUploaded(updated: PortalMarca) {
    setMarca(updated);
    setFormError(null);
    setSuccessMessage("Imagen cargada en el borrador.");
  }

  return (
    <div className="flex flex-col gap-6">
      <section
        className="rounded-2xl border bg-card p-4"
        aria-labelledby="portal-pn-design-title"
      >
        <div className="mb-3">
          <h2
            id="portal-pn-design-title"
            className="text-sm font-bold"
            style={{ color: "var(--color-flit-primary)" }}
          >
            {textos.titulo}
          </h2>
          <p className="text-xs opacity-70">{textos.descripcion}</p>
        </div>

        {status === "loading" && <CarLoaderModal label="Armando el administrador…" />}

        <UiStateBoundary
          status={status === "loading" ? "ready" : status}
          onRetry={() => void load()}
          errorMessage={loadError}
        >
          {status === "ready" && marca && (
            <div className="flex flex-col gap-5">
              {textos.nombreEtiqueta && (
                <div className="flex flex-col gap-1">
                  <label
                    htmlFor="portal-pn-name"
                    className="text-xs font-semibold"
                    style={{ color: "var(--color-flit-primary)" }}
                  >
                    {textos.nombreEtiqueta}
                  </label>
                  <input
                    id="portal-pn-name"
                    type="text"
                    value={draftName}
                    onChange={(e) => setDraftName(e.target.value)}
                    maxLength={40}
                    placeholder={textos.nombrePlaceholder}
                    className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring w-full max-w-md rounded-lg border px-3 py-2 text-sm"
                    style={{ borderColor: "var(--border)" }}
                  />
                </div>
              )}

              <div className="grid gap-4 lg:grid-cols-[minmax(0,1fr)_minmax(260px,340px)] lg:items-start">
                <div className="flex flex-col gap-5">
                  <BrandingColorPicker
                    colors={draftColors}
                    onChange={setDraftColors}
                    fieldCopy={fieldCopy}
                    primaryFirst
                    rules={REGLAS_COLOR}
                    disabled={busy}
                  />

                  <ImageField
                    label="Logo"
                    textos={textos}
                    currentSrc={logoSrc}
                    hasImage={Boolean(marca.logoUrl)}
                    disabled={busy}
                    removing={removingLogo}
                    onUpload={(file) => cliente.subirLogo(file)}
                    onUploaded={handleImageUploaded}
                    onRemove={() => void handleRemoveLogo()}
                  />
                  {cliente.banners ? (
                    <BannersSection
                      items={marca.banners ?? []}
                      api={cliente.banners}
                      imagen={cliente.imagen}
                      readOnly={marca.canEdit === false}
                      disabled={busy}
                      onChange={handleListChanged}
                      onError={fail}
                      onBusyChange={setBannersBusy}
                    />
                  ) : (
                    accesoBanner
                  )}
                  {cliente.carrusel && (
                    <CarruselSection
                      items={marca.carousel ?? []}
                      api={cliente.carrusel}
                      imagen={cliente.imagen}
                      readOnly={marca.canEdit === false}
                      disabled={busy}
                      onChange={handleListChanged}
                      onError={fail}
                      onBusyChange={setCarouselBusy}
                    />
                  )}
                </div>
                <div className="lg:sticky lg:top-4">
                  <PortalPnPreview
                    colors={draftColors}
                    platformName={draftName}
                    logoSrc={logoSrc}
                    bannerSrc={bannerSrc}
                    textos={textosVistaPrevia}
                    bannerCount={cliente.banners ? (marca.banners?.length ?? 0) : undefined}
                    tieneLogo={Boolean(marca.logoUrl)}
                    carouselSrc={carouselFirstSrc}
                    carouselCount={cliente.carrusel ? (marca.carousel?.length ?? 0) : undefined}
                  />
                </div>
              </div>

              <div
                className="rounded-xl border px-3 py-2 text-xs"
                style={{ borderColor: "var(--border)" }}
              >
                {marca.published && marca.publishedAt ? (
                  <p style={{ color: "var(--color-flit-primary)" }}>
                    Publicada (versión {marca.publishedVersion}) el{" "}
                    {formatFechaHora(marca.publishedAt)}.
                  </p>
                ) : (
                  <p className="opacity-70">{textos.sinMarca}</p>
                )}
                {marca.hasUnpublishedChanges && (
                  <p className="mt-1 font-semibold" style={{ color: "var(--badge-warning-fg)" }}>
                    Hay cambios en el borrador sin publicar.
                  </p>
                )}
              </div>

              {missing.length > 0 && (
                <p className="text-xs opacity-70">Para publicar falta: {missing.join(", ")}.</p>
              )}

              {formError && (
                <div
                  role="alert"
                  className="flex flex-wrap items-center gap-3 text-xs"
                  style={{ color: "var(--destructive)" }}
                >
                  <span>{formError.message}</span>
                  {formError.conflict && (
                    <button
                      type="button"
                      onClick={() => void load()}
                      className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring rounded-lg border px-3 py-1 font-semibold"
                      style={{ borderColor: "var(--destructive)" }}
                    >
                      Recargar la marca
                    </button>
                  )}
                </div>
              )}
              {successMessage && (
                <p
                  role="status"
                  className="flex items-center gap-1.5 text-xs font-medium"
                  style={{ color: "var(--flit-success-ink)" }}
                >
                  <Icono nombre="check" className="h-4 w-4 shrink-0" style={{ color: "var(--flit-success-ink)" }} />
                  {successMessage}
                </p>
              )}

              <div className="flex flex-wrap gap-3">
                <button
                  type="button"
                  onClick={() => void handleSaveDraft()}
                  disabled={busy}
                  className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                  style={{ background: "var(--primary)", color: "var(--primary-foreground)" }}
                >
                  <Save className="h-3.5 w-3.5" aria-hidden />{" "}
                  {saving ? "Guardando…" : "Guardar borrador"}
                </button>
                <button
                  type="button"
                  onClick={() => setPublishOpen(true)}
                  disabled={!canPublish || busy}
                  className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring inline-flex items-center gap-1.5 rounded-full px-4 py-2 text-xs font-semibold text-white disabled:cursor-not-allowed disabled:opacity-50"
                  style={{ background: "var(--gradient-action)" }}
                >
                  <Rocket className="h-3.5 w-3.5" aria-hidden /> Publicar
                </button>
                {marca.published && (
                  <button
                    type="button"
                    onClick={() => setRetireOpen(true)}
                    disabled={busy}
                    className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring ml-auto inline-flex items-center gap-1.5 rounded-full border px-4 py-2 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
                    style={{ borderColor: "var(--destructive)", color: "var(--destructive)" }}
                  >
                    <Icono nombre="devolucion" className="h-3.5 w-3.5" /> Retirar marca publicada
                  </button>
                )}
              </div>
            </div>
          )}
        </UiStateBoundary>

        <BrandingPublishDialog
          open={publishOpen}
          busy={publishing}
          onClose={() => setPublishOpen(false)}
          onConfirm={() => void handleConfirmPublish()}
          title={textos.publicarTitulo}
          description={textos.publicarDescripcion}
          note={textoSegunImagenes(textos.publicarNota, {
            tieneLogo: Boolean(marca?.logoUrl),
            tieneBanners: (marca?.banners?.length ?? 0) > 0,
          })}
        />

        <Modal
          open={retireOpen}
          onClose={() => setRetireOpen(false)}
          busy={retiring}
          size="sm"
          icono="devolucion"
          title="Retirar la marca publicada"
        >
          <p className="text-sm opacity-80">{textos.retirarDescripcion}</p>
          <div className="mt-5 flex gap-3">
            <button
              type="button"
              onClick={() => setRetireOpen(false)}
              disabled={retiring}
              className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring flex-1 rounded-full border py-2.5 text-sm font-medium disabled:opacity-60"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void handleRetire()}
              disabled={retiring}
              className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring flex-1 rounded-full py-2.5 text-sm font-semibold text-white disabled:opacity-60"
              style={{ background: "var(--destructive)", color: "var(--destructive-foreground)" }}
            >
              {retiring ? "Retirando…" : "Retirar"}
            </button>
          </div>
        </Modal>
      </section>

      {domain}
    </div>
  );
}

interface ImageFieldProps {
  label: string;
  textos: TextosDiseno;
  currentSrc: string | null;
  hasImage: boolean;
  disabled: boolean;
  removing: boolean;
  onUpload: (file: File) => Promise<PortalMarca>;
  onUploaded: (marca: PortalMarca) => void;
  onRemove: () => void;
}

/** Carga (pieza de marca blanca) y quitar del logo del portal. */
function ImageField({
  label,
  textos,
  currentSrc,
  hasImage,
  disabled,
  removing,
  onUpload,
  onUploaded,
  onRemove,
}: ImageFieldProps) {
  return (
    <div className="flex flex-col gap-2">
      <BrandingLogoUploader<PortalMarca>
        key={hasImage ? "con-imagen" : "sin-imagen"}
        currentLogoUrl={currentSrc}
        disabled={disabled}
        upload={onUpload}
        onUploaded={onUploaded}
        validate={async (file) => validatePortalImage(file, "logo")}
        label={label}
        buttonLabel={hasImage ? "Reemplazar logo" : "Seleccionar logo"}
        dropzone
        hint={`${portalImageHint("logo")} · ${textos.logoSinImagen}`}
        accept={PORTAL_IMAGE_ACCEPT}
        inputAriaLabel={textos.logoInput}
        imageAlt={textos.logoAlt}
        previewClassName="h-16 w-16"
      />
      {hasImage && (
        <button
          type="button"
          onClick={onRemove}
          disabled={disabled}
          className="focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring inline-flex w-fit items-center gap-1.5 rounded-full border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50"
          style={{ borderColor: "var(--destructive)", color: "var(--destructive)" }}
        >
          <Icono nombre="papelera" className="h-3.5 w-3.5" />
          {removing ? "Quitando…" : "Quitar logo"}
        </button>
      )}
    </div>
  );
}
