import type { ManualCallout } from "@/lib/manual/types";
import { Info, Lightbulb, AlertTriangle } from "lucide-react";

const styles: Record<
  ManualCallout["variant"],
  { border: string; bg: string; icon: typeof Info; label: string }
> = {
  info: {
    border: "color-mix(in srgb, var(--color-flit-brand) 35%, transparent)",
    bg: "color-mix(in srgb, var(--color-flit-brand) 8%, transparent)",
    icon: Info,
    label: "Información",
  },
  tip: {
    border: "color-mix(in srgb, var(--color-flit-tech) 45%, transparent)",
    bg: "color-mix(in srgb, var(--color-flit-tech) 10%, transparent)",
    icon: Lightbulb,
    label: "Consejo",
  },
  warning: {
    border: "rgba(255, 78, 0, 0.35)",
    bg: "rgba(255, 78, 0, 0.08)",
    icon: AlertTriangle,
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
