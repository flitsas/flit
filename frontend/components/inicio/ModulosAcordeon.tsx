"use client";

import { useState } from "react";
import Link from "next/link";
import { useSearchParams } from "next/navigation";
import { ChevronUp } from "lucide-react";
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

  return (
    // Tarjeta «Módulos» del prototipo (`.card` con `.acordeones`). HU #14481 (referencia de Samuel): 3 columnas en
    // escritorio, 2 en tableta y 1 en celular. Son columnas CSS (no filas de rejilla): abrir un grupo solo empuja los
    // de su columna, como antes.
    <section
      aria-labelledby="inicio-modulos-titulo"
      className="flex flex-col gap-4 rounded-[20px] border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] p-5 shadow-[var(--shadow-card)] md:p-6"
    >
      <div>
        <h2 id="inicio-modulos-titulo" className="text-base font-semibold text-[color:var(--di-heading)]">
          Módulos
        </h2>
        <p className="mt-0.5 text-xs text-[color:var(--di-text-tertiary)]">Todo lo que puedes hacer en la plataforma, agrupado como en el menú.</p>
      </div>
      <div className="gap-x-4 sm:columns-2 lg:columns-3 [&>*]:mb-3 [&>*]:break-inside-avoid">
        {groups.map((g) => (
          <Grupo key={g.id} grupo={g} abierto={abierto === g.id} onAlternar={() => alternar(g.id)} conteos={conteos} />
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

/** Ilustración del grupo o del módulo (la del dock, HU #14479); sin ella, el ícono de línea en un círculo cielo. */
function Glifo({ entry, size }: { entry: { icon: DockEntry["icon"]; ilustracion?: string }; size: "grupo" | "modulo" }) {
  const cls = size === "grupo" ? "h-9 w-9 max-sm:h-8 max-sm:w-8" : "h-8 w-8";
  if (entry.ilustracion) {
    // eslint-disable-next-line @next/next/no-img-element
    return <img src={entry.ilustracion} alt="" aria-hidden="true" className={`${cls} shrink-0`} />;
  }
  const Icon = entry.icon;
  return (
    <span aria-hidden="true" className={`${cls} grid shrink-0 place-items-center rounded-full bg-[var(--di-sky)] text-[color:var(--di-accent)]`}>
      <Icon className="h-[55%] w-[55%]" aria-hidden="true" />
    </span>
  );
}

function Grupo({ grupo, abierto, onAlternar, conteos }: { grupo: DockGroup; abierto: boolean; onAlternar: () => void; conteos: ConteosModulo }) {
  const lista = filas(grupo);
  const conCifra = lista.map((f) => conteos[f.entry.key]).filter((x): x is ConteosModulo[string] => Boolean(x));
  const total = conCifra.reduce((s, x) => s + x.total, 0);
  const panelId = `inicio-grupo-${grupo.id}`;

  // Tarjeta del grupo (referencia de Samuel): ilustración, nombre en negrita con cuántos módulos tiene debajo, a la
  // derecha lo pendiente y la flecha.
  return (
    <div className="overflow-clip rounded-xl border border-[color:var(--di-border)] bg-[var(--di-surface-elevated)] shadow-[var(--shadow-card)]">
      <h3 className="m-0">
        <button
          type="button"
          aria-expanded={abierto}
          aria-controls={panelId}
          onClick={onAlternar}
          className={`group/fila flex min-h-16 w-full items-center gap-3 px-4 py-2.5 text-left transition-colors duration-150 hover:bg-[var(--di-sky)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--di-action)] motion-reduce:transition-none max-sm:gap-2.5 max-sm:px-3 max-sm:py-2 ${
            abierto ? "border-b border-[color:var(--di-divider)] bg-[var(--di-sky)]" : ""
          }`}
        >
          <Glifo entry={grupo} size="grupo" />
          <span className="grid min-w-0 flex-1 leading-[1.3]">
            <span className="truncate font-bold text-[color:var(--di-heading)]">{grupo.label}</span>
            <span className="truncate text-xs text-[color:var(--di-text-tertiary)]">
              {lista.length} {lista.length === 1 ? "módulo" : "módulos"}
            </span>
          </span>
          {conCifra.length > 0 && (
            <span
              // Sobre el cielo de la fila abierta o con el puntero encima, la insignia pasa a blanco (como `.badge.warn`).
              className={`shrink-0 rounded-full border px-2 py-0.5 text-xs font-semibold tabular-nums ${
                abierto ? "bg-[var(--di-surface-elevated)]" : "bg-[var(--badge-pending-bg)] group-hover/fila:bg-[var(--di-surface-elevated)]"
              }`}
              style={{ color: "var(--badge-pending-fg)", borderColor: "var(--badge-pending-border)" }}
            >
              {total.toLocaleString("es-CO")} {conCifra.length === 1 ? conCifra[0].etiqueta : "con cifra"}
            </span>
          )}
          <ChevronUp
            className={`h-[18px] w-[18px] shrink-0 transition-transform duration-150 motion-reduce:transition-none ${abierto ? "text-[color:var(--di-action)]" : "rotate-180 text-[color:var(--di-text-tertiary)]"}`}
            aria-hidden="true"
          />
        </button>
      </h3>
      <div id={panelId} hidden={!abierto}>
        <ul className="m-0 list-none p-0 py-1">
          {lista.map(({ entry, sub }) => {
            const cifra = conteos[entry.key];
            const detalle = [sub, entry.descripcion].filter(Boolean).join(" · ");
            return (
              <li key={entry.key}>
                {/* `.modulo` del prototipo: ilustración de 32 px, nombre, para qué sirve y, si hay, su cifra. */}
                <Link
                  href={entry.href}
                  className="group flex min-h-13 items-center gap-3 py-2 pl-5 pr-4 no-underline transition-colors duration-150 hover:bg-[var(--di-surface-sunken)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-inset focus-visible:ring-[var(--di-action)] motion-reduce:transition-none max-sm:pl-3"
                >
                  <Glifo entry={entry} size="modulo" />
                  <span className="grid min-w-0 flex-1 leading-[1.3]">
                    <span className="truncate text-[0.8125rem] font-semibold text-[color:var(--di-heading)] group-hover:text-[color:var(--di-action)]">{entry.label}</span>
                    {detalle && <span className="truncate text-xs text-[color:var(--di-text-tertiary)]">{detalle}</span>}
                  </span>
                  {cifra && (
                    <span className="inline-grid h-6 min-w-6 shrink-0 place-items-center rounded-full bg-[var(--di-sky)] px-2 text-xs font-bold tabular-nums text-[color:var(--di-heading)] shadow-[inset_0_0_0_1.5px_var(--di-alerta-punto)]">
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
