// HU #11756 (ADR-0050) — matriz de rótulos + copy por estado.
// Cubre: identidadRotulo (4 estados), firmaBaulRotulo (2 formas) e identityCopy (matriz completa
// 4 estados x 2 firma = 8 combinaciones), respetando la precedencia D8 del ADR-0025 (baúl > identidad)
// y el caso NIT (ADR-0036: la prevalidación no aplica a persona jurídica).

import { describe, expect, it } from "vitest";
import {
  identidadRotulo,
  firmaBaulRotulo,
  identityCopy,
  IDENTITY_MODULE_HREF,
  type IdentityCopyContext,
} from "../identity-vigencia";

// Uso de ejemplo:
//   identidadRotulo("valid") → "Identidad: aprobada y vigente"
//   firmaBaulRotulo(true, "2027-12-31") → "Firma del baúl: vigente hasta 2027/12/31"
//   identityCopy({ identityStatus: "none", firmaBaulVigente: false }) →
//     { message: "...módulo Identidad.", showLink: true }

describe("HU #11756 — identidadRotulo: los 4 estados canónicos del ADR-0050", () => {
  it("happy path: none → «Identidad: sin validación»", () => {
    expect(identidadRotulo("none")).toBe("Identidad: sin validación");
  });
  it("happy path: pending → «Identidad: en curso»", () => {
    expect(identidadRotulo("pending")).toBe("Identidad: en curso");
  });
  it("happy path: valid → «Identidad: aprobada y vigente»", () => {
    expect(identidadRotulo("valid")).toBe("Identidad: aprobada y vigente");
  });
  it("happy path: expired → «Identidad: vencida»", () => {
    expect(identidadRotulo("expired")).toBe("Identidad: vencida");
  });
  it("edge case: null/undefined/estado desconocido caen a «sin validación»", () => {
    expect(identidadRotulo(null)).toBe("Identidad: sin validación");
    expect(identidadRotulo(undefined)).toBe("Identidad: sin validación");
    expect(identidadRotulo("cualquier-otra-cosa")).toBe("Identidad: sin validación");
  });
});

describe("HU #11756 — firmaBaulRotulo: las dos formas del rótulo de firma", () => {
  it("happy path: vigente con fecha → «Firma del baúl: vigente hasta AAAA/MM/DD»", () => {
    expect(firmaBaulRotulo(true, "2027-12-31")).toBe("Firma del baúl: vigente hasta 2027/12/31");
  });
  it("happy path: sin firma vigente → «Firma del baúl: sin firma vigente»", () => {
    expect(firmaBaulRotulo(false, null)).toBe("Firma del baúl: sin firma vigente");
  });
  it("edge case: vigente=true sin fecha registrada no promete una fecha inexistente", () => {
    expect(firmaBaulRotulo(true, null)).toBe("Firma del baúl: vigente");
  });
  it("edge case: vigente=null/undefined se trata como sin firma vigente", () => {
    expect(firmaBaulRotulo(null, null)).toBe("Firma del baúl: sin firma vigente");
    expect(firmaBaulRotulo(undefined, null)).toBe("Firma del baúl: sin firma vigente");
  });
});

// ── Matriz completa: 4 estados x 2 (firma sí/no) = 8 combinaciones ───────────

describe("HU #11756 — identityCopy: matriz completa 4 estados x 2 firma (D8 ADR-0025: baúl > identidad)", () => {
  const ESTADOS = ["none", "pending", "valid", "expired"] as const;

  it.each(ESTADOS)("con firma de baúl vigente, estado «%s» → sin invitación (D8 manda)", (status) => {
    const ctx: IdentityCopyContext = { identityStatus: status, firmaBaulVigente: true };
    expect(identityCopy(ctx)).toEqual({ message: null, showLink: false });
  });

  it("sin firma vigente + none → invita al módulo Identidad (primera vez)", () => {
    const result = identityCopy({ identityStatus: "none", firmaBaulVigente: false });
    expect(result.showLink).toBe(true);
    expect(result.message).toMatch(/todavía no tiene una validación/i);
  });

  it("sin firma vigente + expired → invita al módulo Identidad (renovar), copy DISTINTO de «none»", () => {
    const none = identityCopy({ identityStatus: "none", firmaBaulVigente: false });
    const expired = identityCopy({ identityStatus: "expired", firmaBaulVigente: false });
    expect(expired.showLink).toBe(true);
    expect(expired.message).toMatch(/venció/i);
    expect(expired.message).not.toBe(none.message);
  });

  it("sin firma vigente + pending → sin invitación (ya está en curso)", () => {
    expect(identityCopy({ identityStatus: "pending", firmaBaulVigente: false })).toEqual({
      message: null,
      showLink: false,
    });
  });

  it("sin firma vigente + valid → sin invitación (ya quedó resuelta)", () => {
    expect(identityCopy({ identityStatus: "valid", firmaBaulVigente: false })).toEqual({
      message: null,
      showLink: false,
    });
  });

  it("contrato: firmaBaulVigente ausente (undefined) se comporta como «sin firma vigente»", () => {
    const result = identityCopy({ identityStatus: "none" });
    expect(result.showLink).toBe(true);
  });
});

// ── Caso NIT (ADR-0036: la prevalidación no aplica a persona jurídica) ───────

describe("HU #11756 — identityCopy: NIT no enlaza a un flujo imposible", () => {
  it("happy path: NIT + none + sin firma → copy de «no aplica», sin enlace", () => {
    const result = identityCopy({ identityStatus: "none", firmaBaulVigente: false, documentType: "NIT" });
    expect(result.showLink).toBe(false);
    expect(result.message).toMatch(/no aplica prevalidación/i);
  });

  it("happy path: NIT + expired + sin firma → también «no aplica», sin enlace", () => {
    const result = identityCopy({ identityStatus: "expired", firmaBaulVigente: false, documentType: "NIT" });
    expect(result.showLink).toBe(false);
    expect(result.message).toMatch(/no aplica prevalidación/i);
  });

  it("edge case: documentType se normaliza (minúsculas / espacios) antes de comparar", () => {
    const result = identityCopy({ identityStatus: "none", firmaBaulVigente: false, documentType: " nit " });
    expect(result.showLink).toBe(false);
  });

  it("contrato: con firma de baúl vigente, NIT tampoco cambia nada (D8 ya lo resolvió)", () => {
    const result = identityCopy({ identityStatus: "none", firmaBaulVigente: true, documentType: "NIT" });
    expect(result).toEqual({ message: null, showLink: false });
  });

  it("contrato: CC (persona natural) sí enlaza — el NIT es la excepción, no la regla", () => {
    const result = identityCopy({ identityStatus: "none", firmaBaulVigente: false, documentType: "CC" });
    expect(result.showLink).toBe(true);
  });
});

describe("HU #11756 — IDENTITY_MODULE_HREF: enlace simple, sin precargar el documento", () => {
  it("contrato: es la ruta del módulo Identidad en la SPA admin, sin query params de documento", () => {
    expect(IDENTITY_MODULE_HREF).toBe("/?m=validaciones");
    expect(IDENTITY_MODULE_HREF).not.toMatch(/documentNumber|documentType/i);
  });
});
