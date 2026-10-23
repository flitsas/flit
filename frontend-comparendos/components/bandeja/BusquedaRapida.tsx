"use client";

import { useSyncExternalStore } from "react";
import { AlertTriangle, ChevronDown, Clock, UserX } from "lucide-react";
import type { Contadores } from "@/lib/tipos";
import { TEXTO_JURIDICO, TEXTO_RAPIDO, type FiltroJuridico, type FiltroRapido } from "@/lib/filtros";

const RAPIDOS: { id: FiltroRapido; color: string; icono: typeof Clock; contador: "proximosAVencer" | "sinContraventor" | "rechazadosOt" }[] = [
  { id: "ProximosAVencer", color: "#E5484D", icono: Clock, contador: "proximosAVencer" },
  { id: "SinContraventor", color: "#B88A00", icono: UserX, contador: "sinContraventor" },
  { id: "RechazadosOt", color: "#E06A00", icono: AlertTriangle, contador: "rechazadosOt" },
];

// C-321 y Pico y placa eléctricos no están: siguen sin definición (S-57) y la HU #13870 los oculta hasta que existan.
const JURIDICOS: FiltroJuridico[] = ["CaducidadInmediata", "NoImpugnables", "C038"];

const CLAVE = "gdc.bandeja.busquedaRapida";

// Abierto o cerrado se recuerda en este navegador; sin almacenamiento vale lo de esta pantalla (como en Trámites).
const enMemoria = { abierto: null as boolean | null };
const suscriptores = new Set<() => void>();

function leerAbierto(): boolean {
  if (enMemoria.abierto !== null) return enMemoria.abierto;
  try {
    return window.localStorage.getItem(CLAVE) !== "cerrado";
  } catch {
    return true;
  }
}

function guardarAbierto(abierto: boolean) {
  enMemoria.abierto = abierto;
  try {
    window.localStorage.setItem(CLAVE, abierto ? "abierto" : "cerrado");
  } catch {
    // Sin almacenamiento (modo privado): no se recuerda entre visitas.
  }
  suscriptores.forEach((avisar) => avisar());
}

function suscribir(avisar: () => void) {
  suscriptores.add(avisar);
  return () => {
    suscriptores.delete(avisar);
  };
}

const ATAJO_CLS = "inline-flex h-8 items-center gap-1.5 rounded-lg border px-3 text-xs font-semibold transition focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand";
const atajoNeutro = (activo: boolean) =>
  `${ATAJO_CLS} ${
    activo
      ? "border-flit-brand bg-flit-brand/10 text-[#3B4FD6] dark:text-[#8FA8FF]"
      : "border-[#DFE5ED] text-flit-primary hover:border-flit-brand dark:border-white/15 dark:text-white/80"
  }`;

/**
 * HU #13870: «Búsqueda rápida» reúne los atajos de la bandeja (§6.11): los de color con su conteo, «Sin gestión» y los
 * filtros jurídicos (RN-182). Cada atajo se aplica al pulsarlo y se quita con un segundo clic.
 */
export function BusquedaRapida({
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
  // En el servidor siempre abierto, para que el primer render del navegador coincida.
  const abierto = useSyncExternalStore(suscribir, leerAbierto, () => true);

  return (
    <section className="rounded-2xl border border-[#DFE5ED] bg-white dark:border-white/10 dark:bg-[#0B0F14]">
      <h2 className="m-0">
        <button
          type="button"
          aria-expanded={abierto}
          aria-controls="bandeja-busqueda-rapida"
          onClick={() => guardarAbierto(!abierto)}
          className="flex w-full items-center justify-between rounded-2xl px-4 py-2.5 text-left text-xs font-semibold text-flit-primary transition hover:bg-flit-brand/[0.04] focus:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-flit-brand dark:text-white"
        >
          Búsqueda rápida
          <ChevronDown className={`h-4 w-4 opacity-60 transition-transform motion-reduce:transition-none ${abierto ? "rotate-180" : ""}`} aria-hidden />
        </button>
      </h2>
      <div id="bandeja-busqueda-rapida" hidden={!abierto} className="px-4 pb-3">
        <div role="group" aria-label="Atajos de búsqueda rápida" className="flex flex-wrap items-center gap-2">
          {RAPIDOS.map((r) => {
            const activo = rapido === r.id;
            const Icono = r.icono;
            return (
              <button
                key={r.id}
                type="button"
                onClick={() => onRapido(activo ? null : r.id)}
                aria-pressed={activo}
                className={ATAJO_CLS}
                style={{ borderColor: r.color, color: activo ? "#FFFFFF" : r.color, background: activo ? r.color : "transparent" }}
              >
                <Icono className="h-3.5 w-3.5" aria-hidden />
                {TEXTO_RAPIDO[r.id]}
                <span className="rounded-full bg-black/10 px-2 py-0.5 text-[11px] tabular-nums">{contadores ? contadores[r.contador] : "…"}</span>
              </button>
            );
          })}
          <button type="button" aria-pressed={sinGestion} onClick={() => onSinGestion(!sinGestion)} className={atajoNeutro(sinGestion)}>
            Sin gestión
          </button>
          <span className="mx-1 h-5 w-px bg-[#DFE5ED] dark:bg-white/15" aria-hidden />
          {JURIDICOS.map((j) => {
            const activo = juridico === j;
            return (
              <button key={j} type="button" aria-pressed={activo} onClick={() => onJuridico(activo ? null : j)} className={atajoNeutro(activo)}>
                {TEXTO_JURIDICO[j]}
              </button>
            );
          })}
        </div>
      </div>
    </section>
  );
}
