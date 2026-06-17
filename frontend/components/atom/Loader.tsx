import { Car } from "lucide-react";

export function VehicleLoader({ label = "Procesando…" }: { label?: string }) {
  return (
    <div className="flex flex-col items-center justify-center gap-3 py-6">
      <div className="flit-loader" role="status" aria-label={label}>
        <div className="flit-vehicle"><Car className="h-8 w-8" /></div>
      </div>
      <p className="text-xs font-mono uppercase tracking-wider text-[#162744]/70 dark:text-white/60">{label}</p>
    </div>
  );
}