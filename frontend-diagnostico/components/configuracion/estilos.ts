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

/**
 * Campo de fecha (HU #14577, hallazgo E2E H-7). En Chromium el `<input type="date">` tiene una parada de Tab en el botón del
 * calendario, dentro del shadow DOM del agente de usuario; con el foco ahí el host deja de casar con `:focus` y
 * `:focus-visible` y CAMPO_CLS no pinta ni contorno ni sombra. `:focus-within` sí casa, así que se repite el foco del kit
 * con esa variante. CAMPO_CLS queda igual para texto, select y textarea.
 */
export const CAMPO_FECHA_CLS = `${CAMPO_CLS} focus-within:border-[color:var(--di-action)] focus-within:shadow-[var(--kit-sombra)] focus-within:outline-2 focus-within:outline-offset-2 focus-within:outline-ring`;

export const ETIQUETA_CLS = "text-sm font-semibold text-flit-primary";

export const AYUDA_CLS = "text-xs text-muted-foreground";

export const OPCION_CLS =
  "flex items-center gap-2 rounded-[9px] border border-border px-3 py-2 text-sm text-flit-primary has-[:checked]:border-[color:var(--di-action)] has-[:checked]:bg-[var(--kit-hover)] has-[:disabled]:opacity-60";

export const MARCA_CLS = `h-4 w-4 accent-[var(--flit-brand-ink)] ${FOCO_CLS}`;

/**
 * Pastilla compacta de las acciones de una fila del tablero («Crear trámite», «Reintentar placa», la bandera de
 * saneamiento). HU #14577 (spec-ui §1.3): sale de `BotonCrearTramite.tsx` para compartirla con `BotonSanear`.
 */
export const ACCION_FILA_CLS = `inline-flex items-center gap-1 rounded-full border border-current px-2.5 py-0.5 text-xs font-semibold text-[var(--flit-brand-ink)] transition disabled:cursor-not-allowed disabled:opacity-60 ${FOCO_CLS}`;

/**
 * Enlace de texto de las acciones de celda y de barra («Abrir diagnóstico», «Cargar evidencia», «Todas las compañías»).
 * HU #14577 (UA-12, hueco H-10): una sola definición para los cinco archivos que la copiaban, con el alto mínimo de 36 px
 * de las barras densas (H-6).
 */
export const ENLACE_ACCION_CLS = `inline-flex min-h-9 items-center gap-1 rounded-full text-xs font-semibold text-[var(--flit-brand-ink)] underline underline-offset-2 ${FOCO_CLS}`;
