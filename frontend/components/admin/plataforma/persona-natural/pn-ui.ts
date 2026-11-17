// Clases compartidas de las pestañas de Plataforma > Persona Natural (Épica #14021, HU #14074 a #14076).
// Mismos tokens y estados de foco que los formularios de las demás consolas admin (línea base de globals.css).

export const PN_INPUT_CLS =
  "h-11 w-full min-w-0 rounded-[10px] border border-border bg-background px-3 text-sm text-flit-primary " +
  "focus:border-flit-brand focus:outline-none focus:ring-2 focus:ring-inset focus:ring-flit-brand " +
  "disabled:bg-secondary disabled:opacity-80";
export const PN_INPUT_ERROR_CLS = "border-destructive ring-1 ring-inset ring-destructive";
export const PN_LABEL_CLS = "flex flex-col gap-1.5 text-sm font-semibold text-flit-primary";
export const PN_HINT_CLS = "text-xs font-normal text-muted-foreground";
export const PN_FIELD_ERROR_CLS = "text-xs font-semibold text-destructive";
export const PN_CANCEL_BTN_CLS =
  "rounded-full border border-border px-5 py-2.5 text-sm font-semibold text-flit-primary hover:bg-secondary " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 " +
  "disabled:opacity-70";
export const PN_PRIMARY_BTN_CLS =
  "inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white " +
  "shadow-[var(--shadow-button)] focus-visible:outline-none focus-visible:ring-2 " +
  "focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70";
export const PN_PRIMARY_BTN_STYLE = {
  background: "var(--gradient-action)",
} as const;
export const PN_DANGER_BTN_STYLE = {
  background: "var(--destructive)",
  color: "var(--destructive-foreground)",
} as const;
export const PN_NOTICE_CLS = "rounded-[10px] border px-3 py-2 text-xs font-semibold";
export const PN_NOTICE_STYLE = {
  background: "var(--badge-warning-bg)",
  color: "var(--badge-warning-fg)",
  borderColor: "var(--badge-warning-border)",
} as const;
export const PN_INFO_NOTICE_CLS =
  "rounded-[10px] border border-border bg-secondary px-3 py-2 text-xs text-flit-primary";
