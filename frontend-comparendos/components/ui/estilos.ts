// Clases comunes de formularios y botones de Comparendos (mismo lenguaje visual que los controles de trámites).
import { buttonClass } from "@flit/ui/Button";
export { controlCls, TRAMITES_CONTROL_CLS as CONTROL_CLS } from "@/components/control-styles";

export const INPUT_CLS =
  "h-10 w-full rounded-[9px] border border-input bg-card px-3 text-sm text-foreground placeholder:text-[color:var(--kit-gris)] " +
  "focus:border-[color:var(--di-action)] focus:shadow-[var(--kit-sombra)] focus:outline-none disabled:opacity-60";

export const TEXTAREA_CLS =
  "min-h-[88px] w-full rounded-[9px] border border-input bg-card px-3 py-2 text-sm text-foreground " +
  "focus:border-[color:var(--di-action)] focus:shadow-[var(--kit-sombra)] focus:outline-none";

// Botones de la propuesta (HU #14486): la familia `.btn` de @flit/ui/Button, en la densidad de 36 px de esta app.
export const BOTON_PRIMARIO_CLS = buttonClass({ size: "sm", className: "gap-1.5" });

// Acción destructiva: el botón naranja de alerta del Manual UI Kit 2026.
export const BOTON_PELIGRO_CLS = buttonClass({ variant: "alerta", size: "sm", className: "gap-1.5" });

export const ETIQUETA_CLS = "flex flex-col gap-1 text-[11px] font-semibold text-muted-foreground";

export const TITULO_CLS = "text-2xl font-semibold text-flit-primary";
