"use client";

import { useCallback, useEffect, useState, type CSSProperties } from "react";
import { Eye, ImageIcon, Mail } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import type { BrandColors, BrandingSource } from "@/lib/api/branding";
import {
  getCompanyBrandingEmailSample,
  type CompanyBrandingEmailSampleResponse,
  type CompanyBrandingEmailSampleSource,
} from "@/lib/api/branding-client";
import { getNotificationSample, type NotificationSample } from "@/lib/api/admin-plataforma-notificaciones";
import { ApiError } from "@/lib/api/types";

export interface BrandingPreviewProps {
  open: boolean;
  onClose: () => void;
  platformName: string | null;
  colors: BrandColors | null;
  logoUrl: string | null;
  /** HU #12431 AC1/AC3 — decide cómo se obtiene la muestra de correo real de la pestaña "Correo". */
  source: BrandingSource;
  /** Requerido cuando `source === "admin"` (ficha de compañía del SuperAdmin). */
  tenantId?: string;
}

type PreviewTab = "acceso" | "cabecera" | "correo";

const TABS: Array<{ id: PreviewTab; label: string }> = [
  { id: "acceso", label: "Acceso" },
  { id: "cabecera", label: "Cabecera" },
  { id: "correo", label: "Correo" },
];

const DEFAULT_TEMPLATE_ID = "tramites.aprobado";

/**
 * HU #12414 AC4 — previsualización sin publicar. Reproduce las superficies "acceso" y
 * "cabecera" del inventario de #12415 (frontend/docs/brand-color-inventory.md) dentro de un
 * contenedor con variables CSS propias (scoped), NUNCA en `:root` — nada de lo previsualizado
 * se aplica a otros usuarios ni al resto de la app.
 *
 * HU #12431 AC1 — la pestaña "Correo" deja de ser un mock estático: renderiza la muestra real
 * de un correo representativo con el tema de la red (borrador o publicada, `source="company"`)
 * dentro de un `iframe` aislado, igual que la consola de plantillas del SuperAdmin.
 */
export function BrandingPreview({
  open,
  onClose,
  platformName,
  colors,
  logoUrl,
  source,
  tenantId,
}: BrandingPreviewProps) {
  const [tab, setTab] = useState<PreviewTab>("acceso");

  const scopedVars: CSSProperties = {
    ["--preview-primary" as string]: colors?.primary ?? "#557EFF",
    ["--preview-secondary" as string]: colors?.secondary ?? "#00DBD5",
    ["--preview-on-primary" as string]: colors?.onPrimary ?? "#FFFFFF",
  };

  const name = platformName?.trim() || "FLIT 2.0";

  useEffect(() => {
    if (!open) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- reinicia la pestaña al reabrir
      setTab("acceso");
    }
  }, [open]);

  return (
    <Modal open={open} onClose={onClose} size="lg" icon={Eye} title="Previsualización de la marca">
      <div className="flex gap-2 border-b pb-2" style={{ borderColor: "#DFE5ED" }} role="tablist" aria-label="Superficies de previsualización">
        {TABS.map((t) => (
          <button
            key={t.id}
            type="button"
            role="tab"
            aria-selected={tab === t.id}
            onClick={() => setTab(t.id)}
            className="rounded-lg px-3 py-1.5 text-xs font-semibold"
            style={
              tab === t.id
                ? { background: "#557EFF", color: "#FFFFFF" }
                : { background: "transparent", color: "#162744" }
            }
          >
            {t.label}
          </button>
        ))}
      </div>

      <div className="mt-4" style={scopedVars} data-testid="branding-preview-scope">
        {tab === "acceso" && (
          <div
            className="flex flex-col items-center gap-3 rounded-2xl p-8 text-center"
            style={{ background: "var(--preview-primary)" }}
            role="tabpanel"
            aria-label="Previsualización de la pantalla de acceso"
          >
            <LogoOrPlaceholder logoUrl={logoUrl} name={name} />
            <p className="text-sm font-semibold" style={{ color: "var(--preview-on-primary)" }}>
              {name}
            </p>
            <button
              type="button"
              tabIndex={-1}
              className="rounded-full px-6 py-2 text-xs font-semibold"
              style={{ background: "var(--preview-secondary)", color: "var(--preview-on-primary)" }}
            >
              Iniciar sesión
            </button>
          </div>
        )}

        {tab === "cabecera" && (
          <div
            className="flex items-center gap-3 rounded-2xl p-4"
            style={{ background: "var(--preview-primary)" }}
            role="tabpanel"
            aria-label="Previsualización de la cabecera de la aplicación"
          >
            <LogoOrPlaceholder logoUrl={logoUrl} name={name} small />
            <span className="text-sm font-bold" style={{ color: "var(--preview-on-primary)" }}>
              {name}
            </span>
            <span
              className="ml-auto rounded-full px-3 py-1 text-[10px] font-semibold"
              style={{ background: "var(--preview-secondary)", color: "var(--preview-on-primary)" }}
            >
              Menú
            </span>
          </div>
        )}

        {tab === "correo" && (
          <div role="tabpanel" aria-label="Previsualización de la muestra de correo">
            <EmailPreviewPanel open={open} active={tab === "correo"} source={source} tenantId={tenantId} />
          </div>
        )}
      </div>

      <p className="mt-3 text-[11px] opacity-60">
        Esta previsualización usa el borrador actual. Nada de lo mostrado aquí se aplica a otros
        usuarios hasta que publiques.
      </p>
    </Modal>
  );
}

