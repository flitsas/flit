"use client";

import { ChevronDown } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { controlCls } from "@/components/ui/estilos";

export interface Opcion {
  valor: string;
  texto: string;
}

/**
 * Lista desplegable con casillas para elegir varios valores. Con `onBuscar`, muestra una caja de búsqueda y quien la
 * usa decide qué opciones ofrecer (p. ej. organismos de tránsito, que son cientos).
 */
export function MultiSeleccion({
  etiqueta,
  opciones,
  valores,
  onCambio,
  onBuscar,
  textosElegidos,
}: {
  etiqueta: string;
  opciones: Opcion[];
  valores: string[];
  onCambio: (valores: string[]) => void;
  onBuscar?: (texto: string) => void;
  /** Textos de los valores elegidos que no estén en `opciones` (búsquedas anteriores). */
  textosElegidos?: Record<string, string>;
}) {
  const [abierto, setAbierto] = useState(false);
  const [busqueda, setBusqueda] = useState("");
  const ref = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!abierto) return;
    const cerrar = (e: MouseEvent) => {
      if (ref.current && !ref.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener("mousedown", cerrar);
    return () => document.removeEventListener("mousedown", cerrar);
  }, [abierto]);

  const alternar = (valor: string) => onCambio(valores.includes(valor) ? valores.filter((v) => v !== valor) : [...valores, valor]);
  const elegidasFuera = valores.filter((v) => !opciones.some((o) => o.valor === v));
  const visibles = [...elegidasFuera.map((v) => ({ valor: v, texto: textosElegidos?.[v] ?? v })), ...opciones];

  return (
    <div ref={ref} className="relative">
      <button type="button" className={`${controlCls(valores.length > 0)} w-full justify-between`} onClick={() => setAbierto((a) => !a)} aria-expanded={abierto}>
        <span className="truncate">{valores.length > 0 ? `${etiqueta} (${valores.length})` : etiqueta}</span>
        <ChevronDown className="h-3.5 w-3.5 shrink-0" aria-hidden />
      </button>
      {abierto && (
        <div className="absolute z-30 mt-1 max-h-72 w-72 overflow-auto rounded-xl border border-[#DFE5ED] bg-white p-2 shadow-lg dark:border-white/15 dark:bg-[#0B0F14]">
          {onBuscar && (
            <input
              value={busqueda}
              onChange={(e) => {
                setBusqueda(e.target.value);
                onBuscar(e.target.value);
              }}
              placeholder="Buscar…"
              aria-label={`Buscar en ${etiqueta}`}
              className="mb-2 h-8 w-full rounded-lg border border-[#DFE5ED] px-2 text-xs dark:border-white/15 dark:bg-transparent"
            />
          )}
          {visibles.length === 0 && <p className="px-2 py-1 text-xs opacity-60">Sin opciones</p>}
          {visibles.map((o) => (
            <label key={o.valor} className="flex cursor-pointer items-center gap-2 rounded-lg px-2 py-1.5 text-xs hover:bg-[#EFF6FF] dark:hover:bg-white/5">
              <input type="checkbox" checked={valores.includes(o.valor)} onChange={() => alternar(o.valor)} />
              <span className="truncate">{o.texto}</span>
            </label>
          ))}
          {valores.length > 0 && (
            <button type="button" className="mt-1 w-full rounded-lg px-2 py-1 text-left text-xs text-[#3B4FD6] hover:underline" onClick={() => onCambio([])}>
              Quitar selección
            </button>
          )}
        </div>
      )}
    </div>
  );
}
