// Clases compartidas de la pantalla 10 (HU #13744). Todo valor visual sale de los tokens de @flit/ui (tokens.css):
// tarjeta blanca con borde gris de marca, CTA en pastilla con la tinta AA del azul de marca y foco visible del dock.
export const TARJETA_CLS =
  "rounded-2xl border border-[var(--color-flit-gray)] bg-white px-6 py-5 shadow-sm dark:border-white/[0.08] dark:bg-white/[0.04]";

export const FOCO_CLS = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--nav-focus)]";

export const BOTON_PRIMARIO_CLS = `inline-flex items-center justify-center gap-1.5 rounded-full bg-[var(--flit-brand-ink)] px-5 py-2 text-sm font-semibold text-white transition disabled:cursor-not-allowed disabled:opacity-50 ${FOCO_CLS}`;

export const BOTON_SECUNDARIO_CLS = `inline-flex items-center justify-center gap-1.5 rounded-full border border-[var(--color-flit-gray)] bg-white px-4 py-2 text-sm font-semibold text-flit-primary transition disabled:cursor-not-allowed disabled:opacity-50 dark:border-white/15 dark:bg-transparent dark:text-white ${FOCO_CLS}`;

export const BOTON_REINTENTAR_CLS = `rounded-full border border-current px-3 py-1 text-xs font-semibold ${FOCO_CLS}`;

export const CAMPO_CLS = `h-10 w-full rounded-xl border border-[var(--color-flit-gray)] bg-white px-3 text-sm text-flit-primary disabled:opacity-60 dark:border-white/15 dark:bg-transparent dark:text-white ${FOCO_CLS}`;

export const ETIQUETA_CLS = "text-sm font-semibold text-flit-primary dark:text-white";

export const AYUDA_CLS = "text-xs text-[var(--nav-texto)] dark:text-slate-300";

export const OPCION_CLS =
  "flex items-center gap-2 rounded-xl border border-[var(--color-flit-gray)] px-3 py-2 text-sm text-flit-primary has-[:disabled]:opacity-60 dark:border-white/15 dark:text-white";

export const MARCA_CLS = `h-4 w-4 accent-[var(--flit-brand-ink)] ${FOCO_CLS}`;
