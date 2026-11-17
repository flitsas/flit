/** Clases compartidas de navegación y botones secundarios en consolas admin (HU #12732 E.4). */

/** Enlace «Volver…» — tinta legible por token, focus ring preexistente. */
export const ADMIN_BACK_LINK_CLS =
  "flex w-fit items-center gap-1.5 text-xs font-semibold text-[color:var(--flit-brand-ink)] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 rounded-md";

/** Botón secundario outline azul corporativo (D7). */
export const ADMIN_BRAND_OUTLINE_BTN_CLS =
  "inline-flex min-h-9 items-center gap-1.5 rounded-full border-0 bg-[color:var(--kit-tinta)] " +
  "px-[18px] py-1.5 text-sm font-semibold text-white hover:brightness-90 " +
  "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--kit-azul-relleno)]";

/** Contenedor de contenido admin sobre superficie plana (sin card de layout). */
export const ADMIN_CONTENT_SURFACE_CLS = "flex flex-1 flex-col";
