"use client";

// Las consultas guardadas del usuario, más las de fábrica.
//
// Las de fábrica existen para que esta lista NUNCA esté vacía. Un constructor que se abre sin nada
// escrito es la forma más segura de que no se use: la gente no sabe qué preguntar hasta que ve una
// pregunta hecha, y a partir de ahí edita muy bien. Van al final y marcadas, porque son el punto de
// partida y no lo que alguien viene a buscar cuando ya tiene las suyas.
//
// Cada fila es una TARJETA y no un renglón de texto. La primera versión eran nombres sueltos sobre
// el fondo, y no se leían como algo que se pueda abrir: sin borde ni relieve, un nombre en una
// columna parece un rótulo. Lo que las vuelve pulsables es el conjunto —borde propio, cursor de
// mano, la flecha que aparece al pasar por encima y el resumen debajo—, no un color más fuerte.
//
// El resumen («2 filtros · últimos 30 días») hace además el trabajo de fondo: convierte un nombre
// que solo significaba algo el día que se guardó en algo que se reconoce meses después.

import { Bookmark, Check, ChevronRight, Lightbulb, Search, Trash2 } from "lucide-react";
import { useState } from "react";

import { RANGE_PRESETS, type SavedQuery } from "@/lib/api/queries";

/**
 * A partir de cuántas propias aparece el buscador.
 *
 * Con dos o tres consultas, leerlas todas es más rápido que escribir para filtrar: el campo solo
 * gana su sitio cuando la lista deja de caber de un vistazo.
 */
const UMBRAL_BUSCADOR = 6;

/** Alto máximo de «Mis consultas» antes de que la lista scrollee por su cuenta. */
const ALTO_MAX_PROPIAS = "max-h-[26rem]";

export function SavedQueryList({
  queries,
  activeId,
  modificada,
  porBorrar,
  onOpen,
  onPedirBorrado,
  onConfirmarBorrado,
  testIdPrefix,
}: {
  queries: SavedQuery[];
  activeId: string | null;
  /** Lo que hay en pantalla se apartó de la consulta abierta. */
  modificada: boolean;
  /** La que está esperando confirmación de borrado, si hay alguna. */
  porBorrar: SavedQuery | null;
  onOpen: (query: SavedQuery) => void;
  onPedirBorrado: (query: SavedQuery | null) => void;
  onConfirmarBorrado: (query: SavedQuery) => void;
  /** Las dos consolas usan esta lista; el prefijo las hace distinguibles en las pruebas. */
  testIdPrefix: string;
}) {
  const propias = queries.filter((q) => !q.deFabrica);
  const fabrica = queries.filter((q) => q.deFabrica);

  // El buscador filtra por nombre. Sin él, encontrar una consulta entre muchas obliga a leerlas
  // todas mientras se hace scroll — el mismo problema que el alto máximo de abajo evita, pero para
  // el ojo en vez de para la barra de scroll.
  const [busqueda, setBusqueda] = useState("");
  const term = busqueda.trim().toLowerCase();
  const propiasFiltradas = term
    ? propias.filter((q) => q.nombre.toLowerCase().includes(term))
    : propias;

  return (
    <div className="flex flex-col gap-4" data-testid={`${testIdPrefix}-guardadas`}>
      <Grupo titulo="Mis consultas" cuenta={propias.length} icono={Bookmark}>
        <div className="flex flex-col gap-2">
          {propias.length > UMBRAL_BUSCADOR && (
            <div className="relative">
              <Search
                className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#9AA5B4] dark:text-white/30"
                aria-hidden="true"
              />
              <input
                type="search"
                value={busqueda}
                onChange={(e) => setBusqueda(e.target.value)}
                placeholder="Buscar por nombre…"
                aria-label="Buscar en mis consultas"
                className="w-full rounded-lg border border-[#DFE5ED] bg-transparent py-1.5 pl-8 pr-2 text-xs outline-none focus:border-[#557EFF] dark:border-white/15"
                data-testid={`${testIdPrefix}-buscar-guardadas`}
              />
            </div>
          )}

          {propias.length === 0 ? (
            // Un hueco en blanco no explica nada. Este dice qué aparecerá aquí y de dónde sale.
            <p className="rounded-xl border border-dashed border-[#DFE5ED] px-3 py-3 text-[11px] leading-relaxed text-[#6B7280] dark:border-white/15 dark:text-white/45">
              Las consultas que guardes aparecen aquí, listas para volver a ejecutarlas con un clic.
            </p>
          ) : propiasFiltradas.length === 0 ? (
            <p className="rounded-xl border border-dashed border-[#DFE5ED] px-3 py-3 text-[11px] leading-relaxed text-[#6B7280] dark:border-white/15 dark:text-white/45">
              Ninguna consulta coincide con «{busqueda.trim()}».
            </p>
          ) : (
            // El alto máximo evita que una lista larga empuje el resto de la pantalla: a partir de
            // ahí scrollea sola, sin arrastrar los filtros ni el resultado que quedan al lado.
            <ul className={`space-y-1.5 overflow-y-auto pr-1 ${ALTO_MAX_PROPIAS}`}>
              {propiasFiltradas.map((query) => (
                <Item
                  key={query.id}
                  query={query}
                  activo={query.id === activeId}
                  modificada={modificada}
                  confirmando={porBorrar?.id === query.id}
                  onOpen={onOpen}
                  onPedirBorrado={onPedirBorrado}
                  onConfirmarBorrado={onConfirmarBorrado}
                  testIdPrefix={testIdPrefix}
                />
              ))}
            </ul>
          )}
        </div>
      </Grupo>

      {fabrica.length > 0 && (
        <Grupo
          titulo="Para empezar"
          cuenta={fabrica.length}
          nota="Ábrelas y cámbialas a tu gusto."
          icono={Lightbulb}
          separado
        >
          <ul className="space-y-1.5">
            {fabrica.map((query) => (
              <Item
                key={query.id}
                query={query}
                activo={query.id === activeId}
                modificada={modificada}
                confirmando={false}
                onOpen={onOpen}
                onPedirBorrado={onPedirBorrado}
                onConfirmarBorrado={onConfirmarBorrado}
                testIdPrefix={testIdPrefix}
              />
            ))}
          </ul>
        </Grupo>
      )}
    </div>
  );
}

