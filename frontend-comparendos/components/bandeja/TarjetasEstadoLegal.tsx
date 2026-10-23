"use client";

import { AlertCircle, Archive, CheckCircle2, CircleDashed, Hourglass } from "lucide-react";
import { ESTADO_LEGAL } from "@/lib/etiquetas";

const TARJETAS: { estado: string; icono: typeof CheckCircle2; color: string }[] = [
  { estado: "SinNotificar", icono: CircleDashed, color: "#64748B" },
  { estado: "EnTiempo", icono: CheckCircle2, color: "#16A34A" },
  { estado: "Vencido", icono: AlertCircle, color: "#DC2626" },
  { estado: "Caducado", icono: Hourglass, color: "#D97706" },
  { estado: "Prescrito", icono: Archive, color: "#2563EB" },
];

/**
 * HU #13869: las 5 tarjetas de estado legal con su conteo, como la tira de estados de Trámites. Clic elige el estado;
 * segundo clic lo suelta. Sin conteo todavía se pinta un guion: un 0 diría que no hay comparendos.
 */
export function TarjetasEstadoLegal({
  conteos,
  elegido,
  onElegir,
}: {
  conteos: Record<string, number> | null;
  elegido: string;
  onElegir: (estado: string) => void;
}) {
  return (
    <div
      role="group"
      aria-label="Estados legales de los comparendos"
      className="grid grid-cols-2 divide-[#EEF2F7] overflow-hidden rounded-2xl border border-[#DFE5ED] bg-white shadow-[0_4px_12px_rgba(0,0,0,0.04)] sm:grid-cols-5 sm:divide-x dark:divide-white/5 dark:border-white/10 dark:bg-[#0B0F14]"
    >
      {TARJETAS.map(({ estado, icono: Icono, color }) => {
        // El color del estado al 12%: se lee igual sobre el fondo claro y el oscuro.
        const fondo = `${color}1F`;
        const activo = elegido === estado;
        const texto = ESTADO_LEGAL[estado].texto;
        const conteo = conteos?.[estado];
        return (
          <button
            key={estado}
            type="button"
            aria-pressed={activo}
            aria-label={conteo === undefined ? texto : `${texto}: ${conteo} comparendo${conteo === 1 ? "" : "s"}`}
            onClick={() => onElegir(activo ? "" : estado)}
            className="flex flex-col items-center gap-1 px-2 py-2 transition hover:bg-flit-brand/[0.06] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-flit-brand dark:hover:bg-white/5"
            style={activo ? { background: fondo } : undefined}
          >
            <span className="flex h-7 w-7 items-center justify-center rounded-full" style={{ background: fondo, color }} aria-hidden>
              <Icono className="h-4 w-4" />
            </span>
            <span className={`text-center text-xs font-medium leading-tight text-flit-primary dark:text-white ${activo ? "" : "opacity-70"}`}>{texto}</span>
            <span className="text-lg font-bold leading-none tabular-nums text-[#1E293B] dark:text-white" aria-hidden>
              {conteo === undefined ? "—" : conteo.toLocaleString("es-CO")}
            </span>
            <span className="h-0.5 w-6 rounded-full" style={{ background: activo ? color : "transparent" }} aria-hidden />
          </button>
        );
      })}
    </div>
  );
}
