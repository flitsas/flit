"use client";

import { AlertTriangle, Clock, UserX } from "lucide-react";
import type { Contadores } from "@/lib/tipos";
import type { FiltroJuridico, FiltroRapido } from "@/lib/filtros";

const RAPIDOS: { id: FiltroRapido; texto: string; color: string; icono: typeof Clock; contador: keyof Contadores }[] = [
  { id: "ProximosAVencer", texto: "Próximos a vencer", color: "#E5484D", icono: Clock, contador: "proximosAVencer" },
  { id: "SinContraventor", texto: "Sin contraventor (manual)", color: "#D9A400", icono: UserX, contador: "sinContraventor" },
  { id: "RechazadosOt", texto: "Rechazados por OT", color: "#FF7A00", icono: AlertTriangle, contador: "rechazadosOt" },
];

const JURIDICOS: { id: FiltroJuridico | "C321" | "PicoPlaca"; texto: string; habilitado: boolean }[] = [
  { id: "CaducidadInmediata", texto: "Caducidad inmediata", habilitado: true },
  { id: "NoImpugnables", texto: "No impugnables", habilitado: true },
  { id: "C038", texto: "C-038", habilitado: true },
  { id: "C321", texto: "C-321", habilitado: false },
  { id: "PicoPlaca", texto: "Pico y placa eléctricos", habilitado: false },
];

/** Filtros rápidos con su conteo (§6.11), el switch «Sin gestión» y los filtros jurídicos predefinidos (RN-182). */
export function BarraFiltrosRapidos({
  contadores,
  rapido,
  onRapido,
  sinGestion,
  onSinGestion,
  juridico,
  onJuridico,
}: {
  contadores: Contadores | null;
  rapido: FiltroRapido | null;
  onRapido: (r: FiltroRapido | null) => void;
  sinGestion: boolean;
  onSinGestion: (v: boolean) => void;
  juridico: FiltroJuridico | null;
  onJuridico: (j: FiltroJuridico | null) => void;
}) {
  return (
    <div className="flex flex-col gap-3">
      <div className="flex flex-wrap items-center gap-2">
        {RAPIDOS.map((r) => {
          const activo = rapido === r.id;
          const Icono = r.icono;
          return (
            <button
              key={r.id}
              type="button"
              onClick={() => onRapido(activo ? null : r.id)}
              aria-pressed={activo}
              className="inline-flex h-9 items-center gap-2 rounded-xl border px-3 text-xs font-semibold transition"
              style={{
                borderColor: r.color,
                color: activo ? "#FFFFFF" : r.color,
                background: activo ? r.color : "transparent",
              }}
            >
              <Icono className="h-3.5 w-3.5" aria-hidden />
              {r.texto}
              <span className="rounded-full bg-black/10 px-2 py-0.5 text-[11px]">{contadores ? contadores[r.contador] : "…"}</span>
            </button>
          );
        })}
        <label className="ml-2 inline-flex items-center gap-2 text-xs font-semibold">
          <input type="checkbox" checked={sinGestion} onChange={(e) => onSinGestion(e.target.checked)} />
          Sin gestión
        </label>
      </div>
      <div className="flex flex-wrap items-center gap-2" role="group" aria-label="Filtros jurídicos">
        <span className="text-[11px] font-semibold uppercase tracking-wide opacity-60">Filtros jurídicos</span>
        {JURIDICOS.map((j) => {
          const activo = juridico === j.id;
          return (
            <button
              key={j.id}
              type="button"
              disabled={!j.habilitado}
              title={j.habilitado ? undefined : "Pendiente de definición"}
              aria-pressed={activo}
              onClick={() => onJuridico(activo ? null : (j.id as FiltroJuridico))}
              className={`inline-flex h-8 items-center rounded-full border px-3 text-[11px] font-semibold transition disabled:cursor-not-allowed disabled:opacity-40 ${
                activo ? "border-[#557EFF] bg-[#557EFF] text-white" : "border-[#DFE5ED] bg-white text-[#1E293B] dark:border-white/15 dark:bg-[#0B0F14] dark:text-white"
              }`}
            >
              {j.texto}
            </button>
          );
        })}
      </div>
    </div>
  );
}
