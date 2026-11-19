// HU #14480 / #14481 — a dónde llevan el buscador Ctrl K y las cifras de Inicio. Funciones puras, una por destino, para
// que el contrato con el listado de /tramites viva en un solo sitio:
//
//   /tramites?placa=<PLACA>    búsqueda del listado sembrada con la placa (mayúsculas, sin espacios ni guiones)
//   /tramites?estado=<estado>  filtro de estado: una clave de ESTADOS_TRAMITE o el pseudo-estado
//                              FILTRO_RECHAZADO_PREASIGNACION (lib/tramites/estados.ts)
//   /tramites?atajo=<clave>    atajo de la búsqueda rápida: la clave de ATAJOS_GESTOR (lib/tramites/busquedaRapida.ts),
//                              que coincide con su `busquedaRapida` cuando el atajo lo tiene
//
// El listado los siembra una sola vez al montar (como la bandeja del OT con ?placa=/?status=); esa lectura la agrega
// la sesión coordinadora al integrar (ver el commit que cierra la HU #14481).
import type { PlateSearch, SearchAction } from "@flit/shell/search";
import { normalizePlate } from "@flit/shell/search";
import { ATAJOS_GESTOR, type AtajoGestor } from "@/lib/tramites/busquedaRapida";
import type { EstadoFiltro } from "@/lib/tramites/estados";

export const TRAMITES_PATH = "/tramites";
/** Entrada del dock de la que dependen los destinos: quien no ve el listado no recibe atajos, placas ni cifras. */
export const TRAMITES_DOCK_KEY = "tramites";

const conParametro = (nombre: string, valor: string) => `${TRAMITES_PATH}?${new URLSearchParams({ [nombre]: valor })}`;

export function tramitesPorPlaca(placa: string): string {
  return conParametro("placa", normalizePlate(placa));
}

export function tramitesPorEstado(estado: EstadoFiltro): string {
  return conParametro("estado", estado);
}

export function tramitesPorAtajo(atajo: AtajoGestor): string {
  return conParametro("atajo", atajo);
}

/** Acciones rápidas del buscador: los atajos que ya existen en el listado, con su texto de siempre. */
export const ACCIONES_BUSCADOR: SearchAction[] = ATAJOS_GESTOR.map((a) => ({
  key: a.key,
  label: a.label,
  hint: a.hint,
  href: tramitesPorAtajo(a.key),
  requires: TRAMITES_DOCK_KEY,
}));

export const PLACA_BUSCADOR: PlateSearch = {
  href: tramitesPorPlaca,
  hint: "Buscar la placa en Trámites",
  requires: TRAMITES_DOCK_KEY,
};
