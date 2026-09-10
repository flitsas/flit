/**
 * Transformaciones del vehículo declaradas en un trámite: color, combustible y carrocería.
 *
 * Cada uno de esos tres atributos tiene DOS caras en el trámite: el valor con el que el vehículo
 * figura en el RUNT (snapshot de la consulta) y el valor EFECTIVO, que es el nuevo cuando el trámite
 * declara la transformación. Quien muestre solo el efectivo hace pasar por dato oficial algo que el
 * gestor acaba de escribir — el error que corrige el detalle del OT (HU #11931).
 *
 * La regla de «hay transformación» se declara aquí una sola vez porque la usan tanto el asistente
 * del gestor, que captura el valor nuevo, como el detalle del OT, que lo revisa.
 */

export type TransformacionTipo = "color" | "combustible" | "carroceria";

/** Etiquetas de atributo: el mismo vocabulario que ya usa el módulo de Reportes. */
export const TRANSFORMACION_LABELS: Record<TransformacionTipo, string> = {
  color: "Color",
  combustible: "Combustible",
  carroceria: "Carrocería",
};

export interface AtributoTransformable {
  tipo: TransformacionTipo;
  /** Valor registrado en el RUNT; nulo o vacío si el trámite nunca lo consultó. */
  valorRunt?: string | null;
  /** Valor efectivo del trámite: el nuevo si se declaró una transformación. */
  valorEfectivo?: string | null;
  /** Bandera `cambio_*` con la que el trámite declara explícitamente la transformación. */
  declarado?: boolean;
}

export interface TransformacionVehiculo {
  tipo: TransformacionTipo;
  label: string;
  /** Valor del RUNT, o `null` cuando el trámite no lo capturó. Nunca se sustituye por el nuevo. */
  valorRunt: string | null;
  valorNuevo: string | null;
}

function limpio(value: string | null | undefined): string {
  return value?.trim() ?? "";
}

/**
 * Hay cambio si ambos valores existen y difieren, comparando sin distinguir mayúsculas ni espacios.
 * Sin uno de los dos no se declara nada: un atributo a medio capturar no es una transformación.
 */
export function valorCambiado(
  runt: string | null | undefined,
  efectivo: string | null | undefined,
): boolean {
  const a = limpio(runt);
  const b = limpio(efectivo);
  return a !== "" && b !== "" && a.toUpperCase() !== b.toUpperCase();
}

/**
 * Transformaciones que el trámite declara, en el orden en que se reciben los atributos.
 *
 * Dos vías, y basta una: la bandera `cambio_*` —que el gestor marca, y que un tipo de trámite de la
 * familia OTROS trae por definición— o la diferencia entre el RUNT y el valor efectivo. La bandera
 * sola cuenta porque puede marcarse antes de capturar el valor nuevo, y el operador debe ver que el
 * trámite promete un cambio aunque el valor todavía no esté.
 */
export function transformacionesDeclaradas(
  atributos: AtributoTransformable[],
): TransformacionVehiculo[] {
  return atributos
    .filter(
      (a) => a.declarado === true || valorCambiado(a.valorRunt, a.valorEfectivo),
    )
    .map((a) => ({
      tipo: a.tipo,
      label: TRANSFORMACION_LABELS[a.tipo],
      valorRunt: limpio(a.valorRunt) || null,
      valorNuevo: limpio(a.valorEfectivo) || null,
    }));
}
