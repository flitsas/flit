// Clases compartidas de la pantalla 10 (HU #13744). Todo valor visual sale de los tokens de @flit/ui (tokens.css):
// tarjeta blanca con borde gris de marca, CTA en pastilla con la tinta AA del azul de marca y foco visible del dock.
export const TARJETA_CLS =
  "rounded-2xl border border-border bg-card px-6 py-5 shadow-[var(--shadow-card)]";

export const FOCO_CLS = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--nav-focus)]";

export const BOTON_PRIMARIO_CLS = `inline-flex items-center justify-center gap-1.5 rounded-full bg-[var(--flit-brand-ink)] px-5 py-2 text-sm font-semibold text-primary-foreground transition disabled:cursor-not-allowed disabled:opacity-50 ${FOCO_CLS}`;

export const BOTON_SECUNDARIO_CLS = `inline-flex items-center justify-center gap-1.5 rounded-full border border-border bg-card px-4 py-2 text-sm font-semibold text-flit-primary transition disabled:cursor-not-allowed disabled:opacity-50 ${FOCO_CLS}`;

export const BOTON_REINTENTAR_CLS = `rounded-full border border-current px-3 py-1 text-xs font-semibold ${FOCO_CLS}`;

export const CAMPO_CLS = `h-10 w-full rounded-xl border border-input bg-card px-3 text-sm text-flit-primary disabled:opacity-60 ${FOCO_CLS}`;

export const ETIQUETA_CLS = "text-sm font-semibold text-flit-primary";

export const AYUDA_CLS = "text-xs text-[var(--nav-texto)]";

export const OPCION_CLS =
  "flex items-center gap-2 rounded-xl border border-border px-3 py-2 text-sm text-flit-primary has-[:disabled]:opacity-60";

export const MARCA_CLS = `h-4 w-4 accent-[var(--flit-brand-ink)] ${FOCO_CLS}`;
