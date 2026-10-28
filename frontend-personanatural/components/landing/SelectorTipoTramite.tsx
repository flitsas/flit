"use client";

import { Check, ChevronDown } from "lucide-react";
import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import type { TipoTramite } from "@/lib/api/cliente";

interface Props {
  etiqueta?: string;
  opciones: readonly TipoTramite[];
  valor: string;
  onChange: (code: string) => void;
  disabled?: boolean;
  /** Texto del campo cuando no hay nada elegido (p. ej. «Cargando…»). */
  marcador?: string;
}

// Mismo aspecto del TransactionSelect del diseño (radix Select), hecho como listbox accesible: teclado completo, Escape y foco de vuelta.
export function SelectorTipoTramite({ etiqueta = "Tipo de Trámite", opciones, valor, onChange, disabled = false, marcador = "Selecciona" }: Props) {
  const idLista = useId();
  const [abierto, setAbierto] = useState(false);
  const [indice, setIndice] = useState(0);
  const raiz = useRef<HTMLDivElement>(null);
  const disparador = useRef<HTMLButtonElement>(null);
  const lista = useRef<HTMLUListElement>(null);
  const elegido = opciones.find((o) => o.code === valor);

  useEffect(() => {
    if (!abierto) return;
    lista.current?.focus();
    const fuera = (e: MouseEvent) => {
      if (raiz.current && !raiz.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener("mousedown", fuera);
    return () => document.removeEventListener("mousedown", fuera);
  }, [abierto]);

  const abrir = () => {
    setIndice(Math.max(0, opciones.findIndex((o) => o.code === valor)));
    setAbierto(true);
  };
  const cerrar = () => {
    setAbierto(false);
    disparador.current?.focus();
  };
  const elegir = (code: string) => {
    onChange(code);
    cerrar();
  };
  const alTeclear = (e: KeyboardEvent) => {
    if (e.key === "ArrowDown") setIndice((i) => Math.min(opciones.length - 1, i + 1));
    else if (e.key === "ArrowUp") setIndice((i) => Math.max(0, i - 1));
    else if (e.key === "Home") setIndice(0);
    else if (e.key === "End") setIndice(opciones.length - 1);
    else if (e.key === "Enter" || e.key === " ") {
      if (opciones[indice]) elegir(opciones[indice].code);
    } else if (e.key === "Escape" || e.key === "Tab") {
      cerrar();
      return;
    } else return;
    e.preventDefault();
  };

  return (
    <div ref={raiz} className="relative">
      <button
        ref={disparador}
        type="button"
        aria-label={etiqueta}
        aria-haspopup="listbox"
        aria-expanded={abierto}
        aria-controls={abierto ? idLista : undefined}
        disabled={disabled}
        data-state={abierto ? "open" : "closed"}
        onClick={() => (abierto ? cerrar() : abrir())}
        onKeyDown={(e) => {
          if (!abierto && (e.key === "ArrowDown" || e.key === "ArrowUp")) {
            e.preventDefault();
            abrir();
          }
        }}
        className="field flex w-full items-center justify-between gap-2 text-left disabled:opacity-60"
      >
        <span className="truncate">{elegido?.name ?? marcador}</span>
        <ChevronDown aria-hidden className="h-4 w-4 shrink-0 text-primary" />
      </button>
      {abierto && (
        <ul
          ref={lista}
          id={idLista}
          role="listbox"
          tabIndex={-1}
          aria-label={etiqueta}
          aria-activedescendant={`${idLista}-${indice}`}
          onKeyDown={alTeclear}
          className="transaction-options absolute left-0 right-0 top-full z-50 mt-1 max-h-72 overflow-y-auto rounded-[9px] border border-border bg-popover p-1 text-popover-foreground outline-none"
        >
          {opciones.map((o, i) => (
            <li
              key={o.code}
              id={`${idLista}-${i}`}
              role="option"
              aria-selected={o.code === valor}
              onClick={() => elegir(o.code)}
              onMouseMove={() => setIndice(i)}
              className={`relative cursor-pointer rounded-md py-2 pl-8 pr-3 text-sm ${i === indice ? "bg-primary/10 text-primary" : ""}`}
            >
              {o.code === valor && <Check aria-hidden className="absolute left-2 top-2.5 h-4 w-4" />}
              {o.name}
            </li>
          ))}
        </ul>
      )}
    </div>
  );
}
