import type { ReactNode } from "react";

export function ModuleTitle({ title, subtitle, right }: { title: string; subtitle?: string; right?: ReactNode }) {
  return (
    <div className="grid grid-cols-1 md:grid-cols-3 gap-3 shrink-0">
      <div className="md:col-span-2 rounded-2xl px-5 py-3 border border-[#DFE5ED] dark:border-white/10 bg-white dark:bg-[#0B0F14] flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h1 className="text-2xl font-bold leading-tight" style={{ color: "#557eff" }}>{title}</h1>
          {subtitle && <p className="text-xs opacity-70 mt-0.5">{subtitle}</p>}
        </div>
        {right}
      </div>
    </div>
  );
}