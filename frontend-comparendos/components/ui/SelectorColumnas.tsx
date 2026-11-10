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
          className="absolute left-0 top-full z-30 mt-1 flex max-h-[min(70vh,28rem)] w-72 flex-col overflow-clip rounded-[var(--di-radius-modal)] bg-card text-foreground shadow-[var(--shadow-modal)]"
        >
          {/* Franja tinta del Modal de @flit/ui (homologación HU #14512), repintada en su sitio. */}
          <div className="relative flex shrink-0 items-baseline justify-between gap-2 overflow-clip bg-[color:var(--di-accent)] px-5 py-3.5 text-[color:var(--di-on-accent)]">
            <span
              aria-hidden="true"
              className="pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]"
            />
            <span className="relative z-[1] text-sm font-semibold">Columnas</span>
            {/* Pastilla tinta: el contador cae sobre el círculo turquesa de la franja y sin ella no se lee. */}
            <span className="relative z-[1] rounded-full bg-[color:var(--di-accent)] px-2 py-0.5 text-[11px] tabular-nums">
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
                  className={`flex items-center gap-2.5 rounded-lg px-2 py-1.5 text-xs transition hover:bg-secondary ${unica ? "cursor-not-allowed opacity-60" : "cursor-pointer"}`}
                >
                  <input type="checkbox" checked={marcada} disabled={unica} onChange={() => alternar(c.clave)} className="h-3.5 w-3.5 shrink-0 accent-flit-brand" />
                  <span className="min-w-0 flex-1 truncate">{c.texto}</span>
                </label>
              );
            })}
          </div>
          <p className="shrink-0 border-t border-[color:var(--di-divider)] px-5 py-2.5 text-[11px] text-muted-foreground">Debes mantener al menos una columna visible.</p>
        </div>
      )}
    </div>
  );
}
