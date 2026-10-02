// HU #13248 (F9 #13245) — presentación de la validación propia del mandatario y sus avisos.
import { describe, expect, it } from "vitest";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import {
  avisoDeNuevaValidacion,
  consecuenciaDeGuardar,
  disparoDeValidacion,
  mensajeReenvio,
  mensajeValidacionTrasGuardar,
  presentarValidacion,
  puedeReenviarValidacion,
  requiereValidacionPropia,
} from "../mandatario-validacion";

const base = {
  signerModel: "natural" as const,
  signatureMethod: "biometria" as const,
  signatureVaultId: null,
  isActive: true,
  identityStatus: "none" as const,
};

function signer(o: Partial<MandateSigner> = {}): MandateSigner {
  return {
    id: "m",
    transitOfficeId: "o",
    fullName: "Ana",
    documentType: "CC",
    documentNumber: "1",
    integrityHash: "h",
    email: "a@b.co",
    userId: null,
    identityStatus: "none",
    signatureVaultId: null,
    registeredAt: "2026-01-01",
    isActive: true,
    companyTenantIds: [],
    signerModel: "natural",
    signatureMethod: "biometria",
    ...o,
  };
}

describe("HU #13248 — presentarValidacion", () => {
  it("un estado desconocido cae a «pendiente de validación»", () => {
    expect(presentarValidacion(undefined).texto).toBe("Pendiente de validación");
    expect(presentarValidacion("valid").tone).toBe("success");
  });
});

describe("HU #13248 — cuándo aplica el bloque", () => {
  it("solo con Persona natural y validación de identidad (o legado sin baúl)", () => {
    expect(requiereValidacionPropia(base)).toBe(true);
    expect(requiereValidacionPropia({ ...base, signatureMethod: null })).toBe(true);
    expect(requiereValidacionPropia({ ...base, signatureMethod: null, signatureVaultId: "s" })).toBe(false);
    expect(requiereValidacionPropia({ ...base, signatureMethod: "baul" })).toBe(false);
    expect(requiereValidacionPropia({ ...base, signerModel: "juridica", signatureMethod: null })).toBe(false);
    expect(requiereValidacionPropia({ ...base, signerModel: "formato_blanco", signatureMethod: null })).toBe(false);
  });

  it("reenviar: no con aprobada, inactivo o sin permiso de edición", () => {
    expect(puedeReenviarValidacion(base)).toBe(true);
    expect(puedeReenviarValidacion({ ...base, identityStatus: "valid" })).toBe(false);
    expect(puedeReenviarValidacion({ ...base, isActive: false })).toBe(false);
    expect(puedeReenviarValidacion({ ...base, puedeEditar: false })).toBe(false);
  });
});

describe("HU #13248 — avisos", () => {
  const args = { editing: null, metodo: "biometria" as const, esNatural: true, tipoDocumento: "CC", numeroDocumento: "1" };

  it("sin disparo no hay aviso", () => {
    expect(avisoDeNuevaValidacion({ ...args, metodo: "baul" })).toBeNull();
    expect(avisoDeNuevaValidacion({ ...args, esNatural: false })).toBeNull();
    expect(avisoDeNuevaValidacion({ ...args, editing: signer() })).toBeNull();
  });

  it("alta, cambio de documento y paso de baúl avisan", () => {
    expect(avisoDeNuevaValidacion(args)).toMatch(/enviaremos el enlace/i);
    expect(avisoDeNuevaValidacion({ ...args, editing: signer(), numeroDocumento: "2" })).toMatch(/anterior deja de contar/i);
    expect(
      avisoDeNuevaValidacion({ ...args, editing: signer({ signatureMethod: "baul", signatureVaultId: "s" }) }),
    ).toMatch(/nueva validación/i);
  });
});

describe("HU #13248 — mensajes", () => {
  it("reenvío y guardado", () => {
    expect(mensajeReenvio({ identity: "sent" }, "a@b.co")).toBe("Enviamos el enlace de validación a a@b.co.");
    expect(mensajeReenvio({ identity: "queued" }, "a@b.co")).toMatch(/Reintentaremos/);
    expect(mensajeValidacionTrasGuardar({ identity: "failed" }, null)).toMatch(/Reenviar validación/);
    expect(mensajeValidacionTrasGuardar({ identity: "notattempted" }, null)).toBeNull();
    expect(mensajeValidacionTrasGuardar(undefined, null)).toBeNull();
  });
});

describe("HU #13248b — consecuencia en el resumen y aviso con correo", () => {
  const args = { editing: null, metodo: "biometria" as const, esNatural: true, tipoDocumento: "CC", numeroDocumento: "1" };

  it("cada disparo tiene su frase corta y sin disparo no hay frase", () => {
    expect(disparoDeValidacion(args)).toBe("alta");
    expect(disparoDeValidacion({ ...args, editing: signer(), numeroDocumento: "2" })).toBe("cambio_documento");
    expect(
      disparoDeValidacion({ ...args, editing: signer({ signatureMethod: "baul", signatureVaultId: "s" }) }),
    ).toBe("desde_baul");
    expect(disparoDeValidacion({ ...args, editing: signer() })).toBeNull();
    expect(consecuenciaDeGuardar("alta")).toBe("se enviará la validación");
    expect(consecuenciaDeGuardar("desde_baul")).toBe("pasará a validación de identidad");
    expect(consecuenciaDeGuardar("cambio_documento")).toBe("la validación anterior deja de contar");
    expect(consecuenciaDeGuardar(null)).toBeNull();
  });

  it("el alta nombra el correo escrito", () => {
    expect(avisoDeNuevaValidacion({ ...args, correo: " ana@x.co " })).toBe("Al guardar le enviaremos el enlace a ana@x.co.");
    expect(avisoDeNuevaValidacion({ ...args, correo: "" })).toMatch(/correo del mandatario/);
  });
});
