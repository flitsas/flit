"use client";

import { useCallback, useEffect, useId, useMemo, useRef, useState, type KeyboardEvent } from "react";
import Link from "next/link";
import { CornerDownLeft, Hash, Search } from "lucide-react";
import type { DockGroup } from "./nav";
import { buildSearchResults, SEARCH_GROUP_LABEL, type PlateSearch, type SearchAction, type SearchResult } from "./search";

export interface CommandPaletteProps {
  groups: DockGroup[];
  home?: { href: string; label: string };
  actions?: SearchAction[];
  plate?: PlateSearch;
}

const isExternal = (href: string) => /^https?:\/\//.test(href);

/** ¿El atajo de teclado que abre el buscador? Ctrl K en Windows/Linux y ⌘K en Mac. */
export function isSearchShortcut(e: { key: string; ctrlKey: boolean; metaKey: boolean; altKey?: boolean; shiftKey?: boolean }): boolean {
  return (e.ctrlKey || e.metaKey) && !e.altKey && !e.shiftKey && e.key.toLowerCase() === "k";
}

/**
 * Buscador Ctrl K (HU #14480): botón en la barra y diálogo con un combobox accesible. El foco queda siempre en el
 * campo; las flechas mueven la opción activa (`aria-activedescendant`), Enter la abre y Escape cierra y devuelve el
 * foco a donde estaba antes de abrir.
 */
