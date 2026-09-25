/** Clases compartidas de navegación y botones secundarios en consolas admin (HU #12732 E.4). */

/** Enlace «Volver…» — tinta legible por token, focus ring preexistente. */
export const ADMIN_BACK_LINK_CLS =
  "flex w-fit items-center gap-1.5 text-xs font-semibold text-[color:var(--flit-brand-ink)] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 rounded-md";

/** Botón secundario outline azul corporativo (D7). */
export const ADMIN_BRAND_OUTLINE_BTN_CLS =
  "inline-flex items-center gap-1.5 rounded-xl border border-[color:var(--color-flit-brand)] " +
  "px-4 py-2 text-xs font-semibold text-[color:var(--flit-brand-ink)] " +
  "hover:bg-[#F4F7FC] dark:hover:bg-white/5 " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2";

/** Contenedor de contenido admin sobre superficie plana (sin card de layout). */
export const ADMIN_CONTENT_SURFACE_CLS = "flex flex-1 flex-col";
