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

import type { FirmaParteEstado, InstanceSummary, TramiteFuente } from '@/lib/api/types/procedure-runtime';
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
 * Qué rotula la fila del listado.
 *
 * En MATRICULAS y TRASPASO la familia identifica bien el trámite. En OTROS no: agrupa quince tipos
 * —blindaje, cambio de color, levantamiento de prenda, duplicado de tarjeta…— que se veían los tres
 * igual, «Otros», sin forma de distinguirlos sin abrirlos. Ahí manda el nombre del tipo.
 *
 * Respaldo a la familia si el expediente viene de un backend anterior al campo, para que la celda
 * nunca quede vacía.
 */
export function tramiteLabel(item: InstanceSummary): string {
  const familia = MODALIDAD_SHORT[item.modalidad];
  if (item.modalidad !== 'OTROS') return familia;
  return item.tipoNombre?.trim() || familia;
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
