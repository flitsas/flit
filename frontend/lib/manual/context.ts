import { MANUAL_ARTICLES } from "./articles";
import {
  MANUAL_MODULE_ARTICLES,
  MANUAL_OT_TAB_ARTICLES,
  MANUAL_PATH_ARTICLES,
  MANUAL_PATH_SUFFIX_ARTICLES,
} from "./articles/meta";
import { isVisibleFor } from "./audience";
import type { ManualArticle, ManualAudience } from "./types";

const OT_HUB_RE = /^\/admin\/transit-offices\/[^/]+\/([^/?#]+)/;

/**
 * HU-G — artículo del manual para el lugar donde está el usuario.
 *
 * `routeScope` es lo que `Shell` ya pasa a DR. FLIT: `"${pathname}|${moduleId}"`. Se resuelve en
 * este orden: pestaña del hub OT (por segmento de la URL) → ruta por sufijo (ficha de compañía /
 * red) → ruta por prefijo → módulo de la SPA (solo en `/`). Fuera de la raíz `moduleId` queda en el default (`dashboard`) y
 * sugeriría el Inicio del Gestor a un organismo o a un administrador.
 *
 * Con `audiences` el artículo se descarta si no aplica al perfil (un Gestor en una URL de OT no
 * debería ver documentación del organismo).
 */
export function resolveContextArticle(
  routeScope: string | null | undefined,
  audiences?: readonly ManualAudience[],
): ManualArticle | null {
  if (!routeScope) return null;
  const sep = routeScope.indexOf("|");
  const pathname = sep >= 0 ? routeScope.slice(0, sep) : routeScope;
  const moduleId = sep >= 0 ? routeScope.slice(sep + 1) : "";

  let slug: string | undefined;
  const otTab = pathname.match(OT_HUB_RE)?.[1];
  if (otTab) {
    slug = MANUAL_OT_TAB_ARTICLES[otTab];
  } else {
    // Sufijos primero: `/admin/companies/{id}` (ficha) y `…/children` (red) son más específicos
    // que el prefijo `/admin/companies` (catálogo del Super Admin).
    const clean = pathname.replace(/\/+$/, "");
    slug = MANUAL_PATH_SUFFIX_ARTICLES.find(
      (p) => clean.startsWith(p.prefix) && clean.length > p.prefix.length && clean.endsWith(p.suffix),
    )?.slug;
    if (!slug) slug = MANUAL_PATH_ARTICLES.find((p) => pathname.startsWith(p.prefix))?.slug;
    // Los módulos `?m=` viven solo en la raíz de la SPA: en cualquier otra página (`/admin/*`,
    // `/profile`, `/manual`) `moduleId` es el default y no dice dónde está el usuario.
    if (!slug && moduleId && pathname === "/") slug = MANUAL_MODULE_ARTICLES[moduleId];
  }
  if (!slug) return null;

  const article = MANUAL_ARTICLES.find((a) => a.slug === slug);
  if (!article || !isVisibleFor(article.audience, audiences)) return null;
  return article;
}
