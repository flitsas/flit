// Consulta automática del estado de la validación propia del mandatario: la lista consulta al proveedor como la
// pantalla de espera del trámite (caso de QA: aprobó en el segundo intento y el aviso no llegó; la ficha quedaba pendiente).
import { describe, expect, it, vi } from "vitest";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import {
  MAX_CONSULTAS_AUTOMATICAS,
  puedeConsultarEstado,
  sincronizarValidacionesEnCurso,
} from "../mandatario-validacion";

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
    identityStatus: "pending",
    signatureVaultId: null,
    registeredAt: "2026-01-01",
    isActive: true,
    companyTenantIds: [],
    signerModel: "natural",
    signatureMethod: "biometria",
    ...o,
  } as MandateSigner;
}

describe("puedeConsultarEstado", () => {
  it("aplica a la validación en curso de la Persona natural con biometría", () => {
    expect(puedeConsultarEstado(signer())).toBe(true);
  });

  it.each([
    ["aprobada", { identityStatus: "valid" as const }],
    ["sin validación", { identityStatus: "none" as const }],
    ["firma con baúl", { signatureMethod: "baul" as const, signatureVaultId: "v" }],
    ["inactivo", { isActive: false }],
  ])("no aplica: %s", (_caso, o) => {
    expect(puedeConsultarEstado(signer(o))).toBe(false);
  });
});

describe("sincronizarValidacionesEnCurso", () => {
  it("consulta solo las validaciones en curso e informa si alguna cambió", async () => {
    const consultar = vi.fn(async (s: MandateSigner) => ({ status: "aprobado", updated: s.id === "b" }));
    const cambio = await sincronizarValidacionesEnCurso(
      [signer({ id: "a" }), signer({ id: "b" }), signer({ id: "c", identityStatus: "valid" })],
      consultar,
    );

    expect(cambio).toBe(true);
    expect(consultar.mock.calls.map(([s]) => s.id)).toEqual(["a", "b"]);
  });

  it("un fallo del proveedor no interrumpe ni recarga", async () => {
    const consultar = vi.fn(async () => {
      throw new ApiError(503, "no disponible");
    });

    await expect(sincronizarValidacionesEnCurso([signer()], consultar)).resolves.toBe(false);
  });

  it("respeta el tope de consultas por carga", async () => {
    const consultar = vi.fn(async () => ({ status: "en_proceso", updated: false }));
    const muchos = Array.from({ length: MAX_CONSULTAS_AUTOMATICAS + 5 }, (_, i) => signer({ id: `s${i}` }));

    await sincronizarValidacionesEnCurso(muchos, consultar);

    expect(consultar).toHaveBeenCalledTimes(MAX_CONSULTAS_AUTOMATICAS);
  });
});
