// Vigencia de la sesión (HU #13651, Feature #13102, Épica #12753): cuándo entra en vigor un cambio de permisos.
//
// El token lleva los permisos efectivos (rol + allow − deny) y solo se recalcula al emitir uno nuevo. Por eso un cambio
// no es inmediato: se aplica la próxima vez que la persona inicie sesión (modo heredado) o renueve su sesión (OIDC),
// y como máximo cuando vence el token. El máximo lo da el servidor (`GET /api/v1/auth/session-policy`), nunca un literal.
// Funciones puras: las consume la pantalla de permisos por usuario (otro Feature) sin saber nada del modo de sesión.

/** Respuesta de `GET /api/v1/auth/session-policy`. */
export interface SessionPolicy {
  /** `legacy` = JWT único (`Jwt:TokenLifetimeHours`); `oidc` = access token con renovación (`AccessTokenMinutes`). */
  mode: "legacy" | "oidc";
  /** Máximo de minutos hasta que la persona recibe un token con los permisos recalculados. */
  maxMinutes: number;
}

const plural = (n: number, singular: string, pluralForm: string) => `${n} ${n === 1 ? singular : pluralForm}`;

/**
 * Minutos → texto legible para el administrador: 720 → «12 horas», 15 → «15 minutos», 90 → «1 hora y 30 minutos».
 * Redondea hacia abajo a minutos enteros; un valor no válido (negativo, NaN) se muestra como «0 minutos».
 */
export function formatMaximo(maxMinutes: number): string {
  const total = Number.isFinite(maxMinutes) ? Math.max(0, Math.floor(maxMinutes)) : 0;
  const hours = Math.floor(total / 60);
  const minutes = total % 60;

  if (hours === 0) return plural(minutes, "minuto", "minutos");
  if (minutes === 0) return plural(hours, "hora", "horas");
  return `${plural(hours, "hora", "horas")} y ${plural(minutes, "minuto", "minutos")}`;
}

/**
 * Aviso para quien acaba de cambiar los permisos de `nombre`.
 *  - Modo heredado: «El cambio se aplicará la próxima vez que Ana inicie sesión (máximo 12 horas).»
 *  - OIDC: «El cambio se aplicará la próxima vez que Ana inicie sesión o renueve su sesión (máximo 15 minutos).»
 */
export function mensajeVigencia(nombre: string, policy: SessionPolicy): string {
  const maximo = formatMaximo(policy.maxMinutes);
  return policy.mode === "oidc"
    ? `El cambio se aplicará la próxima vez que ${nombre} inicie sesión o renueve su sesión (máximo ${maximo}).`
    : `El cambio se aplicará la próxima vez que ${nombre} inicie sesión (máximo ${maximo}).`;
}
