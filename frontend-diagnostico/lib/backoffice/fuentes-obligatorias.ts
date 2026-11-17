// Catálogo global de fuentes obligatorias (ADR-0078). Contratos GET y PUT
// /api/v1/diagnostico/backoffice/fuentes-obligatorias de contracts/openapi/diagnostico.v1.yaml
// (esquemas DefinicionFuentesObligatorias y ListaDefinicionesFuentesObligatorias), en snake_case.
// El dato es de FLIT: esta llamada no manda ?empresa= ni lee una compañía. No hay DELETE.
import { apiFetch } from "@/lib/api";
import { FUENTES, type Fuente } from "@/lib/configuracion";

/** Valor del enum Fuente salvo RUNT, que no se configura en este catálogo. */
export type FuenteConfigurable = Exclude<Fuente, "RUNT">;

export interface DefinicionFuentesObligatorias {
  /** Familia del trámite. Null cuando la definición es de un tipo. */
  familia: string | null;
  /** Código del tipo. Null cuando la definición es de una familia. */
  tipo_tramite_codigo: string | null;
  /** Lista completa. Vacía, con la definición presente, es la lista vacía explícita. */
  fuentes: FuenteConfigurable[];
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
