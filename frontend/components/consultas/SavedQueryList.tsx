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

import { Bookmark, CalendarPlus, Check, ChevronRight, Lightbulb, Search, Trash2 } from "lucide-react";
import { useMemo, useState } from "react";

import { Modal } from "@/components/atom/Modal";
import { RANGE_PRESETS, type SavedQuery } from "@/lib/api/queries";

/**
 * Cuántos ítems se muestran siempre en la barra lateral, sin scroll ni clics — el mismo tope para
 * «Mis consultas» y «Para empezar», porque el problema que resuelve (una lista que puede crecer sin
 * límite empujando el resto de la pantalla) es el mismo en los dos grupos, hoy uno y mañana el otro.
 *
 * Con dos o tres no hace falta separar «visibles» de «todas»: se ven todas de un vistazo. Por
 * encima de este número, el grupo solo enseña estas y deja el resto detrás de «Ver todas», en vez
 * de crecer indefinidamente o esconderse entero.
 */
const VISIBLES_SIN_DESBORDE = 5;

export function SavedQueryList({
  queries,
  activeId,
  modificada,
  porBorrar,
  onOpen,
  onPedirBorrado,
  onConfirmarBorrado,
  onSchedule,
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
  /** Reportes 2.0 (HU-D) — "Programar este informe". Sin esto (Consultas del organismo) el botón
   * no se renderiza. */
  onSchedule?: (query: SavedQuery) => void;
  /** Las dos consolas usan esta lista; el prefijo las hace distinguibles en las pruebas. */
  testIdPrefix: string;
}) {
  const propias = queries.filter((q) => !q.deFabrica);
  const fabrica = queries.filter((q) => q.deFabrica);

  return (
    <div className="flex flex-col gap-4" data-testid={`${testIdPrefix}-guardadas`}>
      <Grupo titulo="Mis consultas" cuenta={propias.length} icono={Bookmark}>
        {propias.length === 0 ? (
          // Un hueco en blanco no explica nada. Este dice qué aparecerá aquí y de dónde sale.
          <p className="rounded-xl border border-dashed border-[#DFE5ED] px-3 py-3 text-[11px] leading-relaxed text-[#6B7280] dark:border-white/15 dark:text-white/45">
            Las consultas que guardes aparecen aquí, listas para volver a ejecutarlas con un clic.
          </p>
        ) : (
          <ConsultasConDesborde
            items={propias}
            // `updatedAt` gana porque abrir y volver a guardar una consulta vieja es señal de que
            // sigue en uso, más que la fecha en la que se creó una vez.
            ordenarPorRecencia
            activeId={activeId}
            modificada={modificada}
            porBorrar={porBorrar}
            onOpen={onOpen}
            onPedirBorrado={onPedirBorrado}
            onConfirmarBorrado={onConfirmarBorrado}
            onSchedule={onSchedule}
            testIdPrefix={testIdPrefix}
            grupoId="guardadas"
            modalTitulo="Mis consultas"
            modalIcono={Bookmark}
          />
        )}
      </Grupo>

      {fabrica.length > 0 && (
        <Grupo
          titulo="Para empezar"
          cuenta={fabrica.length}
          nota="Ábrelas y cámbialas a tu gusto."
          icono={Lightbulb}
          separado
        >
          <ConsultasConDesborde
            items={fabrica}
            // El orden de fábrica ya viene curado desde el backend: no hay «recencia» que valga
            // más que ese orden, así que se conserva tal cual llega.
            ordenarPorRecencia={false}
            activeId={activeId}
            modificada={modificada}
            porBorrar={porBorrar}
            onOpen={onOpen}
            onPedirBorrado={onPedirBorrado}
            onConfirmarBorrado={onConfirmarBorrado}
            onSchedule={onSchedule}
            testIdPrefix={testIdPrefix}
            grupoId="fabrica"
            modalTitulo="Para empezar"
            modalIcono={Lightbulb}
          />
        </Grupo>
      )}
    </div>
  );
}

