"use client";

import { useEffect, useId, useRef, useState } from "react";
import { Columns3 } from "lucide-react";
import { controlCls } from "@/components/ui/estilos";

export interface OpcionColumna {
  clave: string;
  texto: string;
}

/**
 * Botón «Columnas» con su lista de casillas, copiado del de la tabla de Trámites (HU #13868) para que las dos barras se
 * vean iguales. Nunca deja cero columnas: la última marcada queda deshabilitada. Cierra con Escape o clic fuera.
 */
export function SelectorColumnas({
  columnas,
  visibles,
  onCambio,
}: {
  columnas: readonly OpcionColumna[];
  visibles: readonly string[];
  onCambio: (visibles: string[]) => void;
}) {
  const [abierto, setAbierto] = useState(false);
  const panel = useRef<HTMLDivElement>(null);
  const boton = useRef<HTMLButtonElement>(null);
  const id = useId();

  useEffect(() => {
    if (!abierto) return;
    const tecla = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setAbierto(false);
        boton.current?.focus();
      }
    };
    const fuera = (e: MouseEvent) => {
      const t = e.target as Node;
      if (panel.current?.contains(t) || boton.current?.contains(t)) return;
      setAbierto(false);
    };
    document.addEventListener("keydown", tecla);
    document.addEventListener("mousedown", fuera);
    return () => {
      document.removeEventListener("keydown", tecla);
      document.removeEventListener("mousedown", fuera);
    };
  }, [abierto]);

  const alternar = (clave: string) => {
    const marcada = visibles.includes(clave);
    if (marcada && visibles.length <= 1) return;
    const nuevas = new Set(marcada ? visibles.filter((k) => k !== clave) : [...visibles, clave]);
    onCambio(columnas.map((c) => c.clave).filter((k) => nuevas.has(k)));
  };

  return (
    <div className="relative">
      <button
        ref={boton}
        type="button"
        aria-haspopup="true"
        aria-expanded={abierto}
        aria-controls={abierto ? id : undefined}
        aria-label="Columnas: elegir columnas visibles"
        onClick={() => setAbierto((a) => !a)}
        className={controlCls(false)}
      >
        <Columns3 className="h-3.5 w-3.5" aria-hidden />
        Columnas
      </button>
      {abierto && (
        <div
          ref={panel}
          id={id}
          role="dialog"
          aria-label="Elegir columnas visibles"
          className="absolute left-0 top-full z-30 mt-1 flex max-h-[min(70vh,28rem)] w-72 flex-col overflow-hidden rounded-xl border border-[#DFE5ED] bg-white shadow-lg dark:border-white/10 dark:bg-[#162744]"
        >
          <div className="flex shrink-0 items-baseline justify-between gap-2 border-b border-[#DFE5ED] px-4 py-2.5 dark:border-white/10">
            <span className="text-[11px] font-semibold uppercase tracking-wide opacity-60">Columnas</span>
            <span className="text-[11px] tabular-nums opacity-50">
              {visibles.length} de {columnas.length}
            </span>
          </div>
          <div className="min-h-0 flex-1 overflow-y-auto px-2 py-2">
            {columnas.map((c) => {
              const marcada = visibles.includes(c.clave);
              const unica = marcada && visibles.length <= 1;
              return (
                <label
                  key={c.clave}
                  className={`flex items-center gap-2.5 rounded-lg px-2 py-1.5 text-xs transition hover:bg-flit-brand/10 ${unica ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
                >
                  <input type="checkbox" checked={marcada} disabled={unica} onChange={() => alternar(c.clave)} className="h-3.5 w-3.5 shrink-0 accent-flit-brand" />
                  <span className="min-w-0 flex-1 truncate">{c.texto}</span>
                </label>
              );
            })}
          </div>
          <p className="shrink-0 border-t border-[#DFE5ED] px-4 py-2 text-[10px] opacity-60 dark:border-white/10">Debes mantener al menos una columna visible.</p>
        </div>
      )}
    </div>
  );
}
