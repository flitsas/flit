// Envío manual de avisos al conductor (HU #13485 sobre la API de AvisosEndpoints). El 4 sale con el cambio de
// contraventor y no se envía desde aquí.

export const TIPOS_AVISO = [
  { valor: "Aviso1", etiqueta: "Aviso 1 (inicial)" },
  { valor: "Aviso2", etiqueta: "Aviso 2 (recordatorio)" },
  { valor: "Aviso3", etiqueta: "Aviso 3 (último)" },
] as const;

export type TipoAvisoManual = (typeof TIPOS_AVISO)[number]["valor"];

/** El siguiente aviso según el estado de notificación del conductor (SinNotificar → 1, Aviso1 → 2, Aviso2/3 → 3). */
export function siguienteAviso(estado: string | null | undefined): TipoAvisoManual {
  if (estado === "Aviso1") return "Aviso2";
  if (estado === "Aviso2" || estado === "Aviso3") return "Aviso3";
  return "Aviso1";
}

/** Por qué no se puede enviar un aviso a este comparendo (null = sí se puede intentar; la API decide el resto). */
export function motivoSinAviso(d: {
  activo: boolean;
  pagado: boolean;
  notificadoFisicamente: boolean;
  contraventor: { correo: string | null } | null;
}): string | null {
  if (!d.activo) return "El comparendo está inactivo.";
  if (d.pagado) return "El comparendo está pagado.";
  if (d.notificadoFisicamente) return "El conductor ya fue notificado físicamente.";
  if (!d.contraventor) return "Asigna primero el conductor.";
  if (!d.contraventor.correo) return "El conductor no tiene correo.";
  return null;
}

export interface ResultadoMasivo {
  comparendoId: string;
  numero?: string | null;
  enviado: boolean;
  motivo: string | null;
}

/** Resumen del envío masivo: cuántos salieron y los omitidos con su motivo. */
export function resumirMasivo(resultados: ResultadoMasivo[]) {
  const enviados = resultados.filter((r) => r.enviado).length;
  const omitidos = resultados.filter((r) => !r.enviado).map((r) => ({ numero: r.numero ?? r.comparendoId, motivo: r.motivo ?? "Sin motivo" }));
  return { enviados, omitidos };
}

export const MAXIMO_AVISOS_MASIVO = 500;
