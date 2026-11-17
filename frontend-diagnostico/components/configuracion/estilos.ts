import { buttonClass } from "@flit/ui/Button";

// Clases compartidas de la pantalla 10 (HU #13744). Todo valor visual sale de los tokens de @flit/ui (tokens.css):
// tarjeta blanca con borde gris de marca, CTA en pastilla con la tinta AA del azul de marca y foco visible del dock.
export const TARJETA_CLS =
  "rounded-[9px] border border-border bg-card px-6 pb-6 pt-4 shadow-none";

export const FOCO_CLS = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

// Botones de la propuesta (HU #14487): la familia `.btn` de @flit/ui/Button, en la densidad de 36 px de esta app.
export const BOTON_PRIMARIO_CLS = buttonClass({ size: "sm", className: "gap-1.5 text-sm" });

export const BOTON_SECUNDARIO_CLS = buttonClass({ variant: "secondary", size: "sm", className: "gap-1.5 text-sm" });

export const BOTON_REINTENTAR_CLS = `rounded-full border border-current px-3 py-1 text-xs font-semibold ${FOCO_CLS}`;

export const CAMPO_CLS = `h-11 w-full rounded-[9px] border border-input bg-card px-3.5 text-base text-flit-primary focus:border-[color:var(--di-action)] focus:shadow-[var(--kit-sombra)] disabled:opacity-60 ${FOCO_CLS}`;

export const ETIQUETA_CLS = "text-sm font-semibold text-flit-primary";

export const AYUDA_CLS = "text-xs text-muted-foreground";

export const OPCION_CLS =
  "flex items-center gap-2 rounded-[9px] border border-border px-3 py-2 text-sm text-flit-primary has-[:checked]:border-[color:var(--di-action)] has-[:checked]:bg-[var(--kit-hover)] has-[:disabled]:opacity-60";

export const MARCA_CLS = `h-4 w-4 accent-[var(--flit-brand-ink)] ${FOCO_CLS}`;
