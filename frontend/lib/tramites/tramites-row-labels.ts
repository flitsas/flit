/**
 * Rótulos de una fila del listado de trámites, en TEXTO PLANO.
 *
 * Viven aquí y no dentro de `TramitesTable` porque los usan DOS consumidores: la celda que se pinta
 * en pantalla y la columna que se escribe en el Excel (ver `exportFields` en
 * `tramites-table-columns.ts`). Con una copia en cada sitio, el día que cambie el rótulo de una
 * fuente o el respaldo del nombre de un paso, la tabla y el archivo exportado dirían cosas
 * distintas sobre el mismo trámite — y el archivo es justo lo que se reenvía por correo a quien no
 * puede contrastarlo con la pantalla.
 *
 * Todo lo de aquí es PURO: recibe la fila y devuelve texto (o un color de marca). Nada de JSX, para
 * que el módulo de columnas pueda importarlo sin arrastrar el componente.
 */

import type {
  FirmaParteEstado,
  InstanceSummary,
  RuntConfirmedValue,
  TramiteCanal,
  TramiteFuente,
} from '@/lib/api/types/procedure-runtime';
import type { ProcedureFamily } from '@/lib/api/types/procedure-parametrization';

export function vehiculo(item: InstanceSummary): string {
  const text = `${item.vehiculoMarca ?? ''} ${item.vehiculoLinea ?? ''}`.trim();
  return text || '—';
}

const MODALIDAD_SHORT: Record<ProcedureFamily, string> = {
  OTROS: 'Otros',
  MATRICULAS: 'Matrícula',
  TRASPASO: 'Traspaso',
};

/**
 * Qué rotula la fila del listado: el nombre del TIPO, no el de su familia.
 *
 * <p>HU #12181 — antes el tipo solo mandaba en OTROS, y en las otras dos familias se rotulaba
 * «Matrícula» o «Traspaso». Pero la familia tampoco identifica ahí: `TRASPASO_STANDARD` y
 * `TRASPASO_UNILATERAL` son trámites distintos —en el unilateral el comprador ni siquiera
 * comparece— y los dos se leían «Traspaso». El nombre del tipo es lo que el gestor reconoce, en
 * las tres familias.</p>
 *
 * <p>Respaldo a la familia si el tipo no está parametrizado o el expediente viene de un backend
 * anterior al campo: la celda nunca queda vacía, y una fila sin tipo se sigue pintando.</p>
 */
export function tramiteLabel(item: InstanceSummary): string {
  return item.tipoNombre?.trim() || MODALIDAD_SHORT[item.modalidad] || '—';
}

/**
 * HU #12183 — las marcas informativas de la fila, en el orden en que se pintan.
 *
 * <p>Prenda, transformación y múltiples propietarios (ADR-0053). El borrador del requerimiento
 * pedía «un ícono por tipo de trámite», que serían quince y ninguno diría nada que la columna
 * «Trámite» no diga ya con palabras. Estas marcan lo que NO se ve en ninguna otra columna.</p>
 *
 * <p>El rótulo no es decorativo: los íconos son círculos de color —verde, azul y turquesa— y el color no
 * puede ser el único portador del significado. Va como `alt` de la imagen, así que un lector de
 * pantalla lee «Con prenda» y quien pasa el ratón ve el mismo texto.</p>
 *
 * <p>Los SVG traen su propio círculo de color: se pintan enteros, sin pastilla ni recoloreado por
 * CSS, igual que los íconos de estado.</p>
 */
export const MARCAS_TRAMITE = [
  {
    id: 'prenda',
    label: 'Con prenda',
    src: '/assets/marcas/prenda.svg',
    lee: (item: InstanceSummary) => item.tienePrenda === true,
  },
  {
    id: 'transformacion',
    label: 'Con transformación',
    src: '/assets/marcas/transformacion.svg',
    lee: (item: InstanceSummary) => item.tieneTransformacion === true,
  },
  {
    // ADR-0053 — algún lado tiene 2+ propietarios. Como las otras dos, marca algo que la fila no
    // dice por sí sola: sin ella, un copropietario pasa desapercibido entre los nombres de la celda.
    id: 'multiples-propietarios',
    label: 'Con múltiples propietarios',
    src: '/assets/marcas/multiples-propietarios.svg',
    lee: (item: InstanceSummary) => item.multiplesPropietarios === true,
  },
] as const;

/** Marcas activas de una fila, en orden. Vacío si no tiene ninguna. */
export function marcasDe(item: InstanceSummary): readonly (typeof MARCAS_TRAMITE)[number][] {
  return MARCAS_TRAMITE.filter((marca) => marca.lee(item));
}

/**
 * Las marcas en TEXTO, para el Excel. Un archivo no puede llevar el ícono, y sin esta columna el
 * dato desaparecería justo en el sitio donde nadie puede contrastarlo con la pantalla.
 */
