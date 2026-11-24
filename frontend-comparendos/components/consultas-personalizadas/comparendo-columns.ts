// Columnas de la consulta de comparendos (fila `ComparendoQueryRow`, contrato §4), agrupadas como el catálogo §5.
//
// - El id de cada columna es el NOMBRE DE LA PROPIEDAD de la fila en camelCase (`numero`, `fechaLimiteGestion`…): así
//   vienen las `definition.columnas` de las consultas del sistema.
// - Los estados llegan como el VALOR de la opción (`EnTiempo`, `Aviso2`…). La etiqueta de negocio sale del catálogo
//   (`/fields`, `options[].label`), en pantalla y en el Excel. Si el catálogo aún no llegó, se usan las etiquetas de la
//   bandeja (`lib/etiquetas`). El valor crudo nunca se muestra.
// - `value` es lo que se ve y lo que va al CSV; `raw`, el valor tipado del Excel (números como números, fechas como fechas).

import type { StatusTone } from "@flit/ui";
import type { ComparendoQueryRow, QueryField } from "@/lib/api/consultas-personalizadas";
import {
  CAMBIO_CONTRAVENTOR,
  CONSULTA_CONTRAVENTOR,
  ESTADO_DP,
  ESTADO_LEGAL,
  estadoLegal,
  RESULTADO_DP,
  ULTIMO_AVISO,
  VENTANA_DESCUENTO,
  type Etiqueta,
} from "@/lib/etiquetas";
import { formatoFecha, formatoPesos } from "@/lib/format/date";
import type { XlsxCell, XlsxDate } from "@/lib/xlsx";
import type { ColumnPreset, DataColumn } from "./columns";

type Fila = ComparendoQueryRow;
type Col = DataColumn<Fila>;
type Clave = keyof Fila;

const RAYA = "—";

/**
 * «2026-10-09» → fecha de Excel, leída TAL CUAL. `bogotaDay` de `lib/xlsx` parte de un instante, y `new Date("2026-10-09")`
 * es la medianoche UTC: en Bogotá eso cae el 8. Las fechas de esta fila ya vienen en día de Colombia.
 */
export function diaXlsx(valor: string | null | undefined): XlsxDate | null {
  const m = valor ? /^(\d{4})-(\d{2})-(\d{2})/.exec(valor) : null;
  return m ? { year: Number(m[1]), month: Number(m[2]), day: Number(m[3]) } : null;
}

const textoDe = (mapa: Record<string, Etiqueta>): Record<string, string> =>
  Object.fromEntries(Object.entries(mapa).map(([k, v]) => [k, v.texto]));

/**
 * Por estado: el campo del catálogo §5 que trae sus etiquetas y, de respaldo, las de la bandeja (si `/fields` aún no
 * respondió o le falta una opción).
 */
const ESTADOS: Partial<Record<Clave, { campo: string; respaldo: Record<string, string> }>> = {
  estadoLegal: { campo: "estado_legal", respaldo: textoDe(ESTADO_LEGAL) },
  ventanaDescuento: { campo: "ventana_descuento", respaldo: textoDe(VENTANA_DESCUENTO) },
  estadoGestion: {
    campo: "estado_gestion",
    respaldo: { SinGestion: "Sin gestión", DpEnviado: "Derecho de petición enviado", RespuestaOt: "Con respuesta del organismo" },
  },
  dpEstado: { campo: "dp_estado", respaldo: textoDe(ESTADO_DP) },
  dpSentido: { campo: "dp_sentido", respaldo: textoDe(RESULTADO_DP) },
  estadoNotificacionConductor: { campo: "notificacion_conductor", respaldo: textoDe(ULTIMO_AVISO) },
  estadoCambioContraventor: { campo: "cambio_contraventor", respaldo: textoDe(CAMBIO_CONTRAVENTOR) },
  estadoConsultaContraventor: { campo: "consulta_contraventor", respaldo: textoDe(CONSULTA_CONTRAVENTOR) },
  pagoTipo: { campo: "pago_tipo", respaldo: { CC: "CC · cambio de contraventor", PC: "PC · pagó el contraventor", BC: "BC · banco / empresa" } },
};

/**
 * Etiqueta de negocio de un estado. Último recurso, un valor que nadie conoce: se separa la palabra («SinGestion» →
 * «Sin gestion») en vez de enseñar el identificador pegado.
 */
