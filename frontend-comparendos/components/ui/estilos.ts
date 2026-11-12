// Clases comunes de formularios y botones de Comparendos (mismo lenguaje visual que los controles de trámites).
import { buttonClass } from "@flit/ui/Button";
export { controlCls, TRAMITES_CONTROL_CLS as CONTROL_CLS } from "@/components/control-styles";

export const INPUT_CLS =
  "h-9 w-full rounded-xl border border-border bg-card px-3 text-xs text-foreground focus:border-flit-brand focus:outline-none " +
  "focus:ring-2 focus:ring-flit-brand/30 disabled:opacity-60";

export const TEXTAREA_CLS =
  "w-full rounded-xl border border-border bg-card px-3 py-2 text-xs text-foreground focus:border-flit-brand focus:outline-none " +
  "focus:ring-2 focus:ring-flit-brand/30";

// Botones de la propuesta (HU #14486): la familia `.btn` de @flit/ui/Button, en la densidad de 36 px de esta app.
export const BOTON_PRIMARIO_CLS = buttonClass({ size: "sm", className: "gap-1.5" });

// Acción destructiva: la propuesta no admite rojo ni naranja de fondo; va como secundario con el aro y el texto de
// error, como la pastilla de error.
export const BOTON_PELIGRO_CLS = buttonClass({
  variant: "secondary",
  size: "sm",
  className:
    "gap-1.5 border-[color:var(--di-alerta-punto)] text-[color:var(--di-danger)] hover:bg-[color:var(--di-danger-soft)]",
});

export const ETIQUETA_CLS = "flex flex-col gap-1 text-[11px] font-semibold text-muted-foreground";

export const TITULO_CLS = "text-2xl font-semibold text-flit-primary";
