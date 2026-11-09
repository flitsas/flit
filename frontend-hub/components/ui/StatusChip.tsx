// Estado de un producto en una línea: disponible hoy o próximamente.
export function StatusChip({ status }: { status: "available" | "soon" }) {
  return status === "available" ? (
    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--badge-success-bg)] px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--badge-success-fg)]">
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
      Disponible
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 rounded-full bg-[var(--badge-info-bg)] px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-[var(--badge-info-fg)]">
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
      Próximamente
    </span>
  );
}
