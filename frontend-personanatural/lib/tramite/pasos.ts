// Pasos del wizard a partir de la definición del tipo (GET /tipos-tramite/{code}/definicion). Módulo puro, sin React ni red.
//
// El portal muestra SIEMPRE los mismos cinco pasos (Diagnóstico, Actores, Requisitos, Pago y Envío); lo que cambia por tipo
// es lo que se pinta dentro de cada uno. Cada sección de la definición (`section_type` del catálogo cerrado de FLIT 2) cae en
// uno de esos pasos y la dibuja su renderer del registro. Aquí no hay ningún tipo de trámite nombrado: todo sale de la
// definición. Un `section_type` sin renderer vuelve al tipo «no disponible» entero; nunca se pinta una parte.
import type { DefinicionTipo, PasoDefinicion, Pendiente, Solicitud } from "@/lib/api/cliente";

export type GrupoPaso = "diagnostico" | "actores" | "requisitos" | "pago" | "envio";

export interface PasoBase {
  id: GrupoPaso;
  /** Nombre en el navegador de pasos: «Paso n · {titulo}». */
  titulo: string;
  /** Nombre en las migas (el último paso se muestra como «Confirmación»). */
  miga: string;
  /** Pago y Envío son del portal: no vienen de la definición del tipo. */
  propio: boolean;
}

export const PASOS_BASE: readonly PasoBase[] = [
  { id: "diagnostico", titulo: "Diagnóstico", miga: "Diagnóstico", propio: false },
  { id: "actores", titulo: "Actores", miga: "Actores", propio: false },
  { id: "requisitos", titulo: "Requisitos", miga: "Requisitos", propio: false },
  { id: "pago", titulo: "Pago", miga: "Pago", propio: true },
  { id: "envio", titulo: "Envío", miga: "Confirmación", propio: true },
];

/** Paso del portal al que pertenece cada `section_type` de FLIT 2. La validación de identidad vive dentro de la tarjeta de actor. */
export const GRUPO_POR_SECCION: Readonly<Record<string, GrupoPaso>> = {
  vehicle_query: "diagnostico",
  plate_request: "diagnostico",
  actor_form: "actores",
  biometric: "actores",
  commercial: "requisitos",
  prenda_decision: "requisitos",
  document_checklist: "requisitos",
  generic_form: "requisitos",
};

/** Una sección que hay que pintar: su `section_type` y el paso de la definición que la declara (con sus campos). */
export interface SeccionWizard {
  /** Estable entre renders: `{paso de la definición}:{section_type}`, o el del paso propio. */
  clave: string;
  sectionType: string;
  paso: PasoDefinicion | null;
}

export interface PasoWizard extends PasoBase {
  secciones: SeccionWizard[];
}

export type ResultadoPasos = { ok: true; pasos: PasoWizard[] } | { ok: false; motivo: string };

/**
 * Arma los pasos del wizard. `soportados` son los `section_type` con renderer (las llaves del registro). Con una definición no
 * disponible, sin pasos o con una sección que el portal no sabe pintar, devuelve `ok: false` con el motivo.
 */
export function armarPasos(definicion: DefinicionTipo, soportados: ReadonlySet<string>): ResultadoPasos {
  if (!definicion.disponible) return { ok: false, motivo: definicion.motivoNoDisponible || "no_disponible" };
  if (definicion.pasos.length === 0) return { ok: false, motivo: "sin_pasos" };

  const porGrupo = new Map<GrupoPaso, SeccionWizard[]>();
  for (const paso of definicion.pasos) {
    for (const sectionType of paso.sectionTypes) {
      const grupo = GRUPO_POR_SECCION[sectionType];
      if (!grupo || !soportados.has(sectionType)) return { ok: false, motivo: "seccion_desconocida" };
      const lista = porGrupo.get(grupo) ?? [];
      lista.push({ clave: `${paso.clave}:${sectionType}`, sectionType, paso });
      porGrupo.set(grupo, lista);
    }
  }

  const pasos = PASOS_BASE.map<PasoWizard>((base) => ({
    ...base,
    secciones: base.propio ? [{ clave: `portal:${base.id}`, sectionType: base.id, paso: null }] : (porGrupo.get(base.id) ?? []),
  }));
  return { ok: true, pasos };
}

/** Los `section_type` de los pasos propios del portal (Pago y Envío) también tienen renderer en el registro. */
export const SECCIONES_PROPIAS: readonly string[] = PASOS_BASE.filter((p) => p.propio).map((p) => p.id);

// ── Qué falta, por paso ─────────────────────────────────────────────────────────────────────────────────────────────

const GRUPO_DE_PENDIENTE: Readonly<Record<string, GrupoPaso>> = {
  actor_requerido: "actores",
  comercial_requerido: "requisitos",
  prenda_acreedor_requerido: "requisitos",
  prenda_nit_requerido: "requisitos",
  documento_requerido: "requisitos",
  comprobante_pago_requerido: "pago",
};

/** Paso del portal al que pertenece un pendiente que informa el back. */
export function grupoDePendiente(pendiente: Pendiente, definicion: DefinicionTipo): GrupoPaso {
  const fijo = GRUPO_DE_PENDIENTE[pendiente.codigo];
  if (fijo) return fijo;
  // campo_requerido: el campo pertenece al paso de la definición; se pinta donde se pinta la primera sección de ese paso.
  const sectionType = definicion.pasos.find((p) => p.clave === pendiente.paso)?.sectionTypes[0];
  return (sectionType && GRUPO_POR_SECCION[sectionType]) || "requisitos";
}

export function pendientesDelGrupo(grupo: GrupoPaso, definicion: DefinicionTipo, solicitud: Solicitud): Pendiente[] {
  return solicitud.pendientes.filter((p) => grupoDePendiente(p, definicion) === grupo);
}

/**
 * Si el paso ya está completo según lo que el back guardó (para reanudar). El diagnóstico cuenta como hecho solo si el tipo lo
 * pide y su último resultado no tiene bloqueos. Pago y Envío no se dan por completos desde un borrador.
 */
export function pasoCompletoGuardado(paso: PasoWizard, definicion: DefinicionTipo, solicitud: Solicitud): boolean {
  if (paso.id === "envio") return false;
  if (pendientesDelGrupo(paso.id, definicion, solicitud).length > 0) return false;
  if (paso.id === "diagnostico" && paso.secciones.some((s) => s.sectionType === "vehicle_query")) return solicitud.datos.diagnostico?.puedeContinuar === true;
  return true;
}

/** Índice del primer paso que todavía no está completo: hasta ahí llegó el ciudadano. Una solicitud ya enviada queda en el último. */
export function pasoInicial(pasos: readonly PasoWizard[], definicion: DefinicionTipo, solicitud: Solicitud, editable = esEditable(solicitud.estado)): number {
  if (!editable) return pasos.length - 1;
  const primero = pasos.findIndex((p) => !pasoCompletoGuardado(p, definicion, solicitud));
  return primero === -1 ? pasos.length - 1 : primero;
}

/** Estados en los que el ciudadano todavía puede editar (los mismos que acepta el back para escribir). */
export const esEditable = (estado: string): boolean => estado === "borrador" || estado === "pendiente_validacion";
