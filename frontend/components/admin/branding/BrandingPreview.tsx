"use client";

import { useState, type CSSProperties } from "react";
import { Eye, ImageIcon, Mail } from "lucide-react";
import { Modal } from "@/components/atom/Modal";
import type { BrandColors } from "@/lib/api/branding";

export interface BrandingPreviewProps {
  open: boolean;
  onClose: () => void;
  platformName: string | null;
  colors: BrandColors | null;
  logoUrl: string | null;
}

type PreviewTab = "acceso" | "cabecera" | "correo";

const TABS: Array<{ id: PreviewTab; label: string }> = [
  { id: "acceso", label: "Acceso" },
  { id: "cabecera", label: "Cabecera" },
  { id: "correo", label: "Correo" },
];

/**
 * HU #12414 AC4 — previsualización sin publicar. Reproduce las superficies "acceso" y
 * "cabecera" del inventario de #12415 (frontend/docs/brand-color-inventory.md) dentro de un
 * contenedor con variables CSS propias (scoped), NUNCA en `:root` — nada de lo previsualizado
 * se aplica a otros usuarios ni al resto de la app.
 */
export function BrandingPreview({ open, onClose, platformName, colors, logoUrl }: BrandingPreviewProps) {
  const [tab, setTab] = useState<PreviewTab>("acceso");

  const scopedVars: CSSProperties = {
    ["--preview-primary" as string]: colors?.primary ?? "#557EFF",
    ["--preview-secondary" as string]: colors?.secondary ?? "#00DBD5",
    ["--preview-on-primary" as string]: colors?.onPrimary ?? "#FFFFFF",
  };

  const name = platformName?.trim() || "FLIT 2.0";

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
          <div
            className="flex flex-col items-center gap-2 rounded-2xl border p-8 text-center"
            style={{ borderColor: "#DFE5ED" }}
            role="tabpanel"
            aria-label="Previsualización de la muestra de correo"
          >
            <Mail className="h-8 w-8 opacity-40" aria-hidden />
            <p className="text-sm opacity-70">
              La muestra de correo con el tema de marca estará disponible con la HU #12431.
            </p>
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
