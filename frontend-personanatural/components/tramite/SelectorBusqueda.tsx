"use client";

import { Check, ChevronDown, Loader2 } from "lucide-react";
import { useEffect, useId, useLayoutEffect, useMemo, useRef, useState, type CSSProperties, type KeyboardEvent } from "react";
import { createPortal } from "react-dom";

export interface OpcionBusqueda {
  codigo: string;
  etiqueta: string;
  /** Línea secundaria (p. ej. municipio y departamento). */
  detalle?: string | undefined;
}

interface Props {
  /** Nombre accesible del campo; también es el de la lista. */
  etiqueta: string;
  opciones: readonly OpcionBusqueda[];
  /** Código elegido (vacío = nada). */
  valor: string;
  /** Etiqueta a mostrar cuando el valor elegido no está en las opciones cargadas (p. ej. un destino ya guardado). */
  etiquetaElegida?: string | undefined;
  onChange: (opcion: OpcionBusqueda) => void;
  disabled?: boolean;
  invalido?: boolean;
  marcador?: string;
  /** La lista llega del servidor: cada cambio del texto se informa para buscar y no se filtra aquí. */
  onBuscar?: ((texto: string) => void) | undefined;
  cargando?: boolean;
  /** Texto cuando no hay coincidencias. */
  sinResultados?: string;
  idError?: string | undefined;
  onBlur?: () => void;
}

const sinTildes = (t: string): string => t.normalize("NFD").replace(/\p{M}/gu, "").toLowerCase();

/**
 * Lista con búsqueda del portal: mismo aspecto y portal que {@link SelectorTipoTramite} (campo `field`, lista `transaction-options`),
 * con un cuadro de texto que filtra. Es un combobox accesible: teclado completo, Escape y foco de vuelta. Con `onBuscar` el
 * filtrado lo hace el servidor; sin él, se filtra en el navegador ignorando tildes y mayúsculas.
 */
export function SelectorBusqueda({ etiqueta, opciones, valor, etiquetaElegida, onChange, disabled = false, invalido = false, marcador = "Selecciona", onBuscar, cargando = false, sinResultados = "Sin coincidencias.", idError, onBlur }: Props) {
  const idLista = useId();
  const [abierto, setAbierto] = useState(false);
  const [texto, setTexto] = useState("");
  const [indice, setIndice] = useState(0);
  const raiz = useRef<HTMLDivElement>(null);
  const entrada = useRef<HTMLInputElement>(null);
  const lista = useRef<HTMLUListElement>(null);
  const [posicion, setPosicion] = useState<CSSProperties | null>(null);
  const [destino, setDestino] = useState<HTMLElement | null>(null);

  const elegida = opciones.find((o) => o.codigo === valor);
  const nombreElegido = elegida?.etiqueta ?? etiquetaElegida ?? "";
  const visibles = useMemo(() => {
    if (onBuscar || texto.trim() === "") return opciones;
    const aguja = sinTildes(texto.trim());
    return opciones.filter((o) => sinTildes(`${o.etiqueta} ${o.detalle ?? ""}`).includes(aguja));
  }, [opciones, texto, onBuscar]);

  useLayoutEffect(() => {
    if (!abierto || !entrada.current) return;
    const campo = entrada.current;
    setDestino(campo.closest("dialog") ?? document.body);
    const ubicar = () => {
      const r = campo.getBoundingClientRect();
      setPosicion({ position: "fixed", top: r.bottom + 4, left: r.left, width: r.width });
    };
    ubicar();
    window.addEventListener("resize", ubicar);
    window.addEventListener("scroll", ubicar, true);
    return () => {
      window.removeEventListener("resize", ubicar);
      window.removeEventListener("scroll", ubicar, true);
    };
  }, [abierto]);

  useEffect(() => {
    if (!abierto) return;
    const fuera = (e: MouseEvent) => {
      const nodo = e.target as Node;
      if (!raiz.current?.contains(nodo) && !lista.current?.contains(nodo)) setAbierto(false);
    };
    document.addEventListener("mousedown", fuera);
    return () => document.removeEventListener("mousedown", fuera);
  }, [abierto]);

  const cerrar = () => {
    setAbierto(false);
    setTexto("");
  };
  const elegir = (opcion: OpcionBusqueda) => {
    onChange(opcion);
    cerrar();
  };
  const escribir = (nuevo: string) => {
    setTexto(nuevo);
    setIndice(0);
    setAbierto(true);
    onBuscar?.(nuevo);
  };
  const alTeclear = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown") {
      if (!abierto) setAbierto(true);
      else setIndice((i) => Math.min(visibles.length - 1, i + 1));
    } else if (e.key === "ArrowUp") setIndice((i) => Math.max(0, i - 1));
    else if (e.key === "Enter") {
      if (!abierto || !visibles[indice]) return;
      elegir(visibles[indice]);
    } else if (e.key === "Escape") {
      if (!abierto) return;
      cerrar();
    } else return;
    e.preventDefault();
  };

  return (
    <div ref={raiz} className="relative">
      <div className="relative">
        <input
          ref={entrada}
          type="text"
          role="combobox"
          aria-label={etiqueta}
          aria-autocomplete="list"
          aria-haspopup="listbox"
          aria-expanded={abierto}
          aria-controls={abierto ? idLista : undefined}
          aria-activedescendant={abierto && visibles[indice] ? `${idLista}-${indice}` : undefined}
          aria-invalid={invalido ? true : undefined}
          aria-describedby={idError}
          autoComplete="off"
          disabled={disabled}
          className="field w-full pr-10"
          placeholder={abierto ? "Escribe para buscar" : nombreElegido ? "" : marcador}
          value={abierto ? texto : nombreElegido}
          onFocus={() => setAbierto(true)}
          onClick={() => setAbierto(true)}
          onChange={(e) => escribir(e.target.value)}
          onKeyDown={alTeclear}
          onBlur={() => onBlur?.()}
        />
        <span aria-hidden className="pointer-events-none absolute inset-y-0 right-3 flex items-center text-primary">
          {cargando ? <Loader2 className="h-4 w-4 animate-spin" /> : <ChevronDown className="h-4 w-4" />}
        </span>
      </div>
      {abierto && destino && posicion && createPortal(
        <ul
          ref={lista}
          id={idLista}
          role="listbox"
          aria-label={etiqueta}
          aria-busy={cargando || undefined}
          style={posicion}
          className="transaction-options z-50 max-h-72 overflow-y-auto rounded-[9px] border border-border bg-popover p-1 text-popover-foreground outline-none"
        >
          {visibles.length === 0 ? (
            <li role="presentation" className="px-3 py-2 text-sm font-light text-muted-foreground">
              {cargando ? "Buscando..." : sinResultados}
            </li>
          ) : (
            visibles.map((o, i) => (
              <li
                key={o.codigo}
                id={`${idLista}-${i}`}
                role="option"
                aria-selected={o.codigo === valor}
                // mousedown evita que el campo pierda el foco antes de elegir.
                onMouseDown={(e) => e.preventDefault()}
                onClick={() => elegir(o)}
                onMouseMove={() => setIndice(i)}
                className={`relative cursor-pointer rounded-md py-2 pl-8 pr-3 text-sm ${i === indice ? "bg-primary/10 text-primary" : ""}`}
              >
                {o.codigo === valor && <Check aria-hidden className="absolute left-2 top-2.5 h-4 w-4" />}
                {o.etiqueta}
                {o.detalle && <span className="block text-xs font-light text-muted-foreground">{o.detalle}</span>}
              </li>
            ))
          )}
        </ul>,
        destino,
      )}
    </div>
  );
}
