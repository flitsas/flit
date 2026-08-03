/**
 * Tokens visuales del módulo Representantes legales.
 * Fuente: flit_design_tokens.json + patrón CTA admin vivo
 * (`linear-gradient(135deg,#557EFF,#00DBD5)` en CompanyConfigTabs / Usuarios).
 *
 * No inventar hex fuera de este archivo dentro del módulo RL.
 */

export const RL_COLOR = {
  /** color.text.primary / brand.blueDark */
  navy: "#162744",
  /** color.text.secondary / state.draft */
  secondary: "#59677D",
  /** color.text.muted */
  muted: "#7D8798",
  /** Admin brand blue (≈ brand.blue #4F74C9 del prototipo) */
  brand: "#557EFF",
  /** Admin cyan (≈ brand.cyan #4FD4CC) */
  cyan: "#00DBD5",
  /** color.background.tableHeader */
  tableHeader: "#F4F6FA",
  /** color.border.soft */
  border: "#DDE5F0",
  /** color.background.card */
  card: "#FFFFFF",
  /** color.background.modal */
  modal: "#EEF5FF",
  /** color.state.success */
  success: "#70CF3A",
  successText: "#3f7a15",
  successBg: "rgba(112, 207, 58, 0.14)",
  /** color.state.warning */
  warning: "#F05A35",
  warningBg: "rgba(240, 90, 53, 0.08)",
  warningText: "#8a3a1a",
  /** color.state.danger */
  danger: "#E43D30",
  dangerBorder: "#f0c38e",
  /** Pendiente / alerta suave (semántica warning) */
  pending: "#F05A35",
  pendingBg: "rgba(240, 90, 53, 0.08)",
  pendingText: "#8a3a1a",
} as const;

export const RL_GRADIENT = {
  /** CTA primario — patrón admin FLIT */
  primary: "linear-gradient(135deg, #557EFF 0%, #00DBD5 100%)",
  /** CTA peligro — tokens danger */
  danger: "linear-gradient(90deg, #F05A35 0%, #E43D30 100%)",
} as const;

export const rlPrimaryCtaClass =
  "inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60";

export const rlPrimaryCtaStyle = { background: RL_GRADIENT.primary } as const;

export const rlDangerCtaClass =
  "inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-60";

export const rlDangerCtaStyle = { background: RL_GRADIENT.danger } as const;

export const rlGhostBrandClass =
  "inline-flex items-center justify-center gap-1.5 rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-50";

export const rlGhostBrandStyle = {
  color: RL_COLOR.brand,
  borderColor: RL_COLOR.brand,
} as const;

export const rlIconActionClass =
  "inline-flex items-center justify-center gap-1 rounded-lg border px-2.5 py-1.5 text-[11px] font-semibold disabled:opacity-60";

export const rlDangerGhostStyle = {
  color: RL_COLOR.danger,
  borderColor: RL_COLOR.dangerBorder,
} as const;

export const RL_INPUT_CLS =
  "w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-[#557EFF] border-[#DDE5F0]";
