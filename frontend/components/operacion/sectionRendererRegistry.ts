import type { ActorRol, WizardSectionType, WizardStep } from '@/lib/api/types/procedure-runtime';

/**
 * Claves de cuerpo de paso que el asistente sabe renderizar.
 *
 * No son los `section_type` del catálogo ni las `key` del backend: son las ramas de `StepBody`. El
 * registry existe justamente para que esas tres cosas dejen de tener que coincidir.
 */
export type StepBodyKind =
  | 'consulta'
  | 'documentos'
  | 'actores'
  | 'prenda'
  | 'identidad'
  | 'fur'
  | 'generico';

/**
 * SectionRendererRegistry (CFD-09) — traduce el `section_type` parametrizado del tipo al cuerpo de
 * paso que lo pinta.
 *
 * Antes el asistente elegía el cuerpo con un `switch` sobre `step.key`, que solo conocía las siete
 * claves de matrícula y traspaso; un paso de otra familia —`propietario`, `prenda`— caía en el
 * default y no pintaba nada. Con el registry, un tipo nuevo se dibuja en cuanto está parametrizado.
 *
 * `commercial` y `plate_request` no tienen cuerpo propio: los datos comerciales viven dentro del
 * paso de Requisitos y la solicitud de placa ocurre después de la entrega, fuera del asistente.
 */
const REGISTRY: Record<WizardSectionType, StepBodyKind> = {
  vehicle_query: 'consulta',
  document_checklist: 'documentos',
  actor_form: 'actores',
  biometric: 'identidad',
  signature_fur: 'fur',
  commercial: 'documentos',
  // La decisión de prenda tiene cuerpo PROPIO. Caía en `documentos`, y como los tipos de prenda de
  // la familia OTROS traen los dos pasos (`documentos` y `prenda`), el asistente pintaba el paso de
  // documentos DOS VECES: el gestor veía el checklist completo otra vez donde esperaba el gravamen.
  // En matrícula y traspaso no cambia nada: sus recorridos no tienen sección `prenda_decision` —la
  // prenda vive dentro del paso de requisitos, que es donde sigue.
  prenda_decision: 'prenda',
  plate_request: 'generico',
  generic_form: 'generico',
};

/**
 * Claves heredadas de matrícula y traspaso. Se conservan para los expedientes cuyo estado del
 * asistente aún no trae `sectionType`; desaparecen cuando todos los tipos vengan parametrizados.
 */
const LEGACY_KEYS: Record<string, StepBodyKind> = {
  consulta: 'consulta',
  consulta_vin: 'consulta',
  documentos: 'documentos',
  // Los datos comerciales dejaron de tener paso propio: viven en Requisitos. La clave solo puede
  // llegar desde un borrador antiguo que quedó apuntando ahí.
  comercial: 'documentos',
  comprador: 'actores',
  vendedor: 'actores',
  propietario: 'actores',
  locatario: 'actores',
  prenda: 'prenda',
  identidad: 'identidad',
  fur: 'fur',
};

/** Cuerpo de paso que corresponde a un paso del asistente. */
export function resolveStepBody(step: Pick<WizardStep, 'key' | 'sectionType'>): StepBodyKind {
  if (step.sectionType && step.sectionType in REGISTRY) {
    return REGISTRY[step.sectionType];
  }
  return LEGACY_KEYS[step.key] ?? 'generico';
}

/**
 * Rol de actor que le toca a un paso de actores. El backend nombra la sección con el rol
 * (COMPRADOR / VENDEDOR / LOCATARIO), y en la familia OTROS el titular se persiste como comprador
 * aunque el paso se titule "Propietario".
 *
 * <p>Resuelve por la CLAVE del paso y con `comprador` de respaldo — a propósito. Lo natural sería
 * leer el código de sección, pero eso cambiaría cómo se resuelve el rol de TODOS los tipos ya en
 * operación, y esta función es portante: de ella salen el guardado del paso y la parte a la que se
 * le asegura la identidad. Añadir claves es aditivo y no toca ningún recorrido existente.</p>
 */
export function resolveActorRole(step: Pick<WizardStep, 'key'>): ActorRol {
  if (step.key === 'vendedor') return 'vendedor';
  if (step.key === 'locatario') return 'locatario';
  return 'comprador';
}
