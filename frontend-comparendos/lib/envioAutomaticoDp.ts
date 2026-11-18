// Lógica pura de la pantalla del envío automático de DP (HU #14614). La API vuelve a validar todo.
import { ApiError } from "@/lib/api";

export const TOPE_MINIMO = 1;
export const TOPE_MAXIMO = 500;
export const TOPE_POR_DEFECTO = 50;

/** Las únicas tipologías que el envío automático sabe decidir (contrato §2): cualquier otra solo la envía una persona. */
export const CODIGOS_AUTOMATIZABLES = ["DCI", "DBN", "DPR", "DLL", "DRP"] as const;

export function esAutomatizable(codigo: string): boolean {
  return (CODIGOS_AUTOMATIZABLES as readonly string[]).includes(codigo);
}

/** null si el tope es válido: un entero de 1 a 500. */
export function errorTope(valor: number): string | null {
  return Number.isInteger(valor) && valor >= TOPE_MINIMO && valor <= TOPE_MAXIMO
    ? null
    : `El tope diario debe ser un número entero entre ${TOPE_MINIMO} y ${TOPE_MAXIMO}.`;
}

const ERRORES: Record<string, string> = {
  CAPACIDAD_DP_INACTIVA: "Esta empresa no tiene el módulo de derechos de petición. Actívalo primero en el parametrizador de la empresa.",
  TOPE_INVALIDO: `El tope diario debe ser un número entero entre ${TOPE_MINIMO} y ${TOPE_MAXIMO}.`,
  TIPOLOGIA_NO_HABILITADA: "Esta tipología no está habilitada para la empresa. Habilítala primero en el parametrizador de la empresa.",
  TIPOLOGIA_SIN_REGLA_AUTOMATICA: "Esta tipología no tiene una regla para decidir cuándo aplica: solo la puede enviar una persona.",
  TIPOLOGIA_SIN_REVISION_JURIDICA: "El texto publicado de esta tipología todavía no está aprobado por el abogado. Apruébalo antes de automatizarla.",
  VERSION_NO_PUBLICADA: "Solo se puede aprobar un texto que ya esté publicado. Publícalo primero en el editor de plantillas.",
};

/** Error de la API → frase en español para la pantalla; los códigos que no conoce los deja con el mensaje de la API. */
export function mensajeError(e: unknown, porDefecto: string): string {
  if (e instanceof ApiError && e.code && ERRORES[e.code]) return ERRORES[e.code];
  return e instanceof Error && e.message ? e.message : porDefecto;
}

export interface EstadoTexto {
  texto: string;
  tono: "success" | "warning" | "neutral";
  /** Se puede aprobar: hay un texto publicado que nadie ha aprobado. */
  aprobable: boolean;
}

/** Estado del texto publicado de una tipología para el abogado. */
export function estadoTexto(publicada: boolean, revisadaEn: string | null | undefined): EstadoTexto {
  if (!publicada) return { texto: "Sin texto publicado", tono: "neutral", aprobable: false };
  if (!revisadaEn) return { texto: "Texto sin aprobar", tono: "warning", aprobable: true };
  return { texto: "Texto aprobado", tono: "success", aprobable: false };
}
