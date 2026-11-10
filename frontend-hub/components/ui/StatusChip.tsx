// Estado de un producto en una línea: disponible hoy o próximamente. Con la forma de los estados de la propuesta
// (StatusBadge de @flit/ui, HU #14487): la marca antes del texto, para no decirlo solo con color.
const CHIP = "inline-flex items-center gap-1.5 rounded-full px-2.5 py-[3px] text-[11px] font-semibold leading-tight";

export function StatusChip({ status }: { status: "available" | "soon" }) {
  return status === "available" ? (
    <span className={`${CHIP} bg-[var(--badge-success-bg)] text-[var(--badge-success-fg)]`}>
      <svg aria-hidden="true" viewBox="0 0 10 8" className="h-2 w-2.5 shrink-0" fill="none">
        <path d="M1 4.2 3.7 6.8 9 1.2" stroke="var(--di-success-dot)" strokeWidth="2" strokeLinecap="round" strokeLinejoin="round" />
      </svg>
      Disponible
    </span>
  ) : (
    <span className={`${CHIP} bg-[var(--badge-info-bg)] text-[var(--badge-info-fg)]`}>
      <span className="h-1.5 w-1.5 shrink-0 rounded-full bg-current" aria-hidden="true" />
      Próximamente
    </span>
  );
}
