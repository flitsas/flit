"use client";

import { useEffect, useId, useMemo, useRef, useState } from "react";
import { Check, ChevronDown, Search, X } from "lucide-react";

/**
 * Selector con buscador interno (combobox). Nace para los pickers de compañía: con decenas de
 * empresas, un `<select>` nativo obliga a recorrer la lista a ojo; aquí se teclea y la lista se
 * filtra por etiqueta y por pista (NIT, código…).
 *
 * WCAG 2.1 AA — patrón combobox de APG:
 *   - input `role="combobox"` con `aria-expanded`, `aria-controls` y `aria-activedescendant`;
 *   - lista `role="listbox"` con opciones `role="option"` y `aria-selected`;
 *   - teclado completo: ↓/↑ recorren, Home/End saltan, Enter elige, Esc cierra, Tab sale;
 *   - la opción activa se anuncia por `aria-activedescendant` (el foco NO se mueve del input);
 *   - el número de resultados se anuncia por una región `aria-live`.
 */

export interface SearchableSelectOption {
  value: string;
  label: string;
  /** Texto secundario (NIT, código…). Se muestra atenuado y TAMBIÉN filtra. */
  hint?: string;
}

export interface SearchableSelectProps {
  options: SearchableSelectOption[];
  /** Valor seleccionado; cadena vacía = la opción por defecto. */
  value: string;
  onChange: (value: string) => void;
  /** Etiqueta visible del campo. */
  label: string;
  /** Etiqueta de la opción vacía (p. ej. "Mi compañía"). Omitida = no hay opción vacía. */
  defaultLabel?: string;
  disabled?: boolean;
  /** Texto del input cuando no hay nada tecleado y no hay selección. */
  placeholder?: string;
  /** Oculta la etiqueta visualmente; sigue accesible para lectores de pantalla. */
  hideLabel?: boolean;
  className?: string;
  id?: string;
}

/** Normaliza para comparar sin tildes ni mayúsculas: "Bogotá" encuentra a "bogota". */
function normalizar(texto: string): string {
  return texto
    .normalize("NFD")
    .replace(/[\u0300-\u036f]/g, "")
    .toLowerCase();
}

