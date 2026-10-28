import type { RegistroRenderers } from "@/components/tramite/ContextoWizard";
import { ConsultaVehiculo } from "./ConsultaVehiculo";
import { FormularioActores, ValidacionIdentidad } from "./FormularioActores";
import { EnvioTramite } from "./EnvioTramite";
import { FormularioRequisitos } from "./FormularioRequisitos";
import { ResumenTramite } from "./ResumenTramite";
import { SeccionPendiente } from "./SeccionPendiente";

/**
 * Registro de renderers por `section_type` (catálogo cerrado de FLIT 2) y de los pasos propios del portal. Es el ÚNICO lugar donde
 * el front decide qué sabe pintar: un tipo cuya definición traiga un `section_type` que no esté aquí se muestra como no
 * disponible entero (por ejemplo `signature_fur`, que no tiene diseño todavía).
 *
 * Construido: `vehicle_query` (HU F2), `actor_form` y `biometric` (HU F3), `commercial`, `prenda_decision`, `document_checklist` y
 * `generic_form` (paso Requisitos, HU F4 y K6) y los pasos propios Resumen (HU F5, reducida) y Envío (HU F6). Solo `plate_request`
 * sigue en el marcador accesible. El cobro está fuera de esta entrega: no hay renderer de pago.
 */
export const REGISTRO_RENDERERS: RegistroRenderers = {
  vehicle_query: ConsultaVehiculo,
  plate_request: SeccionPendiente,
  actor_form: FormularioActores,
  biometric: ValidacionIdentidad,
  commercial: FormularioRequisitos,
  prenda_decision: FormularioRequisitos,
  document_checklist: FormularioRequisitos,
  generic_form: FormularioRequisitos,
  // Pasos propios del portal.
  resumen: ResumenTramite,
  envio: EnvioTramite,
};