/**
 * Un grupo de consultas que se contiene solo: unas pocas siempre visibles y, si sobran, un «Ver
 * todas» que abre el resto en un modal con buscador.
 *
 * Compartido entre «Mis consultas» y «Para empezar» porque el problema es el mismo en ambos —hoy
 * uno de los dos grupos crece, mañana puede ser el otro— y repetir la lógica de corte + modal por
 * cada uno sería la forma más rápida de que un ajuste futuro se aplique a uno y se olvide en el
 * otro.
 */
function ConsultasConDesborde({
  items,
  ordenarPorRecencia,
  activeId,
  modificada,
  porBorrar,
  onOpen,
  onPedirBorrado,
  onConfirmarBorrado,
  onSchedule,
  testIdPrefix,
  grupoId,
  modalTitulo,
  modalIcono,
}: {
  items: SavedQuery[];
  ordenarPorRecencia: boolean;
  activeId: string | null;
  modificada: boolean;
  porBorrar: SavedQuery | null;
  onOpen: (query: SavedQuery) => void;
  onPedirBorrado: (query: SavedQuery | null) => void;
  onConfirmarBorrado: (query: SavedQuery) => void;
  onSchedule?: (query: SavedQuery) => void;
  testIdPrefix: string;
  /** Distingue los data-testid del «Ver todas» y el buscador de este grupo del otro grupo. */
  grupoId: string;
  modalTitulo: string;
  modalIcono: typeof Bookmark;
}) {
  const [modalAbierto, setModalAbierto] = useState(false);

  const ordenados = useMemo(
    () =>
      ordenarPorRecencia
        ? [...items].sort(
            (a, b) =>
              new Date(b.updatedAt ?? b.createdAt).getTime() -
              new Date(a.updatedAt ?? a.createdAt).getTime(),
          )
        : items,
    [items, ordenarPorRecencia],
  );
  const visibles = ordenados.slice(0, VISIBLES_SIN_DESBORDE);
  const desborda = items.length > VISIBLES_SIN_DESBORDE;

  return (
    <div className="flex flex-col gap-2">
      <ul className="space-y-1.5">
        {visibles.map((query) => (
          <Item
            key={query.id}
            query={query}
            activo={query.id === activeId}
            modificada={modificada}
            confirmando={porBorrar?.id === query.id}
            onOpen={onOpen}
            onPedirBorrado={onPedirBorrado}
            onConfirmarBorrado={onConfirmarBorrado}
            onSchedule={onSchedule}
            testIdPrefix={testIdPrefix}
          />
        ))}
      </ul>

      {desborda && (
        <button
          type="button"
          onClick={() => setModalAbierto(true)}
          className="w-full rounded-lg border border-[#DFE5ED] px-2 py-1.5 text-[11px] font-semibold text-[#6B7280] transition hover:border-[#557EFF]/50 hover:text-[#557EFF] dark:border-white/15 dark:text-white/50"
          data-testid={`${testIdPrefix}-ver-todas-${grupoId}`}
        >
          Ver todas ({items.length})
        </button>
      )}

      {desborda && (
        <Modal
          open={modalAbierto}
          onClose={() => setModalAbierto(false)}
          title={modalTitulo}
          icon={modalIcono}
          size="sm"
        >
          <BuscadorDeConsultas
            items={ordenados}
            activeId={activeId}
            modificada={modificada}
            porBorrar={porBorrar}
            onOpen={(query) => {
              onOpen(query);
              setModalAbierto(false);
            }}
            onPedirBorrado={onPedirBorrado}
            onConfirmarBorrado={onConfirmarBorrado}
            onSchedule={onSchedule}
            testIdPrefix={testIdPrefix}
            grupoId={grupoId}
          />
        </Modal>
      )}
    </div>
  );
}

/**
 * El cuerpo del modal: buscador + la lista completa del grupo.
 *
 * Vive en un modal y no en un popover porque a esta lo trae quien ya sabe que no está entre las
 * visibles — viene a buscar algo puntual, no a ojear. Un modal centrado le da todo el ancho para
 * leer nombres largos y resúmenes sin el apretón de una barra lateral de 16rem.
 */
