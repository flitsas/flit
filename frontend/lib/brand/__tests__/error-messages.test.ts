// HU #12414 AC2 — mismo texto en cliente y en el rechazo del servidor por código.
import { describe, expect, it } from "vitest";
import { BRANDING_ERROR_MESSAGES, brandingErrorMessage } from "../error-messages";

describe("brandingErrorMessage", () => {
  it("devuelve el texto único por cada código conocido de BrandingErrors.cs", () => {
    expect(brandingErrorMessage("BRANDING_LOGO_TOO_LARGE")).toBe(BRANDING_ERROR_MESSAGES.BRANDING_LOGO_TOO_LARGE);
    expect(brandingErrorMessage("BRANDING_LOGO_FORMAT")).toBe(BRANDING_ERROR_MESSAGES.BRANDING_LOGO_FORMAT);
    expect(brandingErrorMessage("BRANDING_LOGO_DIMENSIONS")).toBe(BRANDING_ERROR_MESSAGES.BRANDING_LOGO_DIMENSIONS);
    expect(brandingErrorMessage("BRANDING_CONTRAST_TOO_LOW")).toBe(BRANDING_ERROR_MESSAGES.BRANDING_CONTRAST_TOO_LOW);
  });

  it("retorna un mensaje genérico para código null/undefined", () => {
    expect(brandingErrorMessage(null)).toMatch(/no se pudo completar/i);
    expect(brandingErrorMessage(undefined)).toMatch(/no se pudo completar/i);
  });

  it("retorna un mensaje genérico (fallback) para un código desconocido", () => {
    expect(brandingErrorMessage("CODIGO_INEXISTENTE")).toMatch(/no se pudo completar/i);
  });

  it("cubre todos los códigos declarados en BrandingErrors.cs (backend)", () => {
    const backendCodes = [
      "BRANDING_NOT_FOUND",
      "BRANDING_TENANT_NOT_MARCA_BLANCA",
      "CONCURRENCY_CONFLICT",
      "BRANDING_INCOMPLETE",
      "BRANDING_NAME_LENGTH",
      "BRANDING_NAME_MARKUP",
      "BRANDING_COLOR_FORMAT",
      "BRANDING_CONTRAST_TOO_LOW",
      "BRANDING_LOGO_NOT_FOUND",
      "BRANDING_LOGO_FORMAT",
      "BRANDING_LOGO_TOO_LARGE",
      "BRANDING_LOGO_DIMENSIONS",
    ];
    for (const code of backendCodes) {
      expect(BRANDING_ERROR_MESSAGES[code]).toBeTruthy();
    }
  });
});
