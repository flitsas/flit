"use client";

import { useEffect, useState } from "react";
import { tramitesClient } from "@/lib/api/tramites-client";

/**
 * HU #14481 — conteo de trámites por estado para Inicio, con el endpoint que ya usa la tira de KPIs del listado
 * (`POST /api/v1/tramites/instances/estado-counts`, sin filtros: el mismo universo que /tramites al abrir).
 *
 * El cliente devuelve `{}` si el endpoint falla, igual que en el listado. Un objeto vacío se trata como «sin datos»,
 * no como «todo en cero»: mostrar ceros que no vienen del servidor sería inventar la cifra.
 */
export type ConteosEstado =
  | { estado: "cargando" }
  | { estado: "sin-datos" }
  | { estado: "listo"; conteos: Record<string, number> };

const APAGADO: ConteosEstado = { estado: "sin-datos" };

export function useConteosEstado(habilitado: boolean): ConteosEstado {
  const [resultado, setResultado] = useState<ConteosEstado>({ estado: "cargando" });

  useEffect(() => {
    if (!habilitado) return;
    let vivo = true;
    tramitesClient
      .searchEstadoCounts({})
      .then((conteos) => {
        if (!vivo) return;
        setResultado(conteos && Object.keys(conteos).length > 0 ? { estado: "listo", conteos } : { estado: "sin-datos" });
      })
      .catch(() => vivo && setResultado({ estado: "sin-datos" }));
    return () => {
      vivo = false;
    };
  }, [habilitado]);

  return habilitado ? resultado : APAGADO;
}