/**
 * Un bloque de la lista con su título.
 *
 * El título vive dentro de una banda con fondo propio, no solo negrita sobre el fondo general —así
 * el ojo separa los dos grupos sin tener que leer las palabras primero. Sin eso, «Mis consultas» y
 * «Para empezar» se leían como una sola lista larga con dos rótulos sueltos por el medio, y la
 * diferencia entre ambas importa: unas son suyas y se borran, las otras vienen dadas.
 */
function Grupo({
  titulo,
  cuenta,
  nota,
  icono: Icono,
  separado,
  children,
}: {
  titulo: string;
  cuenta: number;
  nota?: string;
  /** Distingue el grupo de un vistazo, sin depender solo del texto del título. */
  icono: typeof Bookmark;
  /** Traza la línea de separación con el grupo anterior. */
  separado?: boolean;
  children: React.ReactNode;
}) {
  return (
    <div className={separado ? "border-t border-[#DFE5ED] pt-4 dark:border-white/10" : undefined}>
      <div className="mb-2 flex items-center gap-1.5 rounded-lg bg-[#F5F7FA] px-2 py-1.5 dark:bg-white/[0.06]">
        <Icono className="h-3.5 w-3.5 shrink-0 text-[#557EFF] dark:text-[#9DB5FF]" aria-hidden="true" />
        <p className="text-[11px] font-bold uppercase tracking-wider text-[#162744] dark:text-white/75">
          {titulo}
        </p>
        <span className="ml-auto rounded-full bg-white px-1.5 py-px text-[10px] font-semibold tabular-nums text-[#6B7280] dark:bg-white/10 dark:text-white/50">
          {cuenta}
        </span>
      </div>
      {nota && (
        <p className="mb-1.5 px-0.5 text-[10px] leading-snug text-[#9AA5B4] dark:text-white/30">
          {nota}
        </p>
      )}
      {children}
    </div>
  );
}

