import { describe, expect, it } from "vitest";
import {
  camposDePerfil,
  campoDeError,
  perfilInicial,
  validarPerfil,
  type PerfilMandatario,
} from "@/lib/plataforma/mandatario-modelo";

const base: PerfilMandatario = {
  model: "natural",
  method: "biometria",
  validityKind: "fixed",
  validFrom: "",
  validTo: "",
  signatureVaultId: null,
};

describe("mandatario-modelo (HU #13132)", () => {
  it("valida la Persona natural: forma de firma, baúl y rango", () => {
    expect(validarPerfil({ ...base, method: null }, { exigeSelectorBaul: true }).signatureMethod).toBeTruthy();
    expect(validarPerfil({ ...base, method: "baul" }, { exigeSelectorBaul: true }).signatureVaultId).toBeTruthy();
    expect(validarPerfil({ ...base, method: "baul" }, { exigeSelectorBaul: false })).toEqual({});
    const rango = validarPerfil({ ...base, validityKind: "range" }, { exigeSelectorBaul: true });
    expect(rango.validFrom).toBeTruthy();
    expect(rango.validTo).toBeTruthy();
    expect(
      validarPerfil(
        { ...base, validityKind: "range", validFrom: "2026-12-31", validTo: "2026-10-01" },
        { exigeSelectorBaul: true },
      ).validTo,
    ).toMatch(/anterior/);
    expect(
      validarPerfil(
        { ...base, validityKind: "range", validFrom: "2026-10-01", validTo: "2026-10-01" },
        { exigeSelectorBaul: true },
      ),
    ).toEqual({});
  });

  it("no valida firma ni fechas fuera de Persona natural", () => {
    expect(validarPerfil({ ...base, model: "juridica", method: null }, { exigeSelectorBaul: true })).toEqual({});
    expect(camposDePerfil({ ...base, model: "formato_blanco" })).toEqual({ signerModel: "formato_blanco" });
  });

  it("perfilInicial trata al mandatario anterior como natural y conserva su baúl", () => {
    const p = perfilInicial({ signatureVaultId: "v1", identityStatus: "none" } as never);
    expect(p).toMatchObject({ model: "natural", method: "baul", signatureVaultId: "v1" });
    expect(perfilInicial(null)).toMatchObject({ model: "natural", method: null, validityKind: "fixed" });
  });

  it("campoDeError acepta camelCase y PascalCase", () => {
    expect(campoDeError("validTo")).toBe("validTo");
    expect(campoDeError("ValidTo")).toBe("validTo");
    expect(campoDeError("signatureVaultId")).toBe("signatureVaultId");
    expect(campoDeError("desconocido")).toBeNull();
  });
});
