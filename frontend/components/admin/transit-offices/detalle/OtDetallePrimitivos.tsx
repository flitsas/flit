"use client";

import type { ReactNode } from "react";
import { OT_BANDA, OT_BLUE, OT_CARD, OT_MARCO, OT_NAVY } from "./ot-detalle-visual";

/**
 * Primitivas del detalle del trámite del OT (HU #12060).
 *
 * Copia local de `components/operacion/detalle/primitivos.tsx` reducida a lo que esta pantalla usa.
 * Existen para cortar la última atadura con el detalle del gestor: mientras las secciones del OT
 * importaran de allí, cualquier retoque en la tarjeta o en el par campo/valor del gestor se vería
 * en el OT, que es exactamente lo que este rediseño tiene prohibido.
 */

/** Tarjeta interior. Lleva su propio relleno, al contrario que `OT_CARD` a secas. */
export function OtTarjeta({
  titulo,
  children,
  className = "",
}: {
  titulo: string;
  children: ReactNode;
  className?: string;
}) {
  return (
    <section className={`${OT_CARD} h-full p-4 ${className}`} aria-label={titulo}>
      <h4 className="mb-3 text-sm font-semibold" style={{ color: OT_NAVY }}>
        <span className="dark:text-white">{titulo}</span>
      </h4>
      {children}
    </section>
  );
}

/** Contenedor de `OtCampo`: rejilla de dos columnas, para que los valores queden alineados. */
export function OtListaCampos({ children }: { children: ReactNode }) {
  return (
    <dl className="grid grid-cols-[minmax(0,auto)_minmax(0,1fr)] gap-x-3 gap-y-1.5">{children}</dl>
  );
}

/** Par campo/valor suelto —`<dt>`/`<dd>`— para que la rejilla de arriba alinee las columnas. */
export function OtCampo({ campo, valor }: { campo: string; valor: ReactNode }) {
  return (
    <>
      <dt className="text-xs text-[#162744]/70 dark:text-white/70">{campo}</dt>
      <dd className="break-words text-xs font-medium text-[#162744] dark:text-white">
        {valor === null || valor === undefined || valor === "" ? "—" : valor}
      </dd>
    </>
  );
}

/** Sello redondeado del prototipo: `soft` lo pinta con el color al 13% de fondo. */
export function OtSello({ texto, color, soft }: { texto: string; color: string; soft?: boolean }) {
  return (
    <span
      className="whitespace-nowrap rounded-full px-2.5 py-0.5 text-[10px] font-semibold"
      style={soft ? { background: `${color}22`, color } : { background: color, color: "#fff" }}
    >
      {texto}
    </span>
  );
}

/** Estado «cargando» de una sección. `role="status"`: la espera se anuncia, no solo se dibuja. */
export function OtCargando({ etiqueta, filas = 3 }: { etiqueta: string; filas?: number }) {
  return (
    <div className="flex flex-col gap-2" role="status" aria-busy="true" aria-label={etiqueta}>
      {Array.from({ length: filas }).map((_, i) => (
        <div
          key={i}
          className="h-10 animate-pulse rounded-xl dark:bg-white/5"
          style={{ background: "rgba(223,229,237,0.5)" }}
        />
      ))}
    </div>
  );
}

/** Estado «vacío»: se dice qué falta, nunca se deja el hueco mudo. */
export function OtVacio({ mensaje }: { mensaje: string }) {
  return <p className="text-xs text-[#162744]/70 dark:text-white/70">{mensaje}</p>;
}

/** Estado «error» con reintento. `contexto` da nombre accesible propio a cada botón del panel. */
export function OtError({
  mensaje,
  onReintentar,
  contexto,
}: {
  mensaje: string;
  onReintentar: () => void;
  contexto?: string;
}) {
  return (
    <div className="flex flex-col items-start gap-2" role="alert">
      <p className="text-xs text-[#162744]/70 dark:text-white/70">{mensaje}</p>
      <button
        type="button"
        onClick={onReintentar}
        aria-label={contexto ? `Reintentar ${contexto}` : undefined}
        className="rounded-xl border px-3 py-1.5 text-xs font-semibold transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
        style={{ borderColor: OT_BLUE, color: OT_BLUE }}
      >
        Reintentar
      </button>
    </div>
  );
}

