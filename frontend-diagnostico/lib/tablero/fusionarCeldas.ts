// Fusión incremental de la grilla del lote (HU #13748, F04-C3). Cada sondeo trae solo los vehículos y las celdas con versión
// mayor que la que ya tiene el cliente; el estado global, las columnas y el avance vienen siempre. Fusiona por vehículo
// (`vehiculos[].id`) y, dentro de él, por fuente (`celdas[].fuente`); lo no recibido se conserva tal cual.
import type { CeldaGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";

const porOrden = (a: FilaVehiculo, b: FilaVehiculo) => a.orden - b.orden;

function fusionarVehiculo(actual: FilaVehiculo, recibido: FilaVehiculo): FilaVehiculo {
  const recibidas = new Map(recibido.celdas.map((c) => [c.fuente, c]));
  // Una celda con versión menor que la que ya se tiene es una respuesta atrasada: no pisa la vigente.
  const vigente = (anterior: CeldaGrilla): CeldaGrilla => {
    const nueva = recibidas.get(anterior.fuente);
    return nueva && nueva.version >= anterior.version ? nueva : anterior;
  };
  const conocidas = new Set(actual.celdas.map((c) => c.fuente));
  return {
    ...actual,
    ...recibido,
    celdas: [...actual.celdas.map(vigente), ...recibido.celdas.filter((c) => !conocidas.has(c.fuente))],
  };
}

/** Pone los cambios recibidos encima de la grilla vigente, sin mutarla. Sin grilla previa, los cambios son la grilla. */
export function fusionarCeldas(actual: Grilla | null, cambios: Grilla): Grilla {
  if (!actual) return { ...cambios, vehiculos: [...cambios.vehiculos].sort(porOrden) };

  const recibidos = new Map(cambios.vehiculos.map((v) => [v.id, v]));
  const conocidos = new Set(actual.vehiculos.map((v) => v.id));
  const vehiculos = [
    ...actual.vehiculos.map((v) => {
      const recibido = recibidos.get(v.id);
      return recibido ? fusionarVehiculo(v, recibido) : v;
    }),
    ...cambios.vehiculos.filter((v) => !conocidos.has(v.id)),
  ].sort(porOrden);

  return { ...cambios, vehiculos };
}

const SIN_TERMINAR: ReadonlySet<CeldaGrilla["estado"]> = new Set(["PENDIENTE", "EN_CURSO"]);

/** Queda alguna celda PENDIENTE o EN_CURSO: el sondeo sigue; si no, se detiene. */
export function hayPendientes(grilla: Grilla | null): boolean {
  return !!grilla?.vehiculos.some((v) => v.celdas.some((c) => SIN_TERMINAR.has(c.estado)));
}

/**
 * Reproceso optimista: la celda vuelve a PENDIENTE (gris), su vehículo deja de estar terminado y el lote pasa a EN_PROCESO,
 * como hace la API (HU #13746). El siguiente sondeo trae la versión del servidor y, si era un RUNT, las celdas que bloqueaba.
 */
export function marcarReprocesada(grilla: Grilla, celdaId: string): Grilla {
  return {
    ...grilla,
    estado: "EN_PROCESO",
    vehiculos: grilla.vehiculos.map((v) =>
      v.celdas.some((c) => c.id === celdaId)
        ? {
            ...v,
            terminado: false,
            celdas: v.celdas.map((c) =>
              c.id === celdaId ? { ...c, estado: "PENDIENTE", tipo_error: null, motivo_na: null, dato_obtenido_en: null, reutilizada: false } : c,
            ),
          }
        : v,
    ),
  };
}
