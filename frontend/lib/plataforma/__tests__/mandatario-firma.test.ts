import { describe, expect, it } from "vitest";
import {
  etiquetaTipoFirma,
  motivoSinFirma,
  organismosSinMedioDeFirma,
  puedeFirmarElectronicamente,
  tipoDeFirmaMandatario,
} from "@/lib/plataforma/mandatario-firma";

const FUNZA = "eeacc872-a522-56bb-9150-70776b094009";
const BOGOTA = "aaaaaaaa-0001-4000-8000-000000000001";

describe("mandatario-firma (HU #11716/#11717)", () => {
  it("sin firma, sin identidad y sin correo no puede firmar", () => {
    expect(puedeFirmarElectronicamente({})).toBe(false);
    expect(organismosSinMedioDeFirma([FUNZA], {})).toEqual([FUNZA]);
  });

  it("la firma del baúl basta", () => {
    expect(organismosSinMedioDeFirma([FUNZA, BOGOTA], { signatureVaultId: "f1" })).toEqual([]);
  });

  it("la identidad vigente basta", () => {
    expect(organismosSinMedioDeFirma([FUNZA], { identityStatus: "valid" })).toEqual([]);
  });

  it("la identidad en camino basta", () => {
    expect(organismosSinMedioDeFirma([FUNZA], { identityStatus: "pending" })).toEqual([]);
  });

  it("HU #13132: el correo ya no cuenta como medio de firma", () => {
    expect(puedeFirmarElectronicamente({ email: "x@y.com" })).toBe(false);
    expect(organismosSinMedioDeFirma([FUNZA], { email: "x@y.com" })).toEqual([FUNZA]);
  });

  it("una identidad vencida no alcanza", () => {
    expect(organismosSinMedioDeFirma([FUNZA], { identityStatus: "expired" })).toEqual([FUNZA]);
    expect(motivoSinFirma({ identityStatus: "expired" })).toContain("vencida");
  });

  it("HU #13133: la firma física ya no exime a ningún organismo", () => {
    expect(organismosSinMedioDeFirma([FUNZA, BOGOTA], {})).toEqual([FUNZA, BOGOTA]);
  });

  it("un correo en blanco no cuenta como medio de firma", () => {
    expect(organismosSinMedioDeFirma([FUNZA], { email: "   " })).toEqual([FUNZA]);
  });

  it("clasifica el tipo de firma con precedencia baúl > identidad > en curso > sin medio", () => {
    expect(tipoDeFirmaMandatario({ signatureVaultId: "v1", identityStatus: "valid" })).toBe("baul");
    expect(tipoDeFirmaMandatario({ identityStatus: "valid" })).toBe("identidad");
    expect(tipoDeFirmaMandatario({ identityStatus: "pending" })).toBe("identidad_pendiente");
    expect(tipoDeFirmaMandatario({ email: "x@y.com" })).toBe("sin_medio");
    expect(tipoDeFirmaMandatario({})).toBe("sin_medio");
    expect(etiquetaTipoFirma("baul")).toBe("Baúl de firmas");
  });

  it("HU #13133: no existe el tipo «firma a mano»", () => {
    expect(etiquetaTipoFirma("sin_medio")).toBe("Sin medio de firma");
    expect(etiquetaTipoFirma("a_mano" as never)).not.toMatch(/mano|física/i);
  });
});
