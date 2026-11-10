/**
 * Tokens visuales del módulo Representantes legales.
 * Fuente: flit_design_tokens.json + patrón CTA admin vivo
 * (`linear-gradient(135deg,#557EFF,#00DBD5)` en CompanyConfigTabs / Usuarios).
 *
 * No inventar hex fuera de este archivo dentro del módulo RL.
 */

export const RL_COLOR = {
  /** color.text.primary / brand.blueDark */
  navy: "var(--color-flit-primary)",
  /** color.text.secondary / state.draft */
  secondary: "var(--muted-foreground)",
  /** color.text.muted */
  muted: "var(--muted-foreground)",
  /** Admin brand blue (≈ brand.blue #4F74C9 del prototipo) */
  brand: "var(--color-flit-brand)",
  /** Admin cyan (≈ brand.cyan #4FD4CC) */
  cyan: "var(--color-flit-tech)",
  /** color.background.tableHeader */
  tableHeader: "var(--secondary)",
  /** color.border.soft */
  border: "var(--border)",
  /** color.background.card */
  card: "var(--card)",
  /** color.background.modal */
  modal: "var(--secondary)",
  /** color.state.success */
  success: "var(--flit-success-ink)",
  successText: "var(--flit-success-ink)",
  successBg: "var(--badge-success-bg)",
  /** color.state.warning */
  warning: "var(--badge-warning-fg)",
  warningBg: "var(--badge-warning-bg)",
  warningText: "var(--badge-warning-fg)",
  /** color.state.danger */
  danger: "var(--destructive)",
  dangerBorder: "var(--badge-danger-border)",
  /** Pendiente / alerta suave (semántica warning) */
  pending: "var(--badge-warning-fg)",
  pendingBg: "var(--badge-warning-bg)",
  pendingText: "var(--badge-warning-fg)",
} as const;

export const RL_GRADIENT = {
  /** CTA primario — patrón admin FLIT */
  primary: "var(--gradient-action)",
  /** CTA peligro — tokens danger */
  danger: "var(--destructive)",
} as const;

export const rlPrimaryCtaClass =
  "inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2 text-xs font-semibold text-white disabled:opacity-60";

export const rlPrimaryCtaStyle = { background: RL_GRADIENT.primary } as const;

export const rlDangerCtaClass =
  "inline-flex items-center justify-center gap-2 rounded-xl px-4 py-2.5 text-sm font-semibold text-white disabled:opacity-60";

export const rlDangerCtaStyle = { background: RL_GRADIENT.danger, color: "var(--destructive-foreground)" } as const;

export const rlGhostBrandClass =
  "inline-flex items-center justify-center gap-1.5 rounded-lg border px-3 py-1.5 text-xs font-semibold disabled:opacity-50";

export const rlGhostBrandStyle = {
  color: RL_COLOR.brand,
  borderColor: RL_COLOR.brand,
} as const;

export const rlIconActionClass =
  "inline-flex items-center justify-center gap-1 rounded-lg border px-2.5 py-1.5 text-xs font-semibold disabled:opacity-60";

export const rlDangerGhostStyle = {
  color: RL_COLOR.danger,
  borderColor: RL_COLOR.dangerBorder,
} as const;

/**
 * Botón secundario con borde e icono — patrón del módulo de trámites (VehicleTransformationsCard,
 * DocumentChecklist: «Adjuntar archivo»): borde y texto de marca, fondo blanco con tinte de hover al
 * pasar el mouse, altura fija. Usar para acciones tipo «Agregar», «Ver», «Editar» dentro de tarjetas.
 */
export const rlOutlinedActionClass =
  "inline-flex h-9 items-center gap-1.5 rounded-lg border bg-card px-4 text-[12px] font-semibold transition hover:bg-secondary disabled:cursor-not-allowed disabled:opacity-50";

export const rlOutlinedActionStyle = {
  borderColor: RL_COLOR.brand,
  color: RL_COLOR.brand,
} as const;

/** Acción destructiva de texto plano — patrón «Borrar» del módulo de trámites: sin borde ni icono. */
export const rlTextDangerActionClass =
  "text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-50";

export const rlTextDangerActionStyle = {
  color: RL_COLOR.danger,
} as const;

export const RL_INPUT_CLS =
  "w-full rounded-xl border bg-transparent px-3 py-2 text-xs outline-none focus:border-flit-brand focus-visible:ring-2 focus-visible:ring-flit-brand/30 border-border";
