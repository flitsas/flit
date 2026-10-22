import { describe, expect, it } from "vitest";
import {
  EMAIL_ALREADY_ASSOCIATED_MESSAGE,
  emailConflictErrorCode,
  isEmailConflictCode,
  normalizeEmail,
} from "../emailConflict";

// HU #13812 (Feature #13808, Épica #13516) — mensaje único de usuario existente y forma canónica del correo.
// Uso de ejemplo: normalizeEmail(" Juan@X.com ") → "juan@x.com".

describe("normalizeEmail", () => {
  it("quita espacios y pasa a minúsculas", () => {
    expect(normalizeEmail(" Juan@X.com ")).toBe("juan@x.com");
  });

  it("deja igual un correo ya normalizado (idempotente)", () => {
    expect(normalizeEmail(normalizeEmail("ANA@Empresa.CO"))).toBe("ana@empresa.co");
  });

  it("devuelve cadena vacía con solo espacios, sin lanzar", () => {
    expect(normalizeEmail("   ")).toBe("");
  });
});

describe("conflicto de correo — contrato con el backend", () => {
  it("el mensaje visible es el aprobado por el PO", () => {
    expect(EMAIL_ALREADY_ASSOCIATED_MESSAGE).toBe("El usuario ya se encuentra previamente creado");
  });

  it("reconoce el código único en `code` o en `error`", () => {
    expect(isEmailConflictCode(emailConflictErrorCode({ code: "EMAIL_ALREADY_IN_USE" }))).toBe(true);
    expect(isEmailConflictCode(emailConflictErrorCode({ error: "EMAIL_ALREADY_IN_USE" }))).toBe(true);
  });

  it("no reconoce los códigos retirados (HU #11580)", () => {
    expect(isEmailConflictCode("USER_ALREADY_EXISTS")).toBe(false);
  });
});
