import { describe, expect, it } from "vitest";
import nextConfig from "../../../next.config";
import { cabecerasDeSeguridad, construirCsp } from "../cabeceras";

const directiva = (csp: string, nombre: string) => csp.split("; ").find((d) => d.startsWith(`${nombre} `)) ?? "";

describe("cabeceras de seguridad", () => {
  const csp = construirCsp(false);

  it("la CSP de producción no permite unsafe-eval y solo el propio origen por defecto", () => {
    expect(csp).not.toContain("unsafe-eval");
    expect(directiva(csp, "default-src")).toBe("default-src 'self'");
    expect(directiva(csp, "connect-src")).toBe("connect-src 'self'");
  });

  it("en desarrollo solo se agrega unsafe-eval (lo exige React)", () => {
    expect(directiva(construirCsp(true), "script-src")).toContain("'unsafe-eval'");
  });

  it("bloquea embebido, base y formularios ajenos", () => {
    expect(directiva(csp, "frame-ancestors")).toBe("frame-ancestors 'none'");
    expect(directiva(csp, "base-uri")).toBe("base-uri 'self'");
    expect(directiva(csp, "form-action")).toBe("form-action 'self'");
    expect(directiva(csp, "object-src")).toBe("object-src 'none'");
    expect(directiva(csp, "img-src")).toBe("img-src 'self' data: blob:");
  });

  it("permite solo el captcha de Turnstile y reCAPTCHA en scripts y frames", () => {
    for (const origen of ["https://challenges.cloudflare.com", "https://www.google.com/recaptcha/", "https://www.gstatic.com/recaptcha/"]) {
      expect(directiva(csp, "script-src")).toContain(origen);
    }
    expect(directiva(csp, "frame-src")).toContain("https://challenges.cloudflare.com");
    expect(directiva(csp, "frame-src")).toContain("https://www.google.com/recaptcha/");
    expect(csp).not.toMatch(/\*/);
  });

  it("incluye el resto de cabeceras exigidas", () => {
    const mapa = Object.fromEntries(cabecerasDeSeguridad().map((c) => [c.key, c.value]));
    expect(mapa["Strict-Transport-Security"]).toMatch(/max-age=\d{7,}/);
    expect(mapa["X-Content-Type-Options"]).toBe("nosniff");
    expect(mapa["X-Frame-Options"]).toBe("DENY");
    expect(mapa["Referrer-Policy"]).toBe("strict-origin-when-cross-origin");
    expect(mapa["Permissions-Policy"]).toBe("camera=(), microphone=(), geolocation=()");
  });

  it("next.config las aplica a todas las rutas, incluida /api", async () => {
    const reglas = await nextConfig.headers!();
    expect(reglas).toHaveLength(1);
    expect(reglas[0].source).toBe("/:path*");
    expect(reglas[0].headers.map((h) => h.key)).toContain("Content-Security-Policy");
  });
});
