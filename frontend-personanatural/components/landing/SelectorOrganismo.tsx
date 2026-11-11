"use client";

import { ChevronDown, Search } from "lucide-react";
import { useEffect, useId, useRef, useState, type KeyboardEvent } from "react";
import type { Organismo } from "@/lib/api/cliente";
import { filtrarOrganismos } from "@/lib/landing/catalogos";

interface Props {
  organismos: readonly Organismo[];
  valor: Organismo | null;
  onChange: (organismo: Organismo) => void;
  disabled?: boolean;
}

/**
 * Selector «Organismo de tránsito» con buscador (el dropdown del modal de cotizar en el diseño), con el catálogo real,
 * para que la HU E4 lo use tal cual. Operable con teclado: flechas, Enter y Escape; devuelve el foco al cerrar.
 */
export function SelectorOrganismo({ organismos, valor, onChange, disabled = false }: Props) {
  const idLista = useId();
  const [abierto, setAbierto] = useState(false);
  const [consulta, setConsulta] = useState("");
  const [indice, setIndice] = useState(0);
  const raiz = useRef<HTMLDivElement>(null);
  const disparador = useRef<HTMLButtonElement>(null);
  const filtrados = filtrarOrganismos(organismos, consulta);

  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: MouseEvent) => {
      if (raiz.current && !raiz.current.contains(e.target as Node)) setAbierto(false);
    };
    document.addEventListener("mousedown", fuera);
    return () => document.removeEventListener("mousedown", fuera);
  }, [abierto]);

  const cerrar = () => {
    setAbierto(false);
    setConsulta("");
    disparador.current?.focus();
  };
  const elegir = (o: Organismo) => {
    onChange(o);
    cerrar();
  };
  const alTeclear = (e: KeyboardEvent) => {
    if (e.key === "ArrowDown") setIndice((i) => Math.min(filtrados.length - 1, i + 1));
    else if (e.key === "ArrowUp") setIndice((i) => Math.max(0, i - 1));
    else if (e.key === "Enter") {
      if (filtrados[indice]) elegir(filtrados[indice]);
    } else if (e.key === "Escape") cerrar();
    else return;
    e.preventDefault();
  };

  return (
    <div ref={raiz} className="relative">
      <button
        ref={disparador}
        type="button"
        aria-haspopup="listbox"
        aria-expanded={abierto}
        disabled={disabled}
        onClick={() => {
          setIndice(0);
          setAbierto((v) => !v);
        }}
        className="field flex w-full items-center justify-between text-left disabled:opacity-60"
      >
        <span className={valor ? "" : "text-foreground/50"}>{valor?.nombre ?? "Selecciona un organismo"}</span>
        <ChevronDown aria-hidden className={`h-4 w-4 text-primary transition-transform ${abierto ? "rotate-180" : ""}`} />
      </button>
      {abierto && (
        <div className="absolute left-0 right-0 top-full z-50 mt-1 rounded-[9px] border border-border bg-card p-2 shadow-float">
          <div className="relative">
            <Search aria-hidden className="absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 text-primary" />
            <input
              autoFocus
              role="combobox"
              aria-expanded
              aria-controls={idLista}
              aria-activedescendant={filtrados[indice] ? `${idLista}-${indice}` : undefined}
              aria-label="Buscar organismo de tránsito"
              className="field !pl-10"
              placeholder="Buscar municipio"
              autoComplete="off"
              value={consulta}
              onChange={(e) => {
                setConsulta(e.target.value);
                setIndice(0);
              }}
              onKeyDown={alTeclear}
            />
          </div>
          <ul id={idLista} role="listbox" aria-label="Organismos de tránsito" className="mt-1 max-h-40 overflow-y-auto">
            {filtrados.map((o, i) => (
              <li key={o.id} id={`${idLista}-${i}`} role="option" aria-selected={valor?.id === o.id}>
                <button type="button" tabIndex={-1} onClick={() => elegir(o)} className={`block w-full rounded-[9px] px-3 py-2 text-left text-sm hover:bg-primary/10 ${valor?.id === o.id ? "bg-primary/10 font-semibold text-primary" : i === indice ? "bg-primary/5" : ""}`}>
                  {o.nombre}
                </button>
              </li>
            ))}
            {filtrados.length === 0 && <li role="presentation" className="px-3 py-2 text-sm">No hay organismos que coincidan.</li>}
          </ul>
        </div>
      )}
    </div>
  );
}
