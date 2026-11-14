// Tarjetas-filtro, búsqueda y veredicto de la grilla del lote (HU #14562 AC27, AC28 y AC31). Las cifras de las tarjetas son
// los conteos del servidor (`grilla.conteos`, B1): no se recalculan con las filas, que en el sondeo incremental llegan solo
// las que cambiaron. El filtro se aplica en el cliente sobre las filas fusionadas. Sin conteos (API previo a B1) solo quedan
// Vehículos y Terminados, que salen del avance.
import type { ConteosGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { derivacionDeFila } from "@/lib/tablero/derivacion";

export type FiltroTablero = keyof ConteosGrilla;

export const FILTROS_TABLERO: readonly { clave: FiltroTablero; etiqueta: string }[] = [
  { clave: "vehiculos", etiqueta: "Vehículos" },
  { clave: "terminados", etiqueta: "Terminados" },
  { clave: "con_error", etiqueta: "Con error" },
  { clave: "con_deuda", etiqueta: "Con deuda" },
  { clave: "bloqueados", etiqueta: "Bloqueados" },
  { clave: "listos_tramite", etiqueta: "Listos para trámite" },
];

export interface TarjetaTablero {
  clave: FiltroTablero;
  etiqueta: string;
  cifra: number;
}

/** Tarjetas que se pueden pintar con su cifra: las seis con `conteos`; sin ellos, Vehículos y Terminados del avance. */
export function tarjetasVisibles(grilla: Grilla): TarjetaTablero[] {
  const { conteos } = grilla;
  if (conteos) return FILTROS_TABLERO.map((f) => ({ ...f, cifra: conteos[f.clave] }));
  return [
    { ...FILTROS_TABLERO[0], cifra: grilla.avance.vehiculos },
    { ...FILTROS_TABLERO[1], cifra: grilla.avance.terminados },
  ];
}

const tieneDeuda = (v: FilaVehiculo) =>
  v.celdas.some((c) => c.fuente === "IMPUESTO_DEPARTAMENTAL" && c.estado === "COMPLETADO" && c.tiene_deuda === true);

const PREDICADOS: Record<FiltroTablero, (v: FilaVehiculo) => boolean> = {
  vehiculos: () => true,
  terminados: (v) => v.terminado,
  con_error: (v) => v.celdas.some((c) => c.estado === "ERROR"),
  con_deuda: tieneDeuda,
  bloqueados: (v) => v.veredicto === "BLOQUEADO",
  listos_tramite: (v) => derivacionDeFila(v).elegible,
};

/** Filas que deja pasar la tarjeta activa (null = sin filtro). */
export function filtrarVehiculos(vehiculos: FilaVehiculo[], filtro: FiltroTablero | null): FilaVehiculo[] {
  return filtro ? vehiculos.filter(PREDICADOS[filtro]) : vehiculos;
}

const normalizar = (t: string) => t.replace(/\s+/g, "").toUpperCase();

/** Búsqueda por placa o VIN: contiene, sin distinguir mayúsculas ni espacios. Texto vacío, todas. */
export function buscarVehiculos(vehiculos: FilaVehiculo[], texto: string): FilaVehiculo[] {
  const q = normalizar(texto);
  if (!q) return vehiculos;
  return vehiculos.filter((v) => [v.placa, v.vin, v.identificador].some((x) => x != null && normalizar(x).includes(q)));
}

export type TonoVeredicto = "success" | "danger" | "neutral";

/** Dictamen de la fila: Viable, Bloqueado o Pendiente (sin la clave, null o modo Simple). */
export function veredictoDeFila(v: FilaVehiculo): { texto: string; tono: TonoVeredicto } {
  if (v.veredicto === "VIABLE") return { texto: "Viable", tono: "success" };
  if (v.veredicto === "BLOQUEADO") return { texto: "Bloqueado", tono: "danger" };
  return { texto: "Pendiente", tono: "neutral" };
}

// ---------- Saneamiento (HU #14577 AC9, spec-ui §2.3) ----------

/** Chip «Con / Sin saneamiento» del tablero; va en la URL como `?saneamiento=con|sin` (D-09). */
export type FiltroSaneamiento = "con" | "sin";

export const FILTROS_SANEAMIENTO: readonly { clave: FiltroSaneamiento; etiqueta: string }[] = [
  { clave: "con", etiqueta: "Con saneamiento" },
  { clave: "sin", etiqueta: "Sin saneamiento" },
];

/** Valor de `?saneamiento=` (repetido: el primero). Cualquier otro valor es «sin filtro». */
export function leerFiltroSaneamiento(valor: string | string[] | null | undefined): FiltroSaneamiento | null {
  const v = Array.isArray(valor) ? valor[0] : valor;
  return v === "con" || v === "sin" ? v : null;
}

/** Con saneamiento = estado no nulo y no anulado (la compañía no ve anulados). Sin la clave cuenta como sin saneamiento. */
const conSaneamiento = (v: FilaVehiculo) => {
  const estado = v.saneamiento?.estado as string | null | undefined;
  return estado != null && estado !== "ANULADO";
};

/** Filas que deja pasar el chip activo (null = todas). Se combina (Y) con la tarjeta y la búsqueda. */
export function filtrarPorSaneamiento(vehiculos: FilaVehiculo[], filtro: FiltroSaneamiento | null): FilaVehiculo[] {
  if (!filtro) return vehiculos;
  return vehiculos.filter((v) => conSaneamiento(v) === (filtro === "con"));
}

/** Alguna fila trae la clave `saneamiento` (backend con saneamiento): solo entonces hay chips (spec-ui §2.6). */
export function hayDatosSaneamiento(vehiculos: readonly FilaVehiculo[]): boolean {
  return vehiculos.some((v) => v.saneamiento !== undefined);
}
