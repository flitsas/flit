"use client";

// Las consultas guardadas del usuario, más las de fábrica.
//
// Las de fábrica existen para que esta lista NUNCA esté vacía. Un constructor que se abre sin nada
// escrito es la forma más segura de que no se use: la gente no sabe qué preguntar hasta que ve una
// pregunta hecha, y a partir de ahí edita muy bien. Van al final y marcadas, porque son el punto de
// partida y no lo que alguien viene a buscar cuando ya tiene las suyas.

import type { OtSavedQuery } from "@/lib/api/ot-queries";

export function SavedQueryList({
  queries,
  activeId,
  modificada,
  porBorrar,
  onOpen,
  onPedirBorrado,
  onConfirmarBorrado,
}: {
  queries: OtSavedQuery[];
  activeId: string | null;
  /** Lo que hay en pantalla se apartó de la consulta abierta. */
  modificada: boolean;
  /** La que está esperando confirmación de borrado, si hay alguna. */
  porBorrar: OtSavedQuery | null;
  onOpen: (query: OtSavedQuery) => void;
  onPedirBorrado: (query: OtSavedQuery | null) => void;
  onConfirmarBorrado: (query: OtSavedQuery) => void;
}) {
  const propias = queries.filter((q) => !q.deFabrica);
  const fabrica = queries.filter((q) => q.deFabrica);

  return (
    <div className="flex flex-col gap-3" data-testid="ot-query-guardadas">
      <Grupo titulo="Mis consultas" vacio="Todavía no ha guardado ninguna.">
        {propias.map((query) => (
          <Item
            key={query.id}
            query={query}
            activo={query.id === activeId}
            modificada={modificada}
            confirmando={porBorrar?.id === query.id}
            onOpen={onOpen}
            onPedirBorrado={onPedirBorrado}
            onConfirmarBorrado={onConfirmarBorrado}
          />
        ))}
      </Grupo>

      <Grupo titulo="Para empezar" vacio="">
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
          />
        ))}
      </Grupo>
    </div>
  );
}

function Grupo({
  titulo,
  vacio,
  children,
}: {
  titulo: string;
  vacio: string;
  children: React.ReactNode[];
}) {
  return (
    <div>
      <p className="mb-1 px-1 text-[10px] font-semibold uppercase tracking-wide text-[#6B7280] dark:text-white/40">
        {titulo}
      </p>
      {children.length === 0 && vacio ? (
        <p className="px-1 text-[11px] text-[#6B7280] dark:text-white/40">{vacio}</p>
      ) : (
        <ul className="space-y-0.5">{children}</ul>
      )}
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
}: {
  query: OtSavedQuery;
  activo: boolean;
  modificada: boolean;
  confirmando: boolean;
  onOpen: (query: OtSavedQuery) => void;
  onPedirBorrado: (query: OtSavedQuery | null) => void;
  onConfirmarBorrado: (query: OtSavedQuery) => void;
}) {
  // Confirmar en la propia fila y no en un diálogo del navegador: se ve QUÉ se va a borrar mientras
  // se decide, que es justo lo que un cuadro modal tapa.
  if (confirmando) {
    return (
      <li className="flex items-center gap-1 rounded-lg bg-[#C0392B]/10 px-2 py-1.5 text-[11px]">
        <span className="min-w-0 flex-1 truncate">¿Borrar «{query.nombre}»?</span>
        <button
          type="button"
          onClick={() => onConfirmarBorrado(query)}
          className="font-semibold text-[#C0392B]"
          data-testid={`ot-query-borrar-confirmar-${query.id}`}
        >
          Sí
        </button>
        <button
          type="button"
          onClick={() => onPedirBorrado(null)}
          className="font-semibold text-[#6B7280] dark:text-white/50"
        >
          No
        </button>
      </li>
    );
  }

  return (
    <li className="group flex items-center gap-1">
      <button
        type="button"
        onClick={() => onOpen(query)}
        aria-current={activo ? "true" : undefined}
        title={query.descripcion ?? undefined}
        className={`min-w-0 flex-1 truncate rounded-lg px-2 py-1.5 text-left text-xs ${
          activo
            ? "bg-[#557EFF]/10 font-semibold text-[#3355CC] dark:text-[#9DB5FF]"
            : "text-[#0B1F33] hover:bg-[#F5F7FA] dark:text-white/80 dark:hover:bg-white/5"
        }`}
        data-testid={`ot-query-guardada-${query.id}`}
      >
        {query.nombre}
        {/* Decir que lo de pantalla ya no es lo guardado evita el malentendido clásico: creer que
            se está mirando la consulta de siempre cuando alguien tocó un filtro. */}
        {activo && modificada && (
          <span className="ml-1 font-normal text-[#8A6100] dark:text-[#F2C14E]">· modificada</span>
        )}
      </button>

      {!query.deFabrica && (
        <button
          type="button"
          onClick={() => onPedirBorrado(query)}
          aria-label={`Borrar ${query.nombre}`}
          className="rounded px-1 text-xs text-[#6B7280] opacity-0 transition-opacity hover:text-[#C0392B] focus:opacity-100 group-hover:opacity-100"
        >
          ×
        </button>
      )}
    </li>
  );
}
