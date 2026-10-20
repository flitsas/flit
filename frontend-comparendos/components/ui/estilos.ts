// Clases comunes de formularios y botones de Comparendos (mismo lenguaje visual que los controles de trámites).
export { controlCls, TRAMITES_CONTROL_CLS as CONTROL_CLS } from "@/components/control-styles";

export const INPUT_CLS =
  "h-9 w-full rounded-xl border border-[#DFE5ED] bg-white px-3 text-xs text-[#1E293B] focus:border-[#557EFF] focus:outline-none " +
  "focus:ring-2 focus:ring-[#557EFF]/30 disabled:opacity-60 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

export const TEXTAREA_CLS =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs text-[#1E293B] focus:border-[#557EFF] focus:outline-none " +
  "focus:ring-2 focus:ring-[#557EFF]/30 dark:border-white/15 dark:bg-[#0B0F14] dark:text-white";

export const BOTON_PRIMARIO_CLS =
  "inline-flex h-9 items-center justify-center gap-1.5 rounded-xl bg-[#557EFF] px-4 text-xs font-semibold text-white transition " +
  "hover:bg-[#3B4FD6] disabled:cursor-not-allowed disabled:opacity-50";

export const BOTON_PELIGRO_CLS =
  "inline-flex h-9 items-center justify-center gap-1.5 rounded-xl bg-[#FF4E00] px-4 text-xs font-semibold text-white transition " +
  "hover:bg-[#D94200] disabled:cursor-not-allowed disabled:opacity-50";

export const ETIQUETA_CLS = "flex flex-col gap-1 text-[11px] font-semibold text-[#5B6B82] dark:text-slate-300";

export const TITULO_CLS = "text-2xl font-semibold text-[#162744] dark:text-white";
