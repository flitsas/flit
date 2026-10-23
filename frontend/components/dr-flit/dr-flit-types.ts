import type { EstadoTramite } from "@/lib/tramites/estados";

export interface DrFlitTramiteResult {
  id: string;
  /** Número de radicado (`referenceNumber`): el identificador que el usuario reconoce, no el GUID. */
  radicado: string;
  /** Instante de radicación en formato estándar de la plataforma (DD/MM/YYYY HH:mm, hora Colombia). */
  fecha: string;
  /** Estado de negocio (string tolerante; chips usan fallback). */
  estado: EstadoTramite | string;
  placa: string;
  vin: string;
  tipoTramite: string;
  /**
   * Razón social de la compañía dueña del trámite. Solo viene cuando el resultado puede mezclar
   * compañías (SuperAdmin, bandeja OT, alcance de red); en la propia compañía es `null`.
   */
  compania: string | null;
  href: string;
}

/** Sobre del resultado: `total` es el universo que cumple el criterio, `items` la página mostrada. */
export interface DrFlitTramiteSearchResult {
  items: DrFlitTramiteResult[];
  total: number;
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

export interface DrFlitHelpResult {
  slug: string;
  title: string;
  audience: string;
  summary: string;
  href: string;
  /** Fuente que respalda el artículo (norma en PDF): se ofrece como enlace aparte. */
  sourceHref?: string;
  sourceLabel?: string;
  /** Es la norma que avala la plataforma: la tarjeta lo destaca. */
  primarySource?: boolean;
}
