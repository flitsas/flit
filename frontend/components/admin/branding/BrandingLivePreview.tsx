"use client";

import { useMemo, type CSSProperties } from "react";
import { Home } from "lucide-react";
import { Icono } from "@flit/ui/Icono";
import { isValidHexColor } from "@/lib/brand/contrast";
import { deriveBrandSurfaces } from "@/lib/brand/derive-tokens";
import type { BrandColors } from "@/lib/api/branding";

export interface BrandingLivePreviewProps {
  colors: BrandColors;
  platformName: string;
  logoUrl: string | null;
}

/**
 * Así se verá la plataforma con estos colores, mientras se eligen: encabezado, menú, botones,
 * enlace y chip pintados con los MISMOS tokens que usa la aplicación (`--color-flit-*`,
 * `--nav-activo`), redefinidos solo dentro de este recuadro.
 *
 * Homologación (HU #14510): el marco (borde, pie y aviso) va con los tokens del tema; el interior es la plataforma del
 * organismo en claro, así que sus superficies blancas y grises fijas son parte del dato, igual que sus colores.
 */
export function BrandingLivePreview({ colors, platformName, logoUrl }: BrandingLivePreviewProps) {
  const scope = useMemo<CSSProperties | null>(() => {
    if (![colors.primary, colors.secondary, colors.onPrimary].every(isValidHexColor)) return null;
    const s = deriveBrandSurfaces(colors.secondary);
    return {
      // Utilidades (`text-flit-primary`): `@theme inline` las compila a `var(--brand-*, …)`.
      ["--brand-primary" as string]: colors.primary,
      ["--brand-secondary" as string]: colors.secondary,
      ["--brand-accent" as string]: s.accent,
      ["--brand-bg" as string]: s.bg,
      ["--brand-tint" as string]: s.tint,
      ["--brand-ink" as string]: s.ink,
      // Estilos en línea (`var(--color-flit-brand)`): esas variables ya vienen resueltas de `:root`.
      ["--color-flit-primary" as string]: colors.primary,
      ["--color-flit-brand" as string]: colors.secondary,
      ["--color-flit-tech" as string]: s.accent,
      ["--color-flit-bg" as string]: s.bg,
      ["--nav-app-bg" as string]: s.tint,
      ["--flit-brand-ink" as string]: s.ink,
      ["--nav-activo" as string]: `linear-gradient(90deg, ${colors.secondary} 0%, ${s.accent} 100%)`,
      ["--preview-on" as string]: colors.onPrimary,
    };
  }, [colors]);

  const name = platformName.trim() || "Tu plataforma";

  if (!scope) {
    return (
      <div className="rounded-2xl border border-border bg-card p-4 text-xs text-muted-foreground">
        Elige colores válidos para ver cómo quedará tu plataforma.
      </div>
    );
  }

  return (
    <figure
      className="overflow-hidden rounded-2xl border border-border shadow-[var(--shadow-card)]"
      style={scope}
      aria-label="Vista previa de la plataforma con tus colores"
      data-testid="branding-live-preview"
    >
      <div className="flex items-center gap-2 border-b bg-white px-3 py-2" style={{ borderColor: "#DFE5ED" }}>
        {logoUrl ? (
          // eslint-disable-next-line @next/next/no-img-element -- logo del borrador, URL dinámica
          <img src={logoUrl} alt="" className="h-6 max-w-[96px] object-contain" />
        ) : (
          <span
            className="grid h-6 w-6 place-items-center rounded-md text-[10px] font-bold"
            style={{ background: "var(--color-flit-brand)", color: "var(--preview-on)" }}
            aria-hidden
          >
            {name.charAt(0).toUpperCase()}
          </span>
        )}
        <span className="truncate text-xs font-bold text-flit-primary">{name}</span>
      </div>

      <div className="space-y-3 p-3" style={{ background: "var(--color-flit-bg)" }}>
        <p className="text-sm font-bold text-flit-primary">Trámites del día</p>
        <div className="rounded-xl border bg-white p-3" style={{ borderColor: "#DFE5ED" }}>
          <p className="text-xs text-[#59677d]">
            Revisa el estado y <span className="font-semibold text-[var(--flit-brand-ink)] underline">consulta el detalle</span>.
          </p>
          <div className="mt-2 flex flex-wrap items-center gap-2">
            <span
              className="rounded-xl px-3 py-1.5 text-[11px] font-semibold"
              style={{ background: "linear-gradient(90deg,var(--brand-ink) 0%,var(--brand-accent) 100%)", color: "var(--preview-on)" }}
            >
              Crear trámite
            </span>
            <span className="rounded-xl border border-flit-brand px-3 py-1.5 text-[11px] font-semibold text-flit-brand">
              Exportar
            </span>
            <span className="rounded-full bg-flit-brand/10 px-2 py-0.5 text-[10px] font-semibold text-[var(--flit-brand-ink)]">
              En trámite
            </span>
          </div>
        </div>

        <div className="mx-auto flex w-fit items-center gap-1 rounded-full border bg-white/95 p-1 shadow" style={{ borderColor: "#DDE5F0" }}>
          <span
            className="flex items-center gap-1 rounded-full px-3 py-1 text-[10px] font-semibold"
            style={{ background: "var(--nav-activo)", color: "var(--preview-on)" }}
          >
            <Home className="h-3 w-3" aria-hidden /> Inicio
          </span>
          <span className="flex items-center gap-1 rounded-full px-3 py-1 text-[10px] font-medium text-[#59677d]">
            <Icono nombre="documento" className="h-3 w-3" /> Trámites
          </span>
          <span className="flex items-center gap-1 rounded-full bg-[var(--nav-app-bg)] px-3 py-1 text-[10px] font-medium text-[#59677d]">
            <Icono nombre="ajustes" className="h-3 w-3" /> Ajustes
          </span>
        </div>
      </div>
      <figcaption className="border-t border-border bg-card px-3 py-1.5 text-[10px] text-muted-foreground">
        Así se verá en toda la plataforma: menú, botones, enlaces y títulos.
      </figcaption>
    </figure>
  );
}
