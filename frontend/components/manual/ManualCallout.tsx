import type { ManualCallout } from "@/lib/manual/types";
import type { ComponentType } from "react";
import { iconoComo, type IconoComoProps } from "@flit/ui/iconoComo";
import { avisoComo } from "@flit/ui/IconoAviso";

const ICONO_DESTELLO = iconoComo("destello");

const styles: Record<
  ManualCallout["variant"],
  { border: string; bg: string; icon: ComponentType<IconoComoProps>; label: string }
> = {
  info: {
    border: "var(--di-sky-line)",
    bg: "var(--di-accent-soft)",
    icon: avisoComo("info"),
    label: "Información",
  },
  tip: {
    border: "color-mix(in srgb, var(--di-highlight-decor) 45%, transparent)",
    bg: "color-mix(in srgb, var(--di-highlight-decor) 10%, transparent)",
    icon: ICONO_DESTELLO,
    label: "Consejo",
  },
  warning: {
    border: "var(--badge-warning-border)",
    bg: "var(--badge-warning-bg)",
    icon: avisoComo("alerta"),
    label: "Importante",
  },
};

export function ManualCalloutBox({ callout }: { callout: ManualCallout }) {
  const s = styles[callout.variant];
  const Icon = s.icon;
  return (
    <aside
      className="mt-4 flex gap-3 rounded-xl border p-4"
      style={{ borderColor: s.border, background: s.bg }}
    >
      <Icon
        className="mt-0.5 h-5 w-5 shrink-0"
        style={{ color: "var(--manual-brand)" }}
        aria-hidden
      />
      <div className="min-w-0 text-sm leading-relaxed" style={{ color: "var(--manual-text)" }}>
        <p className="font-semibold" style={{ color: "var(--manual-heading)" }}>
          {callout.title ?? s.label}
        </p>
        <p className="mt-1">{callout.text}</p>
      </div>
    </aside>
  );
}