function BuscadorDeConsultas({
  items,
  activeId,
  modificada,
  porBorrar,
  onOpen,
  onPedirBorrado,
  onConfirmarBorrado,
  onSchedule,
  testIdPrefix,
  grupoId,
}: {
  items: SavedQuery[];
  activeId: string | null;
  modificada: boolean;
  porBorrar: SavedQuery | null;
  onOpen: (query: SavedQuery) => void;
  onPedirBorrado: (query: SavedQuery | null) => void;
  onConfirmarBorrado: (query: SavedQuery) => void;
  onSchedule?: (query: SavedQuery) => void;
  testIdPrefix: string;
  grupoId: string;
}) {
  const [busqueda, setBusqueda] = useState("");
  const term = busqueda.trim().toLowerCase();
  const filtradas = term ? items.filter((q) => q.nombre.toLowerCase().includes(term)) : items;

  return (
    <div className="flex flex-col gap-3">
      <div className="relative">
        <Search
          className="pointer-events-none absolute left-2.5 top-1/2 h-3.5 w-3.5 -translate-y-1/2 text-[#9AA5B4] dark:text-white/30"
          aria-hidden="true"
        />
        <input
          autoFocus
          type="search"
          value={busqueda}
          onChange={(e) => setBusqueda(e.target.value)}
          placeholder="Buscar por nombre…"
          aria-label="Buscar consulta"
          className="w-full rounded-lg border border-[#DFE5ED] bg-transparent py-2 pl-8 pr-2 text-xs outline-none focus:border-[#557EFF] dark:border-white/15"
          data-testid={`${testIdPrefix}-buscar-${grupoId}`}
        />
      </div>

      {filtradas.length === 0 ? (
        <p className="rounded-xl border border-dashed border-[#DFE5ED] px-3 py-3 text-[11px] leading-relaxed text-[#6B7280] dark:border-white/15 dark:text-white/45">
          Ninguna consulta coincide con «{busqueda.trim()}».
        </p>
      ) : (
        <ul className="space-y-1.5">
          {filtradas.map((query) => (
            <Item
              key={query.id}
              query={query}
              activo={query.id === activeId}
              modificada={modificada}
              confirmando={porBorrar?.id === query.id}
              onOpen={onOpen}
              onPedirBorrado={onPedirBorrado}
              onConfirmarBorrado={onConfirmarBorrado}
              onSchedule={onSchedule}
              testIdPrefix={testIdPrefix}
            />
          ))}
        </ul>
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
  onSchedule,
  testIdPrefix,
}: {
  query: SavedQuery;
  activo: boolean;
  modificada: boolean;
  confirmando: boolean;
  onOpen: (query: SavedQuery) => void;
  onPedirBorrado: (query: SavedQuery | null) => void;
  onConfirmarBorrado: (query: SavedQuery) => void;
  onSchedule?: (query: SavedQuery) => void;
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

      {/* Programar y borrar comparten la esquina inferior derecha, en un grupo — así ninguno tapa
          la flecha ni al otro, y los dos aparecen juntos al apuntar. */}
      {(onSchedule ?? !query.deFabrica) && (
        <span className="absolute bottom-1.5 right-1.5 flex items-center gap-0.5 opacity-0 transition focus-within:opacity-100 group-hover:opacity-100">
          {onSchedule && (
            <button
              type="button"
              onClick={() => onSchedule(query)}
              aria-label={`Programar informe de ${query.nombre}`}
              title={`Programar informe de «${query.nombre}»`}
              className="rounded-md p-1 text-[#9AA5B4] transition hover:bg-[#557EFF]/10 hover:text-[#557EFF] dark:text-white/30"
            >
              <CalendarPlus className="h-3 w-3" aria-hidden="true" />
            </button>
          )}
          {!query.deFabrica && (
            <button
              type="button"
              onClick={() => onPedirBorrado(query)}
              aria-label={`Borrar ${query.nombre}`}
              title={`Borrar «${query.nombre}»`}
              className="rounded-md p-1 text-[#9AA5B4] transition hover:bg-[#C0392B]/10 hover:text-[#C0392B] dark:text-white/30"
            >
              <Trash2 className="h-3 w-3" aria-hidden="true" />
            </button>
          )}
        </span>
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