export function marcasLabel(item: InstanceSummary): string {
  const activas = marcasDe(item);
  return activas.length > 0 ? activas.map((m) => m.label.replace('Con ', '')).join(', ') : '';
}

/**
 * Nombres de paso por familia — RESPALDO para expedientes servidos por un backend anterior a
 * `pasoNombre`. No se amplía: desde ADR-0050 el recorrido lo define el TIPO, no la familia, así que
 * una lista por familia no puede acertar en OTROS —quince tipos con recorridos distintos— y de hecho
 * estaba VACÍA, que es por lo que esas filas mostraban «—».
 */
const STEP_LABELS_FALLBACK: Record<ProcedureFamily, string[]> = {
  OTROS: [],
  MATRICULAS: [
    'Consulta VIN',
    'Datos y Documentos del Trámite',
    'Comprador',
    'Identidad',
    'Resumen del trámite',
  ],
  TRASPASO: [
    'Consulta del vehículo',
    'Datos y Documentos del Trámite',
    'Vendedor',
    'Comprador',
    'Datos comerciales',
    'Resumen del trámite',
  ],
};

/** Rótulo del paso en curso: manda el que arma el recorrido del tipo en el servidor. */
export function stepLabel(item: InstanceSummary): string {
  return (
    item.pasoNombre?.trim() ||
    STEP_LABELS_FALLBACK[item.modalidad]?.[item.pasoActual - 1] ||
    '—'
  );
}

/**
 * Acreditación de una parte (identidad validada o firma del baúl) en la columna "Firmas".
 *
 * El diseño la dibuja como TEXTO PLANO de color, no como píldora, así que aquí solo se necesita
 * etiqueta + color, y se usan los tonos EXACTOS de la propuesta por decisión expresa del usuario.
 *
 * DEUDA DE CONTRASTE CONOCIDA: sobre blanco, `#F9AC00` da 1.9:1 y `#16A34A` 3.3:1, por debajo del
 * 4.5:1 que pide AA para texto. Se asume a sabiendas: el estado nunca depende solo del color —la
 * etiqueta lo dice— pero la legibilidad sigue siendo peor de lo que exige la norma. Se documentó
 * junto a las otras dos deudas de contraste abiertas (blanco sobre `#8CC63F` y sobre `#FF4E00`).
 */
export const FIRMA_TEXTO: Record<FirmaParteEstado, { label: string; color: string }> = {
  firmado: { label: 'Firmado', color: '#16A34A' },
  pendiente: { label: 'Sin firma', color: '#F9AC00' },
  // La propuesta no dibuja una firma rechazada, así que no hay "tono exacto" que copiar: se queda
  // el naranja de marca en su variante para texto, que sí cumple contraste.
  rechazado: { label: 'Rechazado', color: '#C2410C' },
};

/** HU #11057 — etiqueta de la columna Fuente. No hay "QX": Quipux es canal de salida, no de entrada. */
export const FUENTE_LABEL: Record<TramiteFuente, string> = {
  dashboard: 'Dashboard',
  integracion: 'Integración',
  migrado: 'Migrado',
};

/**
 * Épica #14296 (HUs #14321/#14322) — ÚNICA fuente de los nombres de canal en el frontend. Los códigos
 * son los del contrato (`TramiteCanal` en el backend / enum del OpenAPI). Ningún otro archivo escribe
 * estos textos.
 */
export const CANAL_LABEL: Record<TramiteCanal, string> = {
  tramites: 'Trámites',
  pn_web: 'Persona Natural Web',
  pn_punto: 'Persona Natural OT',
  integracion: 'Integración',
  aplicacion: 'Aplicación',
};

/** Canales en el orden en que se ofrecen en los selectores. */
export const CANALES_ORDENADOS: readonly TramiteCanal[] = [
  'tramites',
  'pn_web',
  'pn_punto',
  'integracion',
  'aplicacion',
];

/** Canal que se muestra cuando el backend no lo trae (histórico o backend anterior). */
export const CANAL_POR_DEFECTO: TramiteCanal = 'tramites';

/** Nombre del canal; un código desconocido (contrato nuevo) se muestra tal cual, sin inventar. */
export function canalLabel(canal: string | null | undefined): string {
  if (!canal) return CANAL_LABEL[CANAL_POR_DEFECTO];
  return CANAL_LABEL[canal as TramiteCanal] ?? canal;
}

/**
 * Feature #12276 (HU #12312) — texto de la columna «Confirmado en RUNT»: SÍ, NO o vacío (la celda
 * pinta «—», el Excel deja la celda vacía). Sin explicaciones: el porqué vive en el Historial de
 * plataforma, no en el listado del gestor.
 */
export const RUNT_CONFIRMED_LABEL: Record<RuntConfirmedValue, string> = {
  yes: 'SÍ',
  no: 'NO',
  not_consulted: '',
};

export function runtConfirmadoLabel(item: InstanceSummary): string {
  return item.runtConfirmed ? RUNT_CONFIRMED_LABEL[item.runtConfirmed] : '';
}
