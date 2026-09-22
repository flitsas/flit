/**
 * A quién aplica un artículo («Aplica para: …»). HU-F — se añaden las consolas de administración:
 * el producto ya las tiene y DR. FLIT filtra por el perfil de quien pregunta.
 */
export type ManualAudience =
  | "Todos"
  | "Gestor"
  | "Organismo de Tránsito"
  | "Admin de Compañía"
  | "Super Admin";

/**
 * Perfil efectivo del usuario que consulta (misma nomenclatura que el rol efectivo de DR. FLIT).
 * El portal público `/manual` no filtra: muestra todo.
 */
export type ManualProfile = "gestor" | "admin_company" | "ot_admin" | "superadmin";

export type ManualCallout = {
  variant: "info" | "tip" | "warning";
  title?: string;
  text: string;
};

export type ManualSectionBlock = {
  id: string;
  title: string;
  paragraphs: string[];
  bullets?: string[];
  callouts?: ManualCallout[];
};

/**
 * Documento de respaldo de un artículo (norma, concepto jurídico, anexo). `kind: "pdf"` apunta a un
 * archivo servido por la propia aplicación (`/legal/...`); `kind: "web"` a la publicación oficial.
 */
export type ManualSource = {
  title: string;
  href: string;
  kind: "pdf" | "web";
  /** Referencia corta que se muestra junto al enlace (p. ej. «Diario Oficial 52386»). */
  ref?: string;
};

export type ManualArticle = {
  slug: string;
  title: string;
  audience: ManualAudience;
  sectionId: string;
  keywords: string[];
  summary: string;
  blocks: ManualSectionBlock[];
  /** Fuentes que respaldan el artículo; se listan al final y DR. FLIT ofrece abrir la primera. */
  sources?: ManualSource[];
  /**
   * Fuente PRINCIPAL de la plataforma (la norma que la avala). Cuando la consulta le casa, el
   * buscador la coloca primero: una duda operativa se responde con el artículo del módulo, pero
   * si la pregunta es «qué dice la norma», la norma manda.
   */
  primarySource?: boolean;
};

export type ManualNavSection = {
  id: string;
  label: string;
  order: number;
};
