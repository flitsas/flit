// Catálogo global de fuentes obligatorias (ADR-0078). Contratos GET y PUT
// /api/v1/diagnostico/backoffice/fuentes-obligatorias de contracts/openapi/diagnostico.v1.yaml
// (esquemas DefinicionFuentesObligatorias y ListaDefinicionesFuentesObligatorias), en snake_case.
// El dato es de FLIT: esta llamada no manda ?empresa= ni lee una compañía. No hay DELETE.
import { apiFetch } from "@/lib/api";
import { FUENTES, type Fuente } from "@/lib/configuracion";
import { FAMILIAS_TRAMITE, rotular, TIPOS_TRAMITE } from "@/lib/rotulos";

/** Valor del enum Fuente salvo RUNT, que no se configura en este catálogo. */
export type FuenteConfigurable = Exclude<Fuente, "RUNT">;

export interface DefinicionFuentesObligatorias {
  /** Familia del trámite. Null cuando la definición es de un tipo. */
  familia: string | null;
  /** Código del tipo. Null cuando la definición es de una familia. */
  tipo_tramite_codigo: string | null;
  /** Lista completa. Vacía, con la definición presente, es la lista vacía explícita. */
  fuentes: FuenteConfigurable[];
  /** Punto 5: nombre de negocio de la familia (solo lectura, opcional; no viaja en el PUT). */
  familia_etiqueta?: string | null;
  /** Punto 5: nombre de negocio del tipo (solo lectura, opcional; no viaja en el PUT). */
  tipo_tramite_etiqueta?: string | null;
}

type Alcance = Pick<DefinicionFuentesObligatorias, "familia" | "tipo_tramite_codigo" | "familia_etiqueta" | "tipo_tramite_etiqueta">;

/** Título visible de una definición: «Familia: …» o «Tipo de trámite: …» con el nombre, nunca el código (Punto 5). */
export function tituloDefinicion(def: Alcance): string {
  if (def.familia) return `Familia: ${rotular({ contexto: "familia de trámite", codigo: def.familia, etiqueta: def.familia_etiqueta, respaldo: FAMILIAS_TRAMITE })}`;
  if (def.tipo_tramite_codigo)
    return `Tipo de trámite: ${rotular({ contexto: "tipo de trámite", codigo: def.tipo_tramite_codigo, etiqueta: def.tipo_tramite_etiqueta, respaldo: TIPOS_TRAMITE })}`;
  return "Definición";
}

export interface ListaDefinicionesFuentesObligatorias {
  definiciones: DefinicionFuentesObligatorias[];
}

/** Casillas del enum Fuente menos RUNT, en el orden del contrato. Las etiquetas salen de `nombreFuente`. */
export const FUENTES_CONFIGURABLES: readonly FuenteConfigurable[] = FUENTES.map((f) => f.codigo).filter((c): c is FuenteConfigurable => c !== "RUNT");

const RUTA = "/backoffice/fuentes-obligatorias";

export function listarFuentesObligatorias(signal?: AbortSignal): Promise<ListaDefinicionesFuentesObligatorias> {
  return apiFetch<ListaDefinicionesFuentesObligatorias>(RUTA, { signal });
}

/** Reemplaza las fuentes de una definición. El cuerpo es la lista completa, no un parche. */
export function guardarFuentesObligatorias(definicion: DefinicionFuentesObligatorias): Promise<DefinicionFuentesObligatorias> {
  return apiFetch<DefinicionFuentesObligatorias>(RUTA, { method: "PUT", body: definicion });
}