export function etiquetaEstado(clave: Clave, valor: string | null | undefined, fields: QueryField[]): string | null {
  if (!valor) return null;
  const def = ESTADOS[clave];
  if (!def) return valor;
  const opcion = fields.find((f) => f.id === def.campo)?.options.find((o) => o.value === valor);
  if (opcion) return opcion.label;
  if (def.respaldo[valor]) return def.respaldo[valor];
  const separado = valor.replace(/([a-záéíóúñ0-9])([A-Z])/g, "$1 $2").toLowerCase();
  return separado.charAt(0).toUpperCase() + separado.slice(1);
}

const G = {
  id: "Identificación",
  inf: "Infracción",
  org: "Organismo",
  plazos: "Fechas y plazos",
  valor: "Valores",
  estado: "Estado",
  dp: "Derecho de petición",
  conductor: "Conductor",
  pago: "Pago",
} as const;

type Tipo = "texto" | "fecha" | "pesos" | "siNo" | "estado";

/** [id = propiedad de la fila, etiqueta, grupo, tipo, ancho en Excel, visible al abrir]. Orden canónico (también el del archivo). */
const DEFINICION: [Clave, string, string, Tipo, number?, boolean?][] = [
  ["numero", "No. comparendo", G.id, "texto", 22, true],
  ["placa", "Placa", G.id, "texto", 10, true],
  ["nitMonitoreado", "NIT monitoreado", G.id, "texto", 14],
  ["titular", "Titular", G.id, "texto", 28],
  ["empresaNombre", "Empresa", G.id, "texto", 24],

  ["codigoInfraccion", "Infracción", G.inf, "texto", 10, true],
  ["descripcionInfraccion", "Descripción de la infracción", G.inf, "texto", 40],
  ["esFotomulta", "Fotomulta", G.inf, "siNo"],
  ["tipoVehiculo", "Tipo de vehículo", G.inf, "texto", 16],
  ["servicioVehiculo", "Servicio del vehículo", G.inf, "texto", 16],

  ["organismoNombre", "Organismo de tránsito", G.org, "texto", 30, true],
  ["departamento", "Departamento", G.org, "texto", 16],
  ["municipio", "Municipio", G.org, "texto", 16],

  ["fechaImposicion", "Imposición", G.plazos, "fecha", 13, true],
  ["fechaNotificacion", "Notificación", G.plazos, "fecha"],
  ["fechaIngreso", "Ingreso a la plataforma", G.plazos, "fecha"],
  ["fechaLimiteGestion", "Límite de gestión", G.plazos, "fecha"],
  ["fechaFinDescuento50", "Fin del descuento 50 %", G.plazos, "fecha"],
  ["fechaFinDescuento25", "Fin del descuento 25 %", G.plazos, "fecha"],
  ["fechaCaducidad", "Caducidad", G.plazos, "fecha"],
  ["fechaPrescripcion", "Prescripción", G.plazos, "fecha"],

  ["valorCapital", "Valor capital", G.valor, "pesos"],
  ["valorAdicional", "Valor adicional", G.valor, "pesos"],
  ["valorTotal", "Valor total", G.valor, "pesos", 14, true],

  ["activo", "Activo", G.estado, "siNo"],
  ["estadoLegal", "Estado legal", G.estado, "estado", 16, true],
  ["ventanaDescuento", "Descuento vigente", G.estado, "estado", 16],
  ["enCobroCoactivo", "Cobro coactivo", G.estado, "siNo"],
  ["estadoGestion", "Estado de gestión", G.estado, "estado", 26, true],
  ["gestionado", "Gestionado", G.estado, "siNo"],
  ["pagado", "Pagado", G.estado, "siNo"],

  ["tieneDp", "Tiene DP", G.dp, "siNo"],
  ["dpEstado", "Estado DP", G.dp, "estado", 14, true],
  ["dpRadicado", "Radicado", G.dp, "texto", 20],
  ["dpTipologia", "Tesis", G.dp, "texto", 24],
  ["dpEnvioAutomatico", "Enviado automáticamente", G.dp, "siNo"],
  ["dpFechaRadicacion", "Radicación del DP", G.dp, "fecha"],
  ["dpSentido", "Respuesta del organismo", G.dp, "estado", 16],
  ["dpFechaRespuesta", "Fecha de respuesta", G.dp, "fecha"],

  ["estadoNotificacionConductor", "Avisos al conductor", G.conductor, "estado", 14],
  ["notificadoFisicamente", "Notificado físicamente", G.conductor, "siNo"],
  ["estadoCambioContraventor", "Cambio de contraventor", G.conductor, "estado", 18],
  ["estadoConsultaContraventor", "Consulta del contraventor", G.conductor, "estado", 26],

  ["pagoTipo", "Tipo de pago", G.pago, "estado", 24],
  ["pagoValor", "Valor pagado", G.pago, "pesos"],
  ["pagoFecha", "Fecha de pago", G.pago, "fecha"],
];

