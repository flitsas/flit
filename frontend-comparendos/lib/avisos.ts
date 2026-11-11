// Envío manual de avisos al conductor (HU #13485 sobre la API de AvisosEndpoints). El 4 sale con el cambio de
// contraventor y no se envía desde aquí.

export const TIPOS_AVISO = [
  { valor: "Aviso1", etiqueta: "Aviso 1 (inicial)" },
  { valor: "Aviso2", etiqueta: "Aviso 2 (recordatorio)" },
  { valor: "Aviso3", etiqueta: "Aviso 3 (último)" },
] as const;

export type TipoAvisoManual = (typeof TIPOS_AVISO)[number]["valor"];

/** Lo que responde GET /comparendos/{id}/avisos/permitidos: la misma regla de secuencia que aplica el envío. */
export interface AvisosPermitidos {
  estadoNotificacion: string;
  ventana: string;
  permitidos: string[];
  avisos: { tipo: string; motivo: string | null }[];
}

/** El aviso que se propone: el más avanzado de los que proceden hoy (null si no procede ninguno). */
export function avisoPorDefecto(permitidos: readonly string[]): TipoAvisoManual | null {
  const validos = TIPOS_AVISO.map((t) => t.valor).filter((v) => permitidos.includes(v));
  return validos.length > 0 ? validos[validos.length - 1]! : null;
}

/** Solo los avisos que proceden; el que ya recibió el conductor se marca como reenvío. */
export function opcionesAviso(permitidos: readonly string[], estado: string | null | undefined) {
  return TIPOS_AVISO.filter((t) => permitidos.includes(t.valor)).map((t) => ({
    valor: t.valor,
    etiqueta: t.valor === estado ? `${t.etiqueta}: reenviar` : t.etiqueta,
  }));
}

/** Para el envío masivo: los avisos que proceden en al menos un comparendo de la selección, con cuántos. */
export function opcionesMasivo(cuenta: readonly { tipo: string; aplica: number }[], total: number) {
  return TIPOS_AVISO.flatMap((t) => {
    const aplica = cuenta.find((c) => c.tipo === t.valor)?.aplica ?? 0;
    return aplica > 0 ? [{ valor: t.valor, etiqueta: `${t.etiqueta} (procede en ${aplica} de ${total})`, aplica }] : [];
  });
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
