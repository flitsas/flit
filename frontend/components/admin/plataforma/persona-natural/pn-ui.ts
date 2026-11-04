// Clases compartidas de las pestañas de Plataforma > Persona Natural (Épica #14021, HU #14074 a #14076).
// Mismos tokens y estados de foco que los formularios de las demás consolas admin (línea base de globals.css).

export const PN_INPUT_CLS =
  "h-11 w-full min-w-0 rounded-[10px] border border-[#DFE5ED] bg-white px-3 text-sm text-flit-primary " +
  "focus:border-flit-brand focus:outline-none focus:ring-2 focus:ring-inset focus:ring-flit-brand " +
  "disabled:bg-[#EEF5FF] disabled:opacity-80 dark:border-white/15 dark:bg-[#162744] dark:text-white";
export const PN_INPUT_ERROR_CLS = "border-[#FF4E00] ring-1 ring-inset ring-[#FF4E00]";
export const PN_LABEL_CLS = "flex flex-col gap-1.5 text-sm font-semibold text-flit-primary dark:text-white";
export const PN_HINT_CLS = "text-xs font-normal text-[#59677D] dark:text-white/70";
export const PN_FIELD_ERROR_CLS = "text-xs font-semibold text-[#C2410C]";
export const PN_CANCEL_BTN_CLS =
  "rounded-full border border-[#DFE5ED] px-5 py-2.5 text-sm font-semibold text-flit-primary hover:bg-[#F4F8FF] " +
  "focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 " +
  "disabled:opacity-70 dark:border-white/15 dark:text-white dark:hover:bg-white/5";
export const PN_PRIMARY_BTN_CLS =
  "inline-flex items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white " +
  "shadow-[0_10px_22px_rgba(79,116,201,0.22)] focus-visible:outline-none focus-visible:ring-2 " +
  "focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:opacity-70";
export const PN_PRIMARY_BTN_STYLE = {
  background: "linear-gradient(135deg, var(--color-flit-brand) 0%, var(--color-flit-tech) 100%)",
} as const;
export const PN_DANGER_BTN_STYLE = {
  background: "linear-gradient(135deg, #FF4E00 0%, #E43D30 100%)",
} as const;
export const PN_NOTICE_CLS = "rounded-[10px] border px-3 py-2 text-xs font-semibold";
export const PN_NOTICE_STYLE = {
  background: "var(--badge-warning-bg)",
  color: "var(--badge-warning-fg)",
  borderColor: "var(--badge-warning-border)",
} as const;
export const PN_INFO_NOTICE_CLS =
  "rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 text-xs text-flit-primary " +
  "dark:border-white/15 dark:bg-white/5 dark:text-white";
