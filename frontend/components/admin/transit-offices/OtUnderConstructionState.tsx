"use client";

import { BarChart3 } from "lucide-react";

/**
 * Estado vacío / “en construcción” del hub OT — patrón AlertCard + empty administrativo.
 * Semántica: borrador/inactivo (gris azulado) + acento de marca alineado a ModuleTitle.
 */
export function OtUnderConstructionState({
  code = "404",
  title,
  description,
  testId,
}: {
  code?: string;
  title: string;
  description: string;
  testId?: string;
}) {
  return (
    <div
      role="status"
      aria-live="polite"
      data-testid={testId}
      className="flex flex-col items-center justify-center gap-3 rounded-2xl border border-border bg-card px-6 py-14 text-center"
    >
      <div
        className="flex h-12 w-12 items-center justify-center rounded-xl bg-flit-brand/10"
        style={{ color: "var(--color-flit-brand)" }}
        aria-hidden="true"
      >
        <BarChart3 className="h-6 w-6" strokeWidth={1.8} />
      </div>
      <p
        className="text-3xl font-bold tracking-tight text-muted-foreground"
        aria-hidden="true"
      >
        {code}
      </p>
      <h2 className="text-base font-semibold text-flit-primary dark:text-foreground">{title}</h2>
      <p className="max-w-md text-sm text-muted-foreground">{description}</p>
      <span className="sr-only">
        {code}. {title}. {description}
      </span>
    </div>
  );
}
