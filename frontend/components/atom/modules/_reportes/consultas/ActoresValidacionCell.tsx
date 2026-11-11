// Celda de una de las cuatro columnas de un rol (Comprador/Vendedor, Validación, Intentos, Motivo de
// rechazo). Pinta un renglón por actor del rol, de 46 px fijos: la línea i de las cuatro columnas es
// siempre el mismo actor, aunque los textos largos se corten (el completo va en el título).

import type { ActorValidacionDto } from "@/lib/api/company-queries";
import {
  actoresDelGrupo,
  type GrupoPersonas,
  estiloChipValidacion,
  documentoActor,
  etiquetaValidacion,
  motivoVisible,
} from "./identidad-validacion";

export type CampoActor = "persona" | "validacion" | "intentos" | "motivo";

/** Alto fijo por actor: es lo que alinea las cuatro columnas entre sí. */
const ALTO_ACTOR = "h-[46px]";

/**
 * Ancho máximo de las columnas de texto libre. Sin él, el `<td>` crece al ancho del nombre más largo
 * (razones sociales de 100+ caracteres) y desborda la tabla: el truncado no funciona si nada acota.
 */
const ANCHO_MAX: Record<CampoActor, string> = {
  persona: "max-w-[250px]",
  validacion: "",
  intentos: "",
  motivo: "max-w-[260px]",
};

export function ActoresValidacionCell({
  actores,
  campo,
}: {
  /** Los actores YA filtrados del rol (ver `actoresDelGrupo`). */
  actores: ActorValidacionDto[];
  campo: CampoActor;
}) {
  const lista = actores;
  if (lista.length === 0) {
    return <span className={`flex ${ALTO_ACTOR} items-center`}>—</span>;
  }

  return (
    <div data-testid={`actores-${campo}`} className={ANCHO_MAX[campo]}>
      {lista.map((a) => (
        <div
          key={`${a.rol}-${a.ordinal}`}
          className={`flex ${ALTO_ACTOR} min-w-0 flex-col justify-center`}
          data-testid={`actor-${campo}`}
        >
          <Contenido actor={a} campo={campo} />
        </div>
      ))}
    </div>
  );
}

function Contenido({ actor, campo }: { actor: ActorValidacionDto; campo: CampoActor }) {
  if (campo === "persona") {
    const detalle = documentoActor(actor);
    return (
      <>
        <span className="truncate font-semibold" title={actor.nombre}>
          {actor.nombre || "—"}
        </span>
        <span className="truncate text-[11px] text-muted-foreground" title={detalle}>
          {detalle}
        </span>
      </>
    );
  }
  if (campo === "validacion") {
    const label = etiquetaValidacion(actor.estadoValidacion);
    return (
      <span
        className="w-fit max-w-full truncate rounded-full px-2 py-0.5 text-[11px] font-semibold"
        style={estiloChipValidacion(actor.estadoValidacion)}
        title={label}
      >
        {label}
      </span>
    );
  }
  if (campo === "intentos") {
    return <span className="tabular-nums">{actor.intentos}</span>;
  }
  const motivo = motivoVisible(actor.motivoRechazo);
  return (
    <span className="line-clamp-2" title={motivo ?? undefined}>
      {motivo ?? "—"}
    </span>
  );
}

const COLUMNA: Record<string, { grupo: GrupoPersonas; campo: CampoActor }> = {
  comprador: { grupo: "comprador", campo: "persona" },
  validacion_comprador: { grupo: "comprador", campo: "validacion" },
  intentos_comprador: { grupo: "comprador", campo: "intentos" },
  motivo_comprador: { grupo: "comprador", campo: "motivo" },
  vendedor: { grupo: "vendedor", campo: "persona" },
  validacion_vendedor: { grupo: "vendedor", campo: "validacion" },
  intentos_vendedor: { grupo: "vendedor", campo: "intentos" },
  motivo_vendedor: { grupo: "vendedor", campo: "motivo" },
};

/**
 * Para el `renderCell` de la consola: pinta las 8 columnas por rol, `null` en el resto.
 *
 * Con la lista de actores vacía (backend anterior, o un trámite sin actores emitibles) devuelve
 * `null` y la consola pinta el texto de siempre (`comprador`/`vendedor`); las columnas de validación
 * dicen «—».
 */
export function renderCeldaIdentidad(
  columnId: string,
  fila: { actores?: ActorValidacionDto[] | null },
) {
  const col = COLUMNA[columnId];
  if (!col) return null;
  if (col.campo === "persona" && (fila.actores?.length ?? 0) === 0) return null;
  return <ActoresValidacionCell actores={actoresDelGrupo(fila.actores, col.grupo)} campo={col.campo} />;
}
