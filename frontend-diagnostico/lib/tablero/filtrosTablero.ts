// Tarjetas-filtro, búsqueda y veredicto de la grilla del lote (HU #14562 AC27, AC28 y AC31). Las cifras de las tarjetas son
// los conteos del servidor (`grilla.conteos`, B1): no se recalculan con las filas, que en el sondeo incremental llegan solo
// las que cambiaron. El filtro se aplica en el cliente sobre las filas fusionadas. Sin conteos (API previo a B1) solo quedan
// Vehículos y Terminados, que salen del avance.
import type { Fuente } from "@/lib/configuracion";
import type { ColumnasGrilla, ConteosGrilla, FilaVehiculo, Grilla } from "@/lib/tablero/api";
import { derivacionDeFila } from "@/lib/tablero/derivacion";
import { FUENTES_TABLA, fuentesPedidas } from "@/lib/tablero/pilares";

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

// ---------- Barra de filtros de la grilla (HU #14584, spec-ui §6.6; contrato §2) ----------
// Se aplica en el cliente sobre las filas ya cargadas (compañía y backoffice). SP-21: el estado solo lleva códigos (fuente,
// estado, veredicto y un booleano) y la búsqueda que escribió el usuario; no se guarda en la URL ni en localStorage.

/** Estado visible de la celda de una fuente pedida, como lo pinta el semáforo («Con deuda» solo en el impuesto). */
export type EstadoFiltro = "EJECUTADO" | "CON_DEUDA" | "ERROR" | "NOVEDAD" | "SIN_EJECUTAR";

/** Rótulos en el orden del semáforo. */
export const ESTADOS_FILTRO: readonly { estado: EstadoFiltro; etiqueta: string }[] = [
  { estado: "EJECUTADO", etiqueta: "Ejecutado" },
  { estado: "CON_DEUDA", etiqueta: "Con deuda" },
  { estado: "ERROR", etiqueta: "Error" },
  { estado: "NOVEDAD", etiqueta: "Novedad" },
  { estado: "SIN_EJECUTAR", etiqueta: "Sin ejecutar" },
];

export type DictamenFiltro = "VIABLE" | "BLOQUEADO" | "PENDIENTE";

export const DICTAMENES_FILTRO: readonly { valor: DictamenFiltro; etiqueta: string }[] = [
  { valor: "VIABLE", etiqueta: "Viable" },
  { valor: "BLOQUEADO", etiqueta: "Bloqueado" },
  { valor: "PENDIENTE", etiqueta: "Pendiente" },
];

export interface FiltrosGrilla {
  /** Placa o VIN (contiene, sin mayúsculas ni espacios). */
  busqueda: string;
  fuente: Fuente | null;
  /** Solo cuenta con `fuente`. */
  estado: EstadoFiltro | null;
  dictamen: DictamenFiltro | null;
  /** true: solo las filas con `cambio_propietario === true`. */
  cambioPropietario: boolean;
}

export const FILTROS_GRILLA_VACIOS: FiltrosGrilla = { busqueda: "", fuente: null, estado: null, dictamen: null, cambioPropietario: false };

/** Algún filtro de la barra está activo (la búsqueda en blanco no cuenta). */
export function hayFiltrosGrilla(f: FiltrosGrilla): boolean {
  return f.busqueda.trim() !== "" || f.fuente !== null || f.estado !== null || f.dictamen !== null || f.cambioPropietario;
}

/** Estado visible de la celda de `fuente` en la fila; sin celda (aún no llegó) o pendiente/en curso, «Sin ejecutar». */
export function estadoFiltroDe(v: FilaVehiculo, fuente: Fuente): EstadoFiltro {
  const c = v.celdas.find((x) => x.fuente === fuente);
  if (!c || c.estado === "PENDIENTE" || c.estado === "EN_CURSO") return "SIN_EJECUTAR";
  if (c.estado === "ERROR") return "ERROR";
  if (c.estado === "NA") return "NOVEDAD";
  return fuente === "IMPUESTO_DEPARTAMENTAL" && c.tiene_deuda === true ? "CON_DEUDA" : "EJECUTADO";
}

/** Estados presentes en la columna de `fuente`, con su conteo, en el orden del semáforo. */
export function opcionesEstado(vehiculos: readonly FilaVehiculo[], fuente: Fuente): { estado: EstadoFiltro; etiqueta: string; cantidad: number }[] {
  const conteo = new Map<EstadoFiltro, number>();
  for (const v of vehiculos) {
    const e = estadoFiltroDe(v, fuente);
    conteo.set(e, (conteo.get(e) ?? 0) + 1);
  }
  return ESTADOS_FILTRO.filter((o) => conteo.has(o.estado)).map((o) => ({ ...o, cantidad: conteo.get(o.estado)! }));
}

/** Fuentes que se pueden filtrar: solo las pedidas por el lote, en el orden de las columnas de la tabla. */
export function fuentesFiltrables(columnas: Partial<ColumnasGrilla> | undefined): Fuente[] {
  const pedidas = fuentesPedidas(columnas);
  return FUENTES_TABLA.filter((f) => pedidas.has(f));
}

const dictamenDeFila = (v: FilaVehiculo): DictamenFiltro => (v.veredicto === "VIABLE" ? "VIABLE" : v.veredicto === "BLOQUEADO" ? "BLOQUEADO" : "PENDIENTE");

/** Filas que dejan pasar todos los filtros de la barra a la vez (Y). */
export function aplicarFiltrosGrilla(vehiculos: FilaVehiculo[], f: FiltrosGrilla): FilaVehiculo[] {
  const { fuente, estado, dictamen, cambioPropietario } = f;
  const filtradas = vehiculos.filter(
    (v) =>
      (!fuente || !estado || estadoFiltroDe(v, fuente) === estado) &&
      (!dictamen || dictamenDeFila(v) === dictamen) &&
      (!cambioPropietario || v.cambio_propietario === true),
  );
  return buscarVehiculos(filtradas, f.busqueda);
}

/** Alguna fila trae veredicto: solo entonces hay filtro de Dictamen. */
export const hayVeredictos = (vehiculos: readonly FilaVehiculo[]) => vehiculos.some((v) => v.veredicto != null);

/** Alguna fila trae `cambio_propietario` no nulo: solo entonces hay chip de cambio de propietario. */
export const hayDatoCambioPropietario = (vehiculos: readonly FilaVehiculo[]) => vehiculos.some((v) => v.cambio_propietario != null);

/** Filas con cambio de propietario (`true`). */
export const contarCambioPropietario = (vehiculos: readonly FilaVehiculo[]) => vehiculos.filter((v) => v.cambio_propietario === true).length;
