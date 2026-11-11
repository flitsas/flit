// Celda de una de las cuatro columnas de validación de identidad (Persona, Validación, Intentos,
// Motivo de rechazo). Pinta un renglón por actor, de 46 px fijos: la línea i de las cuatro columnas
// es siempre el mismo actor, aunque los textos largos se corten (el completo va en el título).

import type { ActorValidacionDto } from "@/lib/api/company-queries";
import {
  actoresOrdenados,
  colorValidacion,
  documentoActor,
  etiquetaValidacion,
  motivoVisible,
  ROL_LABEL,
} from "./identidad-validacion";

export type CampoActor = "persona" | "validacion" | "intentos" | "motivo";

/** Alto fijo por actor: es lo que alinea las cuatro columnas entre sí. */
const ALTO_ACTOR = "h-[46px]";

export function ActoresValidacionCell({
  actores,
  campo,
}: {
  actores: ActorValidacionDto[] | undefined;
  campo: CampoActor;
}) {
  const lista = actoresOrdenados(actores);
  if (lista.length === 0) {
    return <span className={`flex ${ALTO_ACTOR} items-center`}>—</span>;
  }

  return (
    <div data-testid={`actores-${campo}`}>
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
    const detalle = [ROL_LABEL[actor.rol], documentoActor(actor)].filter(Boolean).join(" · ");
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
    const color = colorValidacion(actor.estadoValidacion);
    const label = etiquetaValidacion(actor.estadoValidacion);
    return (
      <span
        className="w-fit max-w-full truncate rounded-full px-2 py-0.5 text-[11px] font-semibold"
        style={{ background: `color-mix(in srgb, ${color} 10%, transparent)`, color }}
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

const CAMPO_POR_COLUMNA: Record<string, CampoActor> = {
  id_persona: "persona",
  id_validacion: "validacion",
  id_intentos: "intentos",
  id_motivo: "motivo",
};

/** Para el `renderCell` de la consola: pinta las 4 columnas de identidad, `null` en el resto. */
export function renderCeldaIdentidad(
  columnId: string,
  fila: { actores?: ActorValidacionDto[] },
) {
  const campo = CAMPO_POR_COLUMNA[columnId];
  return campo ? <ActoresValidacionCell actores={fila.actores} campo={campo} /> : null;
}
