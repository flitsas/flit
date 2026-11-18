// Clases propias del formulario de parametrización (HU #13752); el resto se reutiliza de components/configuracion/estilos.
// Todo valor visual sale de los tokens de @flit/ui (tokens.css): rojo/naranja de alerta para el error de campo, chip
// informativo azul para «Dependiente» y tooltip navy (flit-primary) con texto blanco.
export const ERROR_CLS = "text-xs font-medium text-[var(--badge-danger-fg)]";

export const INSIGNIA_CLS =
  "rounded-full border border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] px-2 py-0.5 text-xs font-semibold text-[var(--badge-info-fg)]";

export const TOOLTIP_CLS =
  "pointer-events-none absolute left-0 top-full z-10 mt-1 w-max max-w-64 rounded-xl bg-flit-primary px-3 py-2 text-xs font-medium text-primary-foreground opacity-0 shadow-[var(--kit-sombra)] transition group-focus-within:opacity-100 group-hover:opacity-100";

// HU #14583 (spec-ui §2.2): contador «N de M fuentes» del Alcance, pastilla informativa en la línea del título.
export const CONTADOR_CLS =
  "rounded-full border border-[var(--badge-info-border)] bg-[var(--badge-info-bg)] px-3 py-1 text-sm font-semibold text-[var(--badge-info-fg)]";

// HU #14583 (spec-ui §2.3): la misma rejilla fija en todos los grupos del Alcance (4 = máximo de fuentes por grupo, GRUPOS),
// por container query, para que todas las tarjetas midan igual aunque un grupo tenga menos fuentes. Sin auto-fit.
export const REJILLA_FUENTES_CLS = "grid grid-cols-1 gap-2 @[22rem]:grid-cols-2 @[40rem]:grid-cols-4";

export const CAMPO_INVALIDO_CLS = "aria-[invalid=true]:border-[var(--badge-danger-border)]";