/**
 * Rejilla de datos del prototipo (HU #12061): una banda de encabezado y una o varias filas debajo.
 *
 * NO es una `<table>` sino una rejilla CSS con roles ARIA. El prototipo alinea columnas de anchos
 * muy distintos —un documento junto a un nombre completo junto a una lista de transformaciones— y
 * con `<table>` el reparto lo decide el navegador según el contenido: la misma columna cambiaba de
 * ancho entre un trámite y el siguiente. Los roles dejan la semántica intacta para quien navega con
 * lector de pantalla.
 */
export function OtRejilla({
  etiqueta,
  columnas,
  filas,
  plantilla,
  conMarco = true,
}: {
  /** Nombre accesible de la tabla; nunca se omite. */
  etiqueta: string;
  columnas: string[];
  filas: ReactNode[][];
  /** `grid-template-columns` a medida; por defecto todas las columnas iguales. */
  plantilla?: string;
  /** A false, la rejilla no dibuja su propio borde: la envuelve otra. */
  conMarco?: boolean;
}) {
  const estilo = {
    gridTemplateColumns: plantilla ?? `repeat(${columnas.length}, minmax(0, 1fr))`,
  };

  return (
    <div role="table" aria-label={etiqueta} className={conMarco ? OT_MARCO : undefined}>
      <div role="row" className={`grid ${OT_BANDA}`} style={estilo}>
        {columnas.map((c) => (
          <div
            key={c}
            role="columnheader"
            className="px-3 py-2 text-[10px] font-semibold uppercase tracking-wide opacity-60"
          >
            {c}
          </div>
        ))}
      </div>
      {filas.map((fila, i) => (
        <div
          key={i}
          role="row"
          className="grid border-t border-[#DFE5ED] dark:border-white/5"
          style={estilo}
        >
          {fila.map((celda, j) => (
            <div
              key={j}
              role="cell"
              className="self-center break-words px-3 py-2 text-xs font-semibold"
              style={{ color: OT_NAVY }}
            >
              <span className="dark:text-white">{celda}</span>
            </div>
          ))}
        </div>
      ))}
    </div>
  );
}

/**
 * Ficha de campos en el patrón de bandas alternas del prototipo: cuatro rótulos, cuatro valores,
 * cuatro rótulos, cuatro valores… dentro de un único marco.
 *
 * Cada tanda es una rejilla independiente y no una tabla con encabezados repetidos: así cada valor
 * queda bajo SU rótulo también para un lector de pantalla, en vez de colgar todos de la primera
 * banda.
 */
export function OtFichaCampos({
  etiqueta,
  campos,
  porFila = 4,
}: {
  etiqueta: string;
  /** Solo lo que tenga valor; un campo sin dato no llega hasta aquí. */
  campos: { campo: string; valor: ReactNode }[];
  porFila?: number;
}) {
  const tandas: { campo: string; valor: ReactNode }[][] = [];
  for (let i = 0; i < campos.length; i += porFila) {
    tandas.push(campos.slice(i, i + porFila));
  }

  return (
    <div className={OT_MARCO}>
      {tandas.map((tanda, i) => (
        <div key={i} className={i > 0 ? "border-t border-[#DFE5ED] dark:border-white/5" : undefined}>
          <OtRejilla
            etiqueta={tandas.length > 1 ? `${etiqueta} (${i + 1} de ${tandas.length})` : etiqueta}
            columnas={tanda.map((c) => c.campo)}
            filas={[tanda.map((c) => c.valor)]}
            // La última tanda puede venir incompleta: se mantiene el ancho de columna del resto
            // para que los valores no se estiren y desalineen la ficha entera.
            plantilla={`repeat(${porFila}, minmax(0, 1fr))`}
            conMarco={false}
          />
        </div>
      ))}
    </div>
  );
}
