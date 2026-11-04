// Prenda del trámite (HU9): qué se hace con ella (registrar, levantar o sin prenda) y el documento que pide cada decisión.
// Módulo puro, sin React ni red. El documento sale de la matriz del tipo: si el tipo no lo lista, el portal no lo ofrece
// (el back no aceptaría un archivo de un tipo que la definición no trae).
import type { Adjunto, DecisionPrenda, DefinicionTipo, DocumentoRequerido } from "@/lib/api/cliente";
import { adjuntoDe, MENSAJE_FALTA_ARCHIVO } from "@/lib/tramite/requisitos";

/** Qué se hace con una prenda que sí existe (con «sin prenda» no hay nada que hacer). */
export type AccionPrenda = Exclude<DecisionPrenda, "sin_prenda">;

/** Documento que acompaña a cada decisión (mismos códigos que FLIT 2 usa para la prenda). */
export const DOC_DE_DECISION: Readonly<Record<AccionPrenda, string>> = {
  registrar: "prenda_registro",
  levantar: "prenda_levantamiento",
};

export const ETIQUETA_ACCION_PRENDA: Readonly<Record<AccionPrenda, string>> = {
  registrar: "Registrar una prenda nueva",
  levantar: "Levantar la prenda inscrita",
};

/** Códigos de los documentos propios de la prenda: se piden en su tarjeta y no se repiten en «Documentos». */
export const DOCS_DE_PRENDA: ReadonlySet<string> = new Set(Object.values(DOC_DE_DECISION));

/** La decisión efectiva: sin prenda, o la acción elegida. */
export const decisionDe = (tienePrenda: boolean, accion: AccionPrenda): DecisionPrenda => (tienePrenda ? accion : "sin_prenda");

/** Acción que se muestra al reabrir un borrador: la guardada si fue levantar; en cualquier otro caso, registrar. */
export const accionGuardada = (decision: DecisionPrenda | null | undefined): AccionPrenda => (decision === "levantar" ? "levantar" : "registrar");

/** El documento que pide la decisión, si la matriz del tipo lo trae. */
export function documentoDeDecision(d: DefinicionTipo, decision: DecisionPrenda): DocumentoRequerido | undefined {
  if (decision === "sin_prenda") return undefined;
  return d.documentos.find((doc) => doc.docCode === DOC_DE_DECISION[decision]);
}

/** Error del documento de la decisión (solo si el tipo lo marca obligatorio): llave `documento.<docCode>`, igual que el resto de documentos. */
export function errorDeDocumentoDePrenda(d: DefinicionTipo, adjuntos: readonly Adjunto[], decision: DecisionPrenda): Record<string, string> {
  const doc = documentoDeDecision(d, decision);
  return doc && doc.obligatorio && !adjuntoDe(adjuntos, doc.docCode) ? { [`documento.${doc.docCode}`]: MENSAJE_FALTA_ARCHIVO } : {};
}