export function SearchableSelect({
  options,
  value,
  onChange,
  label,
  defaultLabel,
  disabled = false,
  placeholder = "Buscar…",
  hideLabel = false,
  className = "",
  id,
}: SearchableSelectProps) {
  const generatedId = useId();
  const baseId = id ?? `ss-${generatedId}`;
  const listId = `${baseId}-list`;

  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [activeIndex, setActiveIndex] = useState(0);
  const rootRef = useRef<HTMLDivElement>(null);
  const inputRef = useRef<HTMLInputElement>(null);

  // La opción vacía es una más de la lista: así el teclado y el filtrado la tratan igual.
  const todas = useMemo<SearchableSelectOption[]>(
    () => (defaultLabel === undefined ? options : [{ value: "", label: defaultLabel }, ...options]),
    [options, defaultLabel],
  );

  const filtradas = useMemo(() => {
    const q = normalizar(query.trim());
    if (q === "") return todas;
    return todas.filter(
      (o) => normalizar(o.label).includes(q) || (o.hint ? normalizar(o.hint).includes(q) : false),
    );
  }, [todas, query]);

  const seleccionada = todas.find((o) => o.value === value);
  const textoSeleccion = seleccionada?.label ?? "";

  // El índice activo se acota EN RENDER: al filtrar, la lista se acorta y el índice guardado puede
  // quedar fuera de rango. Corregirlo aquí evita un efecto que dispararía un render en cascada.
  const indiceActivo = activeIndex >= filtradas.length ? 0 : activeIndex;

  /** Abre la lista dejando activa la opción ya seleccionada (no la primera). */
  const abrir = () => {
    if (open || disabled) return;
    const idx = todas.findIndex((o) => o.value === value);
    setActiveIndex(idx >= 0 ? idx : 0);
    setOpen(true);
  };

  /** Cierra y descarta lo tecleado: el input vuelve a mostrar la selección vigente. */
  const cerrar = () => {
    setOpen(false);
    setQuery("");
  };

  // Clic fuera = cerrar sin cambiar nada.
  useEffect(() => {
    if (!open) return;
    const alClic = (e: MouseEvent) => {
      if (rootRef.current && !rootRef.current.contains(e.target as Node)) {
        setOpen(false);
        setQuery("");
      }
    };
    document.addEventListener("mousedown", alClic);
    return () => document.removeEventListener("mousedown", alClic);
  }, [open]);

  const elegir = (opcion: SearchableSelectOption) => {
    onChange(opcion.value);
    cerrar();
    inputRef.current?.focus();
  };

  const alTeclear = (e: React.KeyboardEvent<HTMLInputElement>) => {
    if (disabled) return;

    if (!open && (e.key === "ArrowDown" || e.key === "Enter")) {
      e.preventDefault();
      abrir();
      return;
    }
    if (!open) return;

    switch (e.key) {
      case "ArrowDown":
        e.preventDefault();
        setActiveIndex(filtradas.length === 0 ? 0 : (indiceActivo + 1) % filtradas.length);
        break;
      case "ArrowUp":
        e.preventDefault();
        setActiveIndex(
          filtradas.length === 0 ? 0 : (indiceActivo - 1 + filtradas.length) % filtradas.length,
        );
        break;
      case "Home":
        e.preventDefault();
        setActiveIndex(0);
        break;
      case "End":
        e.preventDefault();
        setActiveIndex(Math.max(0, filtradas.length - 1));
        break;
      case "Enter": {
        e.preventDefault();
        const opcion = filtradas[indiceActivo];
        if (opcion) elegir(opcion);
        break;
      }
      case "Escape":
        e.preventDefault();
        cerrar();
        break;
      case "Tab":
        cerrar();
        break;
      default:
        break;
    }
  };

  const activeId = filtradas[indiceActivo] ? `${baseId}-opt-${indiceActivo}` : undefined;

  return (
    <div className={`flex flex-col gap-1 ${className}`} ref={rootRef}>
      <label
        htmlFor={baseId}
        className={
          hideLabel
            ? "sr-only"
            : "text-[10px] font-semibold uppercase opacity-60 text-[#162744] dark:text-white"
        }
      >
        {label}
      </label>

      <div className="relative">
        <Search
          className="pointer-events-none absolute left-3 top-1/2 h-3.5 w-3.5 -translate-y-1/2 opacity-40"
          aria-hidden="true"
        />
        <input
          id={baseId}
          ref={inputRef}
          type="text"
          role="combobox"
          aria-expanded={open}
          aria-controls={listId}
          aria-autocomplete="list"
          aria-activedescendant={open ? activeId : undefined}
          autoComplete="off"
          disabled={disabled}
          value={open ? query : textoSeleccion}
          placeholder={textoSeleccion === "" ? placeholder : undefined}
          onChange={(e) => {
            setQuery(e.target.value);
            if (!open) setOpen(true);
          }}
          onFocus={abrir}
          // También al hacer clic: tras elegir una opción el foco se queda en el input, así que sin
          // esto un segundo clic no reabría la lista (el `focus` ya no vuelve a dispararse).
          onClick={abrir}
          onKeyDown={alTeclear}
          className="h-10 w-full rounded-[10px] border bg-white pl-9 pr-14 text-xs font-medium text-[#162744] outline-none focus:border-[#557EFF] disabled:opacity-60 dark:bg-[#0B0F14] dark:text-white"
        />

        <div className="absolute right-2 top-1/2 flex -translate-y-1/2 items-center gap-0.5">
          {value !== "" && defaultLabel !== undefined && !disabled && (
            <button
              type="button"
              onClick={() => {
                onChange("");
                cerrar();
                inputRef.current?.focus();
              }}
              // Nombre genérico a propósito: si repitiera la etiqueta del campo, habría tres
              // elementos anunciando lo mismo (input + limpiar + abrir) y `getByLabelText` dejaría
              // de identificar el control.
              aria-label="Quitar selección"
              className="rounded p-1 opacity-50 transition hover:opacity-100 focus-visible:outline focus-visible:outline-2 focus-visible:outline-offset-1"
            >
              <X className="h-3.5 w-3.5" aria-hidden="true" />
            </button>
          )}
          <button
            type="button"
            tabIndex={-1}
            disabled={disabled}
            onClick={() => {
              if (open) cerrar();
              else abrir();
              inputRef.current?.focus();
            }}
            // Afordancia de ratón: fuera del árbol de accesibilidad. Con teclado la lista se abre
            // desde el propio input (↓ o tecleando), así que este botón no aporta nada y solo
            // duplicaría el nombre del campo.
            aria-hidden="true"
            className="rounded p-1 opacity-50 transition hover:opacity-100 disabled:opacity-30"
          >
            <ChevronDown
              className={`h-3.5 w-3.5 transition-transform ${open ? "rotate-180" : ""}`}
              aria-hidden="true"
            />
          </button>
        </div>

        {open && (
          <ul
            id={listId}
            role="listbox"
            aria-label={label}
            className="absolute z-50 mt-1 max-h-64 w-full overflow-y-auto rounded-xl border bg-white py-1 shadow-lg dark:bg-[#0B0F14]"
          >
            {filtradas.length === 0 && (
              <li className="px-3 py-2 text-xs opacity-60">Sin coincidencias</li>
            )}
            {filtradas.map((o, i) => {
              const activa = i === indiceActivo;
              const elegida = o.value === value;
              return (
                <li
                  key={o.value || "__default__"}
                  id={`${baseId}-opt-${i}`}
                  role="option"
                  aria-selected={elegida}
                  onMouseEnter={() => setActiveIndex(i)}
                  onMouseDown={(e) => e.preventDefault()} // no robar el foco del input
                  onClick={() => elegir(o)}
                  className={`flex cursor-pointer items-center justify-between gap-2 px-3 py-2 text-xs ${
                    activa ? "bg-[#557EFF]/10" : ""
                  }`}
                >
                  <span className="min-w-0">
                    <span className="block truncate text-[#162744] dark:text-white">{o.label}</span>
                    {o.hint && <span className="block truncate text-[10px] opacity-60">{o.hint}</span>}
                  </span>
                  {elegida && (
                    <Check className="h-3.5 w-3.5 shrink-0" style={{ color: "#557EFF" }} aria-hidden="true" />
                  )}
                </li>
              );
            })}
          </ul>
        )}
      </div>

      {/* Anuncio del filtrado para lectores de pantalla (el listado visual ya lo comunica en pantalla). */}
      <span className="sr-only" role="status" aria-live="polite">
        {open ? `${filtradas.length} opciones disponibles` : ""}
      </span>
    </div>
  );
}