interface EmailPreviewPanelProps {
  open: boolean;
  active: boolean;
  source: BrandingSource;
  tenantId?: string;
}

type EmailSample = CompanyBrandingEmailSampleResponse | NotificationSample;

/**
 * `source="company"` (autogestión de la cabeza): conmutador Borrador/Publicada contra
 * `GET /company/branding/email-sample` (HU #12431 AC1). `source="admin"` (ficha de compañía del
 * SuperAdmin): una sola muestra —la publicada— resuelta vía
 * `GET /admin/plataforma/notificaciones/plantillas/{id}/muestra?tenantId=` (mismo endpoint que la
 * consola de plantillas), sin conmutador porque esa ruta no expone el borrador de otra cabeza.
 */
function EmailPreviewPanel({ open, active, source, tenantId }: EmailPreviewPanelProps) {
  const [emailSource, setEmailSource] = useState<CompanyBrandingEmailSampleSource>("draft");
  const [status, setStatus] = useState<UiStatus>("loading");
  const [sample, setSample] = useState<EmailSample | null>(null);

  const load = useCallback(() => {
    setStatus("loading");
    setSample(null);

    if (source === "admin") {
      if (!tenantId) {
        setStatus("error");
        return;
      }
      getNotificationSample(DEFAULT_TEMPLATE_ID, { tenantId })
        .then((data) => {
          setSample(data);
          setStatus("ready");
        })
        .catch(() => setStatus("error"));
      return;
    }

    getCompanyBrandingEmailSample({ templateId: DEFAULT_TEMPLATE_ID, source: emailSource })
      .then((data) => {
        if (emailSource === "published" && data.theme?.kind === "flit") {
          // AC1 — "publicada" sin marca publicada todavía: estado vacío, no la muestra FLIT.
          setStatus("empty");
          return;
        }
        setSample(data);
        setStatus("ready");
      })
      .catch((err) => {
        if (emailSource === "published" && err instanceof ApiError && err.status === 404) {
          setStatus("empty");
          return;
        }
        setStatus("error");
      });
  }, [source, tenantId, emailSource]);

  useEffect(() => {
    if (!open || !active) return;
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga de la muestra al activar la pestaña
    load();
  }, [open, active, load]);

  const theme = sample?.theme;

  return (
    <div className="flex flex-col gap-3">
      {source === "company" && (
        <div
          className="inline-flex w-fit gap-1 rounded-full border p-0.5"
          style={{ borderColor: "#DFE5ED" }}
          role="group"
          aria-label="Fuente de la muestra de correo"
        >
          {(["draft", "published"] as const).map((value) => (
            <button
              key={value}
              type="button"
              aria-pressed={emailSource === value}
              onClick={() => setEmailSource(value)}
              className="rounded-full px-3 py-1 text-[11px] font-semibold"
              style={
                emailSource === value
                  ? { background: "#557EFF", color: "#FFFFFF" }
                  : { background: "transparent", color: "#162744" }
              }
            >
              {value === "draft" ? "Borrador" : "Publicada"}
            </button>
          ))}
        </div>
      )}

      <UiStateBoundary
        status={status}
        onRetry={load}
        errorMessage="No se pudo cargar la muestra de correo con el tema de la red."
        emptyMessage="Aún no hay marca publicada. Publica el borrador para ver la muestra con esa versión."
        skeletonRows={3}
      >
        {sample ? (
          <div className="flex flex-col gap-2">
            <p className="text-xs opacity-70">
              <span className="font-semibold" style={{ color: "#162744" }}>
                Asunto:{" "}
              </span>
              {sample.subject}
            </p>
            {theme && (
              <p className="flex items-center gap-1.5 text-[11px] opacity-70" data-testid="branding-preview-email-theme">
                <Mail className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
                {theme.platformName}
                {theme.version !== undefined ? ` · v${theme.version}` : ""}
                {theme.senderName ? ` · ${theme.senderName}` : ""}
              </p>
            )}
            {theme?.kind === "draft-partial" && (
              <p role="status" className="text-[11px] font-semibold" style={{ color: "#F9AC00" }}>
                Borrador incompleto: se completa con la identidad de FLIT.
              </p>
            )}
            <p role="note" className="text-[11px] opacity-60">
              Esta muestra es una vista aislada. No se envía ningún correo al mostrarla.
            </p>
            <iframe
              title="Vista previa aislada de la muestra de correo con el tema de la red"
              srcDoc={sample.html}
              sandbox=""
              className="h-[420px] w-full rounded-xl border"
              style={{ borderColor: "#DFE5ED" }}
              data-testid="branding-preview-email-iframe"
            />
          </div>
        ) : null}
      </UiStateBoundary>
    </div>
  );
}

function LogoOrPlaceholder({ logoUrl, name, small = false }: { logoUrl: string | null; name: string; small?: boolean }) {
  const size = small ? "h-8 w-8" : "h-14 w-14";
  if (logoUrl) {
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={logoUrl} alt={`Logotipo de ${name}`} className={`${size} rounded-lg object-contain bg-white p-1`} />;
  }
  return (
    <span className={`flex ${size} items-center justify-center rounded-lg bg-white/20`} aria-hidden>
      <ImageIcon className="h-1/2 w-1/2 text-white" />
    </span>
  );
}