function columna([id, label, group, tipo, ancho, visible]: (typeof DEFINICION)[number], fields: QueryField[]): Col {
  const base = { id, label, group, defaultVisible: visible ?? false };
  switch (tipo) {
    case "fecha": {
      const get = (f: Fila) => f[id] as string | null;
      return { ...base, width: ancho ?? 13, value: (f) => formatoFecha(get(f)), raw: (f) => diaXlsx(get(f)) };
    }
    case "pesos": {
      const get = (f: Fila) => f[id] as number | null;
      return { ...base, numeric: true, width: ancho ?? 14, xlsxHeader: `${label} (COP)`, value: (f) => formatoPesos(get(f)), raw: (f): XlsxCell => get(f) };
    }
    case "siNo": {
      const get = (f: Fila) => f[id] as boolean | null;
      const texto = (f: Fila) => (get(f) === null || get(f) === undefined ? null : get(f) ? "Sí" : "No");
      return { ...base, width: ancho, value: (f) => texto(f) ?? RAYA, raw: (f) => texto(f) };
    }
    case "estado": {
      const texto = (f: Fila) => etiquetaEstado(id, f[id] as string | null, fields);
      return { ...base, width: ancho, value: (f) => texto(f) ?? RAYA, raw: (f) => texto(f) };
    }
    default: {
      const get = (f: Fila) => {
        const v = f[id] as string | null;
        return v && v.trim() ? v : null;
      };
      // Sin dato: «—» en pantalla, celda vacía en el Excel (un guion volvería texto la columna).
      return { ...base, width: ancho, value: (f) => get(f) ?? RAYA, raw: (f) => get(f) };
    }
  }
}

/** Todas las columnas, con las etiquetas de estado del catálogo que llegó de `/fields`. */
export function comparendoColumns(fields: QueryField[], opciones: { conEmpresa: boolean }): Col[] {
  return DEFINICION.filter(([id]) => opciones.conEmpresa || id !== "empresaNombre").map((d) => columna(d, fields));
}

/** Lo que se ve al abrir: número, placa, infracción, organismo, imposición, valor total, estado legal, gestión y DP. */
export const COMPARENDO_DEFAULT_COLUMNS: string[] = DEFINICION.filter((d) => d[5]).map((d) => d[0]);

export const COMPARENDO_PRESETS: ColumnPreset[] = [
  { id: "basica", label: "Básica", hint: "Las columnas con las que abre la consulta.", columns: COMPARENDO_DEFAULT_COLUMNS },
  {
    id: "plazos",
    label: "Plazos",
    hint: "Qué vence y cuándo: descuentos, caducidad y prescripción.",
    columns: ["numero", "placa", "fechaImposicion", "fechaNotificacion", "fechaLimiteGestion", "fechaFinDescuento50", "fechaFinDescuento25", "fechaCaducidad", "fechaPrescripcion", "estadoLegal", "valorTotal"],
  },
  {
    id: "dp",
    label: "Derechos de petición",
    hint: "Radicado, tesis y respuesta del organismo.",
    columns: ["numero", "placa", "organismoNombre", "dpEstado", "dpRadicado", "dpTipologia", "dpFechaRadicacion", "dpSentido", "dpFechaRespuesta", "valorTotal"],
  },
  {
    id: "pago",
    label: "Pagos",
    hint: "Quién pagó, cuánto y cuándo.",
    columns: ["numero", "placa", "valorTotal", "pagado", "pagoTipo", "pagoValor", "pagoFecha"],
  },
];

/** Tono del estado legal para la insignia de la tabla. */
export function tonoEstadoLegal(v: string | null): StatusTone {
  return estadoLegal(v).tono;
}
