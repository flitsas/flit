// Pilares del tablero (HU #14087): la tabla de resultados agrupa las fuentes por familia de fuente y pinta SIEMPRE las 8, en el
// orden de FUENTES (lib/configuracion.ts), sin importar lo que pidió el lote. Lo pedido sale de `grilla.columnas`.
import { FUENTES, type Fuente } from "@/lib/configuracion";
import type { ColumnasGrilla } from "@/lib/tablero/api";

export interface Pilar {
  nombre: string;
  fuentes: Fuente[];
}

const PILAR_DE: Record<Fuente, string> = {
  RUNT: "RUNT",
  RUNT_PROPIETARIO: "RUNT",
  RUNT_LOCATARIO: "RUNT",
  SIMIT_VEHICULO: "SIMIT",
  SIMIT_PROPIETARIO: "SIMIT",
  SIMIT_LOCATARIO: "SIMIT",
  AVALUO: "Avalúo comercial",
  IMPUESTO_DEPARTAMENTAL: "Impuesto departamental",
};

/** RUNT, SIMIT, Avalúo comercial e Impuesto departamental, cada uno con sus fuentes en el orden de FUENTES. */
export const PILARES: readonly Pilar[] = FUENTES.reduce<Pilar[]>((pilares, { codigo }) => {
  const nombre = PILAR_DE[codigo];
  const ultimo = pilares[pilares.length - 1];
  if (ultimo?.nombre === nombre) ultimo.fuentes.push(codigo);
  else pilares.push({ nombre, fuentes: [codigo] });
  return pilares;
}, []);

/** Las 8 fuentes en el orden de las columnas de la tabla. */
export const FUENTES_TABLA: readonly Fuente[] = PILARES.flatMap((p) => p.fuentes);

/** Fuentes que pidió el lote: las de cualquiera de las tres listas de `grilla.columnas`. */
export function fuentesPedidas(columnas: Partial<ColumnasGrilla> | undefined): ReadonlySet<Fuente> {
  return new Set<Fuente>([...(columnas?.por_vehiculo ?? []), ...(columnas?.por_propietario ?? []), ...(columnas?.por_locatario ?? [])]);
}
