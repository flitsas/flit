import type { RegistroRenderers } from "@/components/tramite/ContextoWizard";
import { SeccionPendiente } from "./SeccionPendiente";

/**
 * Registro de renderers por `section_type` (catálogo cerrado de FLIT 2) y de los pasos propios del portal. Es el ÚNICO lugar donde
 * el front decide qué sabe pintar: un tipo cuya definición traiga un `section_type` que no esté aquí se muestra como no
 * disponible entero (por ejemplo `signature_fur`, que no tiene diseño todavía).
 *
 * Todas apuntan al marcador accesible hasta que lleguen sus renderers (F2: consulta de vehículo; F3 a F6, K5 y K6).
 */
export const REGISTRO_RENDERERS: RegistroRenderers = {
  vehicle_query: SeccionPendiente,
  plate_request: SeccionPendiente,
  actor_form: SeccionPendiente,
  biometric: SeccionPendiente,
  commercial: SeccionPendiente,
  prenda_decision: SeccionPendiente,
  document_checklist: SeccionPendiente,
  generic_form: SeccionPendiente,
  // Pasos propios del portal.
  pago: SeccionPendiente,
  envio: SeccionPendiente,
};