export function CommandPalette({ groups, home, actions, plate }: CommandPaletteProps) {
  const [open, setOpen] = useState(false);
  const [query, setQuery] = useState("");
  const [active, setActive] = useState(0);
  const returnFocus = useRef<HTMLElement | null>(null);
  const inputRef = useRef<HTMLInputElement>(null);
  const baseId = useId();
  const listId = `${baseId}-lista`;
  const optionId = (i: number) => `${baseId}-op-${i}`;

  const results = useMemo(() => buildSearchResults(query, groups, { home, actions, plate }), [query, groups, home, actions, plate]);

  const show = useCallback(() => {
    returnFocus.current = document.activeElement instanceof HTMLElement ? document.activeElement : null;
    setQuery("");
    setActive(0);
    setOpen(true);
  }, []);

  const close = useCallback(() => {
    setOpen(false);
    const target = returnFocus.current;
    returnFocus.current = null;
    // Después del render que quita el diálogo, para que el foco no se pierda con él.
    if (target && target.isConnected) requestAnimationFrame(() => target.focus());
  }, []);

  useEffect(() => {
    const onKey = (e: globalThis.KeyboardEvent) => {
      if (!isSearchShortcut(e)) return;
      e.preventDefault();
      if (open) close();
      else show();
    };
    document.addEventListener("keydown", onKey);
    return () => document.removeEventListener("keydown", onKey);
  }, [open, show, close]);

  useEffect(() => {
    if (open) inputRef.current?.focus();
  }, [open]);

  useEffect(() => {
    if (!open) return;
    document.getElementById(`${baseId}-op-${active}`)?.scrollIntoView?.({ block: "nearest" });
  }, [active, open, baseId]);

  const choose = (i: number) => {
    const el = document.getElementById(optionId(i));
    // El enlace navega solo (next/link dentro del producto, <a> hacia otro producto) y su onClick cierra el diálogo
    // sin devolver el foco, porque la página cambia.
    el?.click();
  };

  const onInputKey = (e: KeyboardEvent<HTMLInputElement>) => {
    if (e.key === "ArrowDown" || e.key === "ArrowUp") {
      e.preventDefault();
      if (!results.length) return;
      const step = e.key === "ArrowDown" ? 1 : results.length - 1;
      setActive((a) => (a + step) % results.length);
    } else if (e.key === "Home" && results.length) {
      e.preventDefault();
      setActive(0);
    } else if (e.key === "End" && results.length) {
      e.preventDefault();
      setActive(results.length - 1);
    } else if (e.key === "Enter") {
      e.preventDefault();
      if (results.length) choose(Math.min(active, results.length - 1));
    } else if (e.key === "Escape") {
      e.preventDefault();
      close();
    } else if (e.key === "Tab") {
      // El diálogo solo tiene el campo: el foco no sale de él mientras está abierto.
      e.preventDefault();
    }
  };

  // Agrupa en orden: los resultados ya vienen contiguos por grupo.
  const sections: { group: SearchResult["group"]; items: { r: SearchResult; i: number }[] }[] = [];
  results.forEach((r, i) => {
    const last = sections[sections.length - 1];
    if (last && last.group === r.group) last.items.push({ r, i });
    else sections.push({ group: r.group, items: [{ r, i }] });
  });
  const activeIndex = Math.min(active, Math.max(results.length - 1, 0));

  const optionClass = (selected: boolean) =>
    `flex min-h-12 items-center gap-3 rounded-[var(--radius-lg)] px-3 py-2 text-left no-underline transition-colors ${
      selected ? "bg-[var(--accent)]" : "hover:bg-[var(--accent)]"
    }`;

  return (
    <>
      <button
        type="button"
        onClick={show}
        aria-haspopup="dialog"
        aria-keyshortcuts="Control+K Meta+K"
        aria-label="Buscar (Ctrl K)"
        className="suite-buscar"
      >
        <Search className="h-5 w-5 shrink-0" aria-hidden="true" />
        <span>Buscar placa, trámite o módulo</span>
        <kbd aria-hidden="true">Ctrl K</kbd>
      </button>

      {open && (
        <div className="fixed inset-0 z-[60] flex items-start justify-center bg-[var(--overlay)] px-4 pt-[12vh]" onMouseDown={(e) => e.target === e.currentTarget && close()}>
          <div
            role="dialog"
            aria-modal="true"
            aria-label="Buscador"
            className="flex max-h-[70vh] w-full max-w-xl flex-col overflow-hidden rounded-[var(--radius-xl)] border border-[var(--border)] bg-[var(--popover)] text-[var(--foreground)] shadow-[var(--shadow-modal)]"
          >
            <div className="flex items-center gap-3 border-b border-[var(--border)] px-4">
              <Search className="h-5 w-5 shrink-0 text-[var(--muted-foreground)]" aria-hidden="true" />
              <input
                ref={inputRef}
                type="text"
                role="combobox"
                aria-label="Buscar una sección, una acción o una placa"
                aria-expanded={results.length > 0}
                aria-controls={listId}
                aria-autocomplete="list"
                aria-activedescendant={results.length ? optionId(activeIndex) : undefined}
                placeholder="Busca una sección, una acción o una placa"
                autoComplete="off"
                spellCheck={false}
                value={query}
                onChange={(e) => {
                  setQuery(e.target.value);
                  setActive(0);
                }}
                onKeyDown={onInputKey}
                className="h-14 min-w-0 flex-1 bg-transparent text-base text-[var(--foreground)] outline-none placeholder:text-[var(--muted-foreground)]"
              />
              <kbd className="rounded-[var(--radius-sm)] border border-[var(--border)] px-1.5 font-sans text-[11px] text-[var(--muted-foreground)]" aria-hidden="true">
                Esc
              </kbd>
            </div>

            <div id={listId} role="listbox" aria-label="Resultados" className="min-h-0 flex-1 overflow-y-auto p-2">
              {sections.map((s) => {
                const labelId = `${baseId}-grupo-${s.group}`;
                return (
                  <div key={s.group} role="group" aria-labelledby={labelId} className="pb-1">
                    <p id={labelId} className="px-3 pb-1 pt-2 text-[11px] font-bold uppercase tracking-wider text-[var(--muted-foreground)]">
                      {SEARCH_GROUP_LABEL[s.group]}
                    </p>
                    {s.items.map(({ r, i }) => {
                      const selected = i === activeIndex;
                      const Icon = r.icon ?? (r.group === "placas" ? Hash : CornerDownLeft);
                      const body = (
                        <>
                          <span className="grid h-8 w-8 shrink-0 place-items-center rounded-[var(--radius-md)] bg-[var(--nav-app-bg)] text-[var(--flit-brand-ink)]">
                            <Icon className="h-4 w-4" aria-hidden="true" />
                          </span>
                          <span className="grid min-w-0 flex-1 leading-tight">
                            <span id={`${optionId(i)}-n`} className={`truncate text-sm font-semibold ${selected ? "text-[var(--flit-brand-ink)]" : "text-[var(--foreground)]"}`}>
                              {r.label}
                            </span>
                            {r.description && <span id={`${optionId(i)}-d`} className="truncate text-xs text-[var(--muted-foreground)]">{r.description}</span>}
                          </span>
                        </>
                      );
                      const common = {
                        id: optionId(i),
                        role: "option",
                        "aria-selected": selected,
                        // Nombre = la etiqueta; la ruta en el dock se lee aparte, no pegada al nombre.
                        "aria-labelledby": `${optionId(i)}-n`,
                        "aria-describedby": r.description ? `${optionId(i)}-d` : undefined,
                        tabIndex: -1,
                        className: optionClass(selected),
                        onMouseMove: () => setActive(i),
                        onClick: () => {
                          returnFocus.current = null;
                          close();
                        },
                      };
                      return isExternal(r.href) ? (
                        <a key={r.id} href={r.href} {...common}>
                          {body}
                        </a>
                      ) : (
                        <Link key={r.id} href={r.href} {...common}>
                          {body}
                        </Link>
                      );
                    })}
                  </div>
                );
              })}
              {results.length === 0 && (
                <p className="px-3 py-8 text-center text-sm text-[var(--muted-foreground)]">
                  No encontramos nada con eso. Prueba con el nombre de una sección o una placa completa.
                </p>
              )}
            </div>
          </div>
        </div>
      )}
    </>
  );
}
