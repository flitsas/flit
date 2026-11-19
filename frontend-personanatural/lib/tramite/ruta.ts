/** Ruta del wizard de un tipo de trámite. El código del tipo es lo único que va en la URL: nunca cédula, placa ni radicado. */
export const rutaDelTramite = (tipoCodigo: string) => `/tramite/${encodeURIComponent(tipoCodigo)}`;
