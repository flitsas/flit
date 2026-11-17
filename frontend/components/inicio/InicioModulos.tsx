"use client";

import { useMemo } from "react";
import { useShellNav } from "@flit/shell/nav-context";
import { ESTADOS_FINALES, ESTADOS_TRAMITE } from "@/lib/tramites/estados";
import { CifrasEstado } from "./CifrasEstado";
import { TRAMITES_DOCK_KEY } from "./destinos";
import { ModulosAcordeon, type ConteosModulo } from "./ModulosAcordeon";
import { useConteosEstado, type ConteosEstado } from "./useConteosEstado";

/**
 * Trámites que siguen en curso: todos los estados menos los finales (RF04, `ESTADOS_FINALES`). El pseudo-estado de
 * filtro «rechazado desde preasignación» no se suma: ya está dentro de `rechazado`.
 */
export function enCurso(conteos: Record<string, number>): number {
  return ESTADOS_TRAMITE.filter((e) => !ESTADOS_FINALES.includes(e)).reduce((s, e) => s + (conteos[e] ?? 0), 0);
}

export function conteosModulo(c: ConteosEstado): ConteosModulo {
  return c.estado === "listo" ? { [TRAMITES_DOCK_KEY]: { total: enCurso(c.conteos), etiqueta: "en curso" } } : {};
}

/**
 * HU #14481 — sección nueva de Inicio: cifras por estado (si el rol ve Trámites en el dock) y el acordeón de módulos.
 * Una sola consulta de conteos para las dos cosas.
 */
export function InicioModulos() {
  const { groups } = useShellNav();
  const veTramites = useMemo(() => groups.some((g) => g.items.some((e) => e.key === TRAMITES_DOCK_KEY)), [groups]);
  const conteos = useConteosEstado(veTramites);
  const porModulo = useMemo(() => conteosModulo(conteos), [conteos]);

  return (
    <div className="flex flex-col gap-4">
      {veTramites && <CifrasEstado conteos={conteos} />}
      <ModulosAcordeon conteos={porModulo} />
    </div>
  );
}