function Item({
  query,
  activo,
  modificada,
  confirmando,
  onOpen,
  onPedirBorrado,
  onConfirmarBorrado,
  testIdPrefix,
}: {
  query: SavedQuery;
  activo: boolean;
  modificada: boolean;
  confirmando: boolean;
  onOpen: (query: SavedQuery) => void;
  onPedirBorrado: (query: SavedQuery | null) => void;
  onConfirmarBorrado: (query: SavedQuery) => void;
  testIdPrefix: string;
}) {
  // Confirmar en la propia tarjeta y no en un diálogo del navegador: se ve QUÉ se va a borrar
  // mientras se decide, que es justo lo que un cuadro modal tapa. Y se dice el nombre completo en
  // vez de «¿está seguro?», porque lo que hay que confirmar es cuál, no si.
  if (confirmando) {
    return (
      <li
        className="rounded-xl border border-[#C0392B]/40 bg-[#C0392B]/5 p-2.5"
        data-testid={`${testIdPrefix}-borrar-${query.id}`}
      >
        <p className="mb-2 text-[11px] leading-snug text-[#0B1F33] dark:text-white/80">
          Se borrará <span className="font-semibold">«{query.nombre}»</span>.
        </p>
        <div className="flex gap-1.5">
          <button
            type="button"
            onClick={() => onConfirmarBorrado(query)}
            className="flex-1 rounded-lg bg-[#C0392B] px-2 py-1.5 text-[11px] font-semibold text-white hover:bg-[#A63325]"
            data-testid={`${testIdPrefix}-borrar-confirmar-${query.id}`}
          >
            Borrar
          </button>
          <button
            type="button"
            autoFocus
            onClick={() => onPedirBorrado(null)}
            className="flex-1 rounded-lg border border-[#DFE5ED] bg-white px-2 py-1.5 text-[11px] font-semibold text-[#6B7280] hover:text-[#0B1F33] dark:border-white/15 dark:bg-transparent dark:text-white/60 dark:hover:text-white"
          >
            Cancelar
          </button>
        </div>
      </li>
    );
  }

  return (
    <li className="group relative">
      <button
        type="button"
        onClick={() => onOpen(query)}
        aria-current={activo ? "true" : undefined}
        title={query.descripcion ?? undefined}
        className={`w-full cursor-pointer rounded-xl border py-2 pl-2.5 pr-7 text-left transition ${
          activo
            ? "border-[#557EFF] bg-[#557EFF]/[0.07] shadow-[inset_3px_0_0_0_#557EFF]"
            : "border-[#DFE5ED] hover:border-[#557EFF]/50 hover:bg-[#F5F7FA] dark:border-white/10 dark:hover:border-[#557EFF]/40 dark:hover:bg-white/5"
        }`}
        data-testid={`${testIdPrefix}-guardada-${query.id}`}
      >
        <span className="flex items-start gap-1.5">
          {/* Dos líneas antes de cortar, no una. El nombre es lo único que identifica la consulta;
              «Con prenda y sin licencia …» obliga a abrirla para saber cuál es. */}
          <span
            className={`min-w-0 flex-1 text-xs leading-snug line-clamp-2 ${
              activo
                ? "font-semibold text-[#3355CC] dark:text-[#9DB5FF]"
                : "font-medium text-[#0B1F33] dark:text-white/85"
            }`}
          >
            {query.nombre}
          </span>
          {activo && !modificada && (
            <Check className="mt-[3px] h-3 w-3 shrink-0 text-[#557EFF]" aria-hidden="true" />
          )}
        </span>

        <span className="mt-0.5 flex items-center gap-1 text-[10px] leading-snug text-[#6B7280] dark:text-white/40">
          {resumen(query)}
        </span>

        {/* Decir que lo de pantalla ya no es lo guardado evita el malentendido clásico: creer que
            se está mirando la consulta de siempre cuando alguien tocó un filtro. */}
        {activo && modificada && (
          <span className="mt-1 inline-block rounded-full bg-[#F2C14E]/20 px-1.5 py-px text-[10px] font-semibold text-[#8A6100] dark:text-[#F2C14E]">
            modificada
          </span>
        )}

        {/* La flecha solo aparece al apuntar: basta para decir «esto se abre» sin competir con el
            nombre, que es lo que la persona viene a leer. */}
        <ChevronRight
          className="pointer-events-none absolute right-1.5 top-2.5 h-3.5 w-3.5 text-[#9AA5B4] opacity-0 transition-opacity group-hover:opacity-100 dark:text-white/30"
          aria-hidden="true"
        />
      </button>

      {!query.deFabrica && (
        // El borrar aparece al apuntar o al enfocar por teclado, y NUNCA tapa la flecha: dos
        // controles disputándose la misma esquina es cómo se borra algo sin querer.
        <button
          type="button"
          onClick={() => onPedirBorrado(query)}
          aria-label={`Borrar ${query.nombre}`}
          title={`Borrar «${query.nombre}»`}
          className="absolute bottom-1.5 right-1.5 rounded-md p-1 text-[#9AA5B4] opacity-0 transition hover:bg-[#C0392B]/10 hover:text-[#C0392B] focus-visible:opacity-100 group-hover:opacity-100 dark:text-white/30"
        >
          <Trash2 className="h-3 w-3" aria-hidden="true" />
        </button>
      )}
    </li>
  );
}

/**
 * Qué pregunta hace esta consulta, en una línea.
 *
 * Un nombre puesto por su autor —«revisión lunes»— no significa nada tres meses después, ni para
 * quien lo escribió. El resumen se saca de la definición, así que no puede desactualizarse.
 */
function resumen(query: SavedQuery): string {
  const n = query.definition.condiciones.length;
  const filtros = n === 0 ? "Sin filtros" : `${n} ${n === 1 ? "filtro" : "filtros"}`;
  const rango = RANGE_PRESETS.find((p) => p.value === query.definition.fechas.preset)?.label;
  return rango ? `${filtros} · ${rango.toLowerCase()}` : filtros;
}
