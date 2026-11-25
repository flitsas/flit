import type { CSSProperties } from "react";
import { buttonClass } from "@flit/ui/Button";

// Recetas locales de Impuestos (spec-visual §0.2): la misma forma que las de Diagnóstico
// (frontend-diagnostico/components/configuracion/estilos.ts y components/tablero/vistasDetalle/comun.tsx), copiadas y no
// importadas (independencia.test.ts). Todo valor visual sale de los tokens de @flit/ui; la duplicación es el hueco H-2.

export const TARJETA_CLS = "rounded-[9px] border border-border bg-card px-6 pb-6 pt-4 shadow-none";

export const FOCO_CLS = "focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring";

export const BOTON_PRIMARIO_CLS = buttonClass({ size: "sm", className: "gap-1.5 text-sm" });

export const BOTON_SECUNDARIO_CLS = buttonClass({ variant: "secondary", size: "sm", className: "gap-1.5 text-sm" });

export const BOTON_REINTENTAR_CLS = `rounded-full border border-current px-3 py-1 text-xs font-semibold ${FOCO_CLS}`;

export const CAMPO_CLS = `h-11 w-full rounded-[9px] border border-input bg-card px-3.5 text-base text-flit-primary focus:border-[color:var(--di-action)] focus:shadow-[var(--kit-sombra)] disabled:opacity-60 ${FOCO_CLS}`;

/** Campo de fecha (`input type="date"`): mismo molde, con el realce al tener el foco dentro (selector nativo). */
export const CAMPO_FECHA_CLS = `${CAMPO_CLS} focus-within:border-[color:var(--di-action)] focus-within:shadow-[var(--kit-sombra)] focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring`;

/** Campo con error: borde rojo del kit cuando lleva aria-invalid. */
export const CAMPO_INVALIDO_CLS = "aria-[invalid=true]:border-[var(--badge-danger-border)]";

export const ETIQUETA_CLS = "text-sm font-semibold text-flit-primary";

export const AYUDA_CLS = "text-xs text-muted-foreground";

/** Texto del error de un campo (va en su aria-describedby). */
export const ERROR_CLS = "text-xs font-medium text-[var(--badge-danger-fg)]";

export const OPCION_CLS =
  "flex items-center gap-2 rounded-[9px] border border-border px-3 py-2 text-sm text-flit-primary has-[:checked]:border-[color:var(--di-action)] has-[:checked]:bg-[var(--kit-hover)] has-[:disabled]:opacity-60";

export const MARCA_CLS = `h-4 w-4 accent-[var(--flit-brand-ink)] ${FOCO_CLS}`;

export const ENLACE_ACCION_CLS = `inline-flex min-h-9 items-center gap-1 rounded-full text-xs font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`;

// Tabla por defecto (molde: comun.tsx de Diagnóstico).
export const CONTENEDOR_TABLA_CLS = "overflow-x-auto rounded-xl border border-border";
export const CABECERA_TABLA_CLS = "px-3 py-2 text-left text-xs font-semibold uppercase tracking-wider";
export const CABECERA_TABLA_ESTILO: CSSProperties = { background: "var(--table-header-bg)", color: "var(--table-header-fg)" };
export const CELDA_TABLA_CLS = "px-3 py-2 align-top text-xs text-flit-primary";
export const FILA_TABLA_CLS = "border-t border-[var(--table-row-border)]";
export const FILA_TABLA_HOVER_CLS =
  "bg-card transition-colors duration-[var(--di-motion-fast)] hover:bg-[var(--table-row-hover)] motion-reduce:transition-none";
export const TEXTO_SECUNDARIO_CLS = "text-xs text-muted-foreground";
