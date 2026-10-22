import type { PrendaDecision } from '@/lib/api/types/procedure-runtime';

/**
 * Fuente única de las etiquetas de la decisión de prenda (Feature #13110, HU #13112).
 *
 * La leen el wizard (`PrendaForm`, que la reexporta para `FirmaFurStep` y `PrendaModificar`), el
 * detalle del trámite (`TramiteDetalleComercial`) y el detalle del OT (`OtDetalleTramiteVehiculo`).
 * Vive fuera de `PrendaForm` para que los detalles no arrastren un componente cliente pesado.
 */
export const PRENDA_DECISION_LABELS: Record<PrendaDecision, string> = {
  solicitar: 'Solicitar constitución de prenda',
  registrar: 'Registrar prenda',
  levantar: 'Levantar gravamen',
  omitir: 'Omitir prenda',
  sin_prenda: 'Sin prenda',
};

/**
 * Texto de ayuda permanente bajo la opción «Omitir prenda». Tono informativo: describe el efecto
 * (el gravamen sigue en el RUNT; el trámite no lo inscribe ni lo levanta), no advierte un error.
 */
export const PRENDA_OMITIR_AYUDA =
  'La prenda seguirá vigente en el RUNT. El trámite se radicará sin inscribirla ni levantarla.';
