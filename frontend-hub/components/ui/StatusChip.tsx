// Estado de un producto en una línea: disponible hoy o próximamente.
export function StatusChip({ status }: { status: "available" | "soon" }) {
  return status === "available" ? (
    <span className="inline-flex items-center gap-1 rounded-full bg-[#F3FBE8] px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-[#4F7A12] dark:bg-[#4F7A12]/20 dark:text-[#b9e27a]">
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
      Disponible
    </span>
  ) : (
    <span className="inline-flex items-center gap-1 rounded-full bg-flit-brand/10 px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide text-flit-brand dark:bg-flit-brand/20 dark:text-[#9db4ff]">
      <span className="h-1.5 w-1.5 rounded-full bg-current" aria-hidden="true" />
      Próximamente
    </span>
  );
}
