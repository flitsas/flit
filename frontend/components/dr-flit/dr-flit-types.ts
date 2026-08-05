import type { EstadoTramite } from "@/lib/tramites/estados";

export interface DrFlitTramiteResult {
  id: string;
  fecha: string;
  /** Estado de negocio (string tolerante; chips usan fallback). */
  estado: EstadoTramite | string;
  placa: string;
  vin: string;
  tipoTramite: string;
  href: string;
}

export interface DrFlitValidacionResult {
  id: string;
  name: string;
  documentType: string;
  documentNumber: string;
  status: string;
  createdAt: string;
  instanceId: string | null;
  /** Deep-link al módulo Validaciones (filtro por documento). */
  href: string;
  /** Si hay trámite ligado, enlace al wizard. */
  tramiteHref: string | null;
}
