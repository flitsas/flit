"use client";

import { useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ChevronDown } from "lucide-react";
import type { DockEntry, DockGroup } from "@flit/shell/nav";
import { useShellNav } from "@flit/shell/nav-context";

/** Parámetro de la dirección con el grupo abierto. */
export const PARAM_GRUPO = "grupo";

/** Cifra propia de una entrada del dock, cuando hay un endpoint que la da (hoy solo Trámites, con estado-counts). */
export type ConteosModulo = Record<string, { total: number; etiqueta: string }>;

/**
 * HU #14481 (AC1, AC3) — los módulos del rol agrupados como en el dock, en acordeón: uno abierto a la vez y el abierto
 * en la dirección (`?grupo=<id del grupo>`). Los grupos y sus módulos son los del dock ya filtrado por permiso. Un
 * grupo solo muestra contador si alguno de sus módulos trae cifra en `conteos`; los demás no muestran ninguno.
 */
export function ModulosAcordeon({ conteos = {} }: { conteos?: ConteosModulo }) {
  const { groups } = useShellNav();
  const params = useSearchParams();
  const [abierto, setAbierto] = useState<string | null>(() => params?.get(PARAM_GRUPO) ?? null);

  if (groups.length === 0) return null;

  const alternar = (id: string) => {
    const siguiente = abierto === id ? null : id;
    setAbierto(siguiente);
    // Sin recargar la página ni tocar los demás parámetros (p. ej. `?m=`): Next sincroniza useSearchParams.
    const url = new URL(window.location.href);
    if (siguiente) url.searchParams.set(PARAM_GRUPO, siguiente);
    else url.searchParams.delete(PARAM_GRUPO);
    window.history.replaceState(window.history.state, "", `${url.pathname}${url.search}${url.hash}`);
  };

  const mitad = Math.ceil(groups.length / 2);
  const columnas = [groups.slice(0, mitad), groups.slice(mitad)].filter((c) => c.length > 0);

  return (
    <section aria-labelledby="inicio-modulos-titulo" className="flex flex-col gap-3">
      <div>
        <h2 id="inicio-modulos-titulo" className="text-base font-semibold text-[var(--foreground)]">
          Módulos
        </h2>
        <p className="mt-0.5 text-xs text-[var(--muted-foreground)]">Todo lo que puedes hacer en la plataforma, agrupado como en el menú.</p>
      </div>
      <div className="grid items-start gap-3 md:grid-cols-2">
        {columnas.map((col, c) => (
          <div key={c} className="grid min-w-0 gap-3">
            {col.map((g) => (
              <Grupo key={g.id} grupo={g} abierto={abierto === g.id} onAlternar={() => alternar(g.id)} conteos={conteos} />
            ))}
          </div>
        ))}
      </div>
    </section>
  );
}

/** Módulos navegables del grupo; un contenedor (`href: ""`) aporta sus hijos con su nombre como subtítulo. */
function filas(grupo: DockGroup): { entry: DockEntry; sub?: string }[] {
  return grupo.items.flatMap((e) => [
    ...(e.href ? [{ entry: e }] : []),
    ...(e.children ?? []).filter((c) => c.href).map((c) => ({ entry: c, sub: e.label })),
  ]);
}

function Grupo({ grupo, abierto, onAlternar, conteos }: { grupo: DockGroup; abierto: boolean; onAlternar: () => void; conteos: ConteosModulo }) {
  const lista = filas(grupo);
  const conCifra = lista.map((f) => conteos[f.entry.key]).filter((x): x is ConteosModulo[string] => Boolean(x));
  const total = conCifra.reduce((s, x) => s + x.total, 0);
  const panelId = `inicio-grupo-${grupo.id}`;
  const Icono = grupo.icon;

  return (
    <div className="overflow-hidden rounded-[var(--radius-xl)] border border-[var(--border)] bg-[var(--card)] text-[var(--card-foreground)] shadow-[var(--shadow-card)]">
      <h3 className="m-0">
        <button
          type="button"
          aria-expanded={abierto}
          aria-controls={panelId}
          onClick={onAlternar}
          className={`flex min-h-16 w-full items-center gap-3 px-4 py-2.5 text-left transition-colors hover:bg-[var(--accent)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--ring)] ${
            abierto ? "border-b border-[var(--border)] bg-[var(--accent)]" : ""
          }`}
        >
          <span className="grid h-8 min-w-8 place-items-center rounded-full bg-[var(--nav-app-bg)] px-2 text-sm font-bold tabular-nums text-[var(--flit-brand-ink)] max-sm:hidden">
            {lista.length}
            <span className="sr-only">{lista.length === 1 ? " módulo" : " módulos"}</span>
          </span>
          <Icono className="h-6 w-6 shrink-0 text-[var(--flit-brand-ink)]" aria-hidden="true" />
          <span className="min-w-0 flex-1 truncate font-semibold text-[var(--foreground)]">{grupo.label}</span>
          {conCifra.length > 0 && (
            <span
              className="shrink-0 rounded-full border px-2 py-0.5 text-xs font-semibold tabular-nums"
              style={{ background: "var(--badge-pending-bg)", color: "var(--badge-pending-fg)", borderColor: "var(--badge-pending-border)" }}
            >
              {total.toLocaleString("es-CO")} {conCifra.length === 1 ? conCifra[0].etiqueta : "con cifra"}
            </span>
          )}
          <ChevronDown
            className={`h-4 w-4 shrink-0 transition-transform motion-reduce:transition-none ${abierto ? "rotate-180 text-[var(--flit-brand-ink)]" : "text-[var(--muted-foreground)]"}`}
            aria-hidden="true"
          />
        </button>
      </h3>
      <div id={panelId} hidden={!abierto}>
        <ul className="m-0 list-none divide-y divide-[var(--border)] p-0 py-1">
          {lista.map(({ entry, sub }) => {
            const cifra = conteos[entry.key];
            const Icon = entry.icon;
            return (
              <li key={entry.key}>
                <Link
                  href={entry.href}
                  className="group flex min-h-13 items-center gap-3 px-4 py-2 pl-5 no-underline transition-colors hover:bg-[var(--muted)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--ring)]"
                >
                  <Icon className="h-5 w-5 shrink-0 text-[var(--muted-foreground)]" aria-hidden="true" />
                  <span className="grid min-w-0 flex-1 leading-tight">
                    <span className="truncate text-sm font-semibold text-[var(--foreground)] group-hover:text-[var(--flit-brand-ink)]">{entry.label}</span>
                    {sub && <span className="truncate text-xs text-[var(--muted-foreground)]">{sub}</span>}
                  </span>
                  {cifra && (
                    <span className="shrink-0 text-xs font-semibold tabular-nums text-[var(--muted-foreground)]">
                      {cifra.total.toLocaleString("es-CO")} {cifra.etiqueta}
                    </span>
                  )}
                </Link>
              </li>
            );
          })}
        </ul>
      </div>
    </div>
  );
}
