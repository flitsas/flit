// HU #13651 — textos de vigencia del cambio de permisos (AC1, AC2, AC3).
import { describe, expect, it } from "vitest";
import { formatMaximo, mensajeVigencia } from "../vigencia";

describe("formatMaximo", () => {
  it.each([
    [720, "12 horas"],
    [15, "15 minutos"],
    [90, "1 hora y 30 minutos"],
    [60, "1 hora"],
    [1, "1 minuto"],
    [125, "2 horas y 5 minutos"],
    [480, "8 horas"],
    [1440, "24 horas"],
    [61, "1 hora y 1 minuto"],
    [0, "0 minutos"],
  ])("%i minutos → «%s»", (minutes, expected) => {
    expect(formatMaximo(minutes)).toBe(expected);
  });

  it("un valor no válido no rompe el texto", () => {
    expect(formatMaximo(Number.NaN)).toBe("0 minutos");
    expect(formatMaximo(-5)).toBe("0 minutos");
    expect(formatMaximo(14.9)).toBe("14 minutos");
  });
});

describe("mensajeVigencia", () => {
  it("AC1 — modo heredado: aplica en el próximo inicio de sesión", () => {
    expect(mensajeVigencia("Ana", { mode: "legacy", maxMinutes: 720 })).toBe(
      "El cambio se aplicará la próxima vez que Ana inicie sesión (máximo 12 horas).",
    );
  });

  it("AC2 — OIDC activo: aplica al iniciar o renovar la sesión", () => {
    expect(mensajeVigencia("Ana", { mode: "oidc", maxMinutes: 15 })).toBe(
      "El cambio se aplicará la próxima vez que Ana inicie sesión o renueve su sesión (máximo 15 minutos).",
    );
  });

  it("AC3 — el máximo refleja el valor configurado, no un literal", () => {
    expect(mensajeVigencia("Ana", { mode: "legacy", maxMinutes: 8 * 60 })).toContain("(máximo 8 horas)");
    expect(mensajeVigencia("Ana", { mode: "oidc", maxMinutes: 30 })).toContain("(máximo 30 minutos)");
    expect(mensajeVigencia("Ana", { mode: "oidc", maxMinutes: 90 })).toContain("(máximo 1 hora y 30 minutos)");
  });

  it("usa el nombre que se le pasa", () => {
    expect(mensajeVigencia("María José", { mode: "legacy", maxMinutes: 720 })).toContain("que María José inicie sesión");
  });
});
