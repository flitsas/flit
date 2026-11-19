"use client";

import { useSyncExternalStore } from "react";
import type { ComponentType } from "react";
import { ChipFiltro, CHIPS_FILTRO_FILA_CLS, TarjetaPlegable } from "@flit/ui";
import { iconoComo } from "@flit/ui/iconoComo";
import type { Contadores } from "@/lib/tipos";
import { TEXTO_JURIDICO, TEXTO_RAPIDO, type FiltroJuridico, type FiltroRapido } from "@/lib/filtros";

const RAPIDOS: { id: FiltroRapido; tono: string; icono: ComponentType<{ className?: string; "aria-hidden"?: boolean }>; contador: "proximosAVencer" | "sinContraventor" | "rechazadosOt" }[] = [
  { id: "ProximosAVencer", tono: "text-[color:var(--badge-danger-fg)]", icono: iconoComo("reloj"), contador: "proximosAVencer" },
  { id: "SinContraventor", tono: "text-[color:var(--badge-purple-fg)]", icono: iconoComo("persona"), contador: "sinContraventor" },
  { id: "RechazadosOt", tono: "text-[color:var(--badge-warning-fg)]", icono: iconoComo("alerta"), contador: "rechazadosOt" },
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
    <TarjetaPlegable titulo="Búsqueda rápida" abierta={abierto} onAlternar={guardarAbierto} nivelTitulo={2}>
      <div role="group" aria-label="Atajos de búsqueda rápida" className={`${CHIPS_FILTRO_FILA_CLS} items-center`}>
        {RAPIDOS.map((r) => {
          const activo = rapido === r.id;
          const IconoAtajo = r.icono;
          return (
            <ChipFiltro
              key={r.id}
              activo={activo}
              onClick={() => onRapido(activo ? null : r.id)}
              etiqueta={
                <span className="inline-flex items-center gap-1.5">
                  {/* El color de cada atajo queda en su ícono; el chip es el del kit. */}
                  <IconoAtajo className={`h-4 w-4 shrink-0 ${r.tono}`} aria-hidden />
                  {TEXTO_RAPIDO[r.id]}
                  <span className="rounded-full bg-[var(--kit-hover)] px-2 py-0.5 text-xs tabular-nums">{contadores ? contadores[r.contador] : "…"}</span>
                </span>
              }
            />
          );
        })}
        <ChipFiltro activo={sinGestion} onClick={() => onSinGestion(!sinGestion)} etiqueta="Sin gestión" />
        <span className="mx-1 h-6 w-px bg-[color:var(--di-divider)]" aria-hidden />
        {JURIDICOS.map((j) => {
          const activo = juridico === j;
          return <ChipFiltro key={j} activo={activo} onClick={() => onJuridico(activo ? null : j)} etiqueta={TEXTO_JURIDICO[j]} />;
        })}
      </div>
    </TarjetaPlegable>
  );
}
