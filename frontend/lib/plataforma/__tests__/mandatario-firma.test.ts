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
    expect(organismosSinMedioDeFirma([FUNZA], [], {})).toEqual([FUNZA]);
  });

  it("la firma del baúl basta", () => {
    expect(organismosSinMedioDeFirma([FUNZA, BOGOTA], [], { signatureVaultId: "f1" })).toEqual([]);
  });

  it("la identidad vigente basta", () => {
    expect(organismosSinMedioDeFirma([FUNZA], [], { identityStatus: "valid" })).toEqual([]);
  });

  it("la identidad en camino basta", () => {
    // Un mandatario nuevo nunca tiene identidad vigente todavía; se le envía al registrarlo.
    expect(organismosSinMedioDeFirma([FUNZA], [], { identityStatus: "pending" })).toEqual([]);
    expect(organismosSinMedioDeFirma([FUNZA], [], { email: "x@y.com" })).toEqual([]);
  });

  it("una identidad vencida no alcanza", () => {
    expect(organismosSinMedioDeFirma([FUNZA], [], { identityStatus: "expired" })).toEqual([FUNZA]);
    expect(motivoSinFirma({ identityStatus: "expired" })).toContain("vencida");
  });

  it("la firma física exime, y solo al organismo marcado", () => {
    expect(organismosSinMedioDeFirma([FUNZA], [FUNZA], {})).toEqual([]);
    expect(organismosSinMedioDeFirma([FUNZA, BOGOTA], [FUNZA], {})).toEqual([BOGOTA]);
  });

  it("un correo en blanco no cuenta como medio de firma", () => {
    expect(organismosSinMedioDeFirma([FUNZA], [], { email: "   " })).toEqual([FUNZA]);
  });

  it("clasifica el tipo de firma con precedencia baúl > identidad > a mano", () => {
    expect(tipoDeFirmaMandatario({ signatureVaultId: "v1", identityStatus: "valid" }, FUNZA)).toBe("baul");
    expect(tipoDeFirmaMandatario({ identityStatus: "valid" }, FUNZA)).toBe("identidad");
    expect(
      tipoDeFirmaMandatario({ physicalSignatureOfficeIds: [FUNZA] }, FUNZA),
    ).toBe("a_mano");
    expect(etiquetaTipoFirma("baul")).toBe("Firma del baúl");
  });
});
