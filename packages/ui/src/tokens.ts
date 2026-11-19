/**
 * Tokens de color FLIT en TypeScript, espejo de `styles/tokens.css`.
 * Valores del UI kit «Diseño Inteligente» (HU #14474). Usar estos valores en lugar de hex ad hoc;
 * en CSS, preferir siempre las variables (`var(--…)`), que respetan la marca blanca y el tema oscuro.
 */
export const FLIT = {
  text: {
    primary: '#162744',
    secondary: '#4A5878',
    muted: '#5D6B86',
    brandTitle: '#557EFF',
  },
  brand: {
    cyan: '#00DBD5',
    blue: '#557EFF',
    blueDark: '#162744',
  },
  state: {
    success: '#8CC63F',
    active: '#557EFF',
    warning: '#FF4E00',
    danger: '#C10000',
    draft: '#91A5B5',
    muted: '#5D6B86',
  },
  border: {
    soft: '#DFE5ED',
    input: '#74839F',
    focus: '#557EFF',
  },
  background: {
    app: '#EEF5FF',
    modal: '#FFFFFF',
    card: '#FFFFFF',
    tableHeader: '#162744',
  },
  /** CTA primario del kit (azul FLIT → azul profundo). */
  gradientPrimary: 'linear-gradient(90deg, #557EFF 0%, #0F6FA8 100%)',
  /** Azul activo con alpha (hover / tint). */
  blueAlpha: (a: number) => `rgba(85, 126, 255, ${a})`,
  dangerAlpha: (a: number) => `rgba(193, 0, 0, ${a})`,
  warningAlpha: (a: number) => `rgba(255, 78, 0, ${a})`,
  successAlpha: (a: number) => `rgba(140, 198, 63, ${a})`,
} as const;
