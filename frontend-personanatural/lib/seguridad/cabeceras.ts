// Cabeceras de seguridad del portal (revisión de seguridad M8). Se aplican a todas las rutas desde next.config.ts.
// Sin dependencias de Next para poder probarlas con Vitest.

// Captcha permitido (lista fija): Cloudflare Turnstile y Google reCAPTCHA.
export const ORIGENES_CAPTCHA_SCRIPT = ["https://challenges.cloudflare.com", "https://www.google.com/recaptcha/", "https://www.gstatic.com/recaptcha/"];
export const ORIGENES_CAPTCHA_FRAME = ["https://challenges.cloudflare.com", "https://www.google.com/recaptcha/", "https://recaptcha.google.com/recaptcha/"];

/**
 * CSP sin `unsafe-eval` en producción. Las páginas son estáticas (sin nonce por petición), por eso `script-src` y
 * `style-src` admiten `'unsafe-inline'`: Next inyecta scripts de arranque en línea y Tailwind/React usan `style=""`.
 * `next/font/google` se autoalberga en el build, así que las fuentes salen del propio origen.
 */
export function construirCsp(desarrollo = false): string {
  const directivas: Record<string, string[]> = {
    "default-src": ["'self'"],
    "script-src": ["'self'", "'unsafe-inline'", ...(desarrollo ? ["'unsafe-eval'"] : []), ...ORIGENES_CAPTCHA_SCRIPT],
    "style-src": ["'self'", "'unsafe-inline'"],
    "img-src": ["'self'", "data:", "blob:"],
    "font-src": ["'self'", "data:"],
    "connect-src": ["'self'"],
    "frame-src": ORIGENES_CAPTCHA_FRAME,
    "object-src": ["'none'"],
    "frame-ancestors": ["'none'"],
    "base-uri": ["'self'"],
    "form-action": ["'self'"],
  };
  return Object.entries(directivas)
    .map(([nombre, valores]) => `${nombre} ${valores.join(" ")}`)
    .join("; ");
}

export function cabecerasDeSeguridad(desarrollo = false): { key: string; value: string }[] {
  return [
    { key: "Content-Security-Policy", value: construirCsp(desarrollo) },
    { key: "Strict-Transport-Security", value: "max-age=63072000; includeSubDomains" },
    { key: "X-Content-Type-Options", value: "nosniff" },
    { key: "X-Frame-Options", value: "DENY" },
    { key: "Referrer-Policy", value: "strict-origin-when-cross-origin" },
    { key: "Permissions-Policy", value: "camera=(), microphone=(), geolocation=()" },
  ];
}
