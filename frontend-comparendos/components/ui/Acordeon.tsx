"use client";

import { ChevronDown } from "lucide-react";
import { useState, type ReactNode } from "react";

/**
 * Sección plegable del detalle del comparendo. Con `abierto` + `onAlternar` la controla quien la usa (p. ej. para
 * abrirla desde un enlace del timeline); si no, se maneja sola.
 */
export function Acordeon({
  titulo,
  abiertoInicial = false,
  abierto: controlado,
  onAlternar,
  id,
  extra,
  children,
}: {
  titulo: string;
  abiertoInicial?: boolean;
  abierto?: boolean;
  onAlternar?: (abierto: boolean) => void;
  id?: string;
  extra?: ReactNode;
  children: ReactNode;
}) {
  const [propio, setPropio] = useState(abiertoInicial);
  const abierto = controlado ?? propio;
  const alternar = () => (onAlternar ? onAlternar(!abierto) : setPropio((a) => !a));
  return (
    <section id={id} className="scroll-mt-4 rounded-2xl border border-[#DFE5ED] dark:border-white/10">
      <div className="flex items-center justify-between gap-3 px-4 py-3">
        <button type="button" className="flex flex-1 items-center gap-2 text-left text-sm font-semibold" onClick={alternar} aria-expanded={abierto}>
          <ChevronDown className={`h-4 w-4 transition ${abierto ? "rotate-180" : ""}`} aria-hidden />
          {titulo}
        </button>
        {extra}
      </div>
      {abierto && <div className="border-t border-[#DFE5ED] px-4 py-4 dark:border-white/10">{children}</div>}
    </section>
  );
}

/** Lista de datos «etiqueta: valor» en dos columnas. */
export function Datos({ filas }: { filas: [string, ReactNode][] }) {
  return (
    <dl className="grid grid-cols-1 gap-x-6 gap-y-2 text-xs sm:grid-cols-2">
      {filas.map(([etiqueta, valor]) => (
        <div key={etiqueta} className="flex flex-col">
          <dt className="text-[11px] font-semibold text-[#5B6B82] dark:text-slate-400">{etiqueta}</dt>
          <dd>{valor === null || valor === undefined || valor === "" ? "—" : valor}</dd>
        </div>
      ))}
    </dl>
  );
}
