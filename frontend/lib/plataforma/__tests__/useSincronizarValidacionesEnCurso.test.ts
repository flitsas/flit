// Consulta automática y periódica del estado de las validaciones en curso mientras la lista de mandatarios está abierta.
import { act, renderHook } from "@testing-library/react";
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest";
import type { MandateSigner } from "@/lib/api/admin-mandate-signers";
import {
  INTERVALO_CONSULTA_MS,
  useSincronizarValidacionesEnCurso,
} from "../useSincronizarValidacionesEnCurso";

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

describe("useSincronizarValidacionesEnCurso", () => {
  beforeEach(() => vi.useFakeTimers());
  afterEach(() => vi.useRealTimers());

  it("consulta cada intervalo y avisa cuando una validación cambió", async () => {
    const consultar = vi.fn(async () => ({ status: "aprobado", updated: true }));
    const onCambio = vi.fn();
    renderHook(() => useSincronizarValidacionesEnCurso([signer()], consultar, onCambio));

    expect(consultar).not.toHaveBeenCalled();
    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_CONSULTA_MS);
    });

    expect(consultar).toHaveBeenCalledTimes(1);
    expect(onCambio).toHaveBeenCalledTimes(1);
  });

  it("sin validaciones en curso no consulta", async () => {
    const consultar = vi.fn(async () => ({ status: "aprobado", updated: false }));
    renderHook(() =>
      useSincronizarValidacionesEnCurso([signer({ identityStatus: "valid" })], consultar, vi.fn()),
    );

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_CONSULTA_MS * 3);
    });

    expect(consultar).not.toHaveBeenCalled();
  });

  it("deshabilitado (sin permiso) no consulta", async () => {
    const consultar = vi.fn(async () => ({ status: "en_proceso", updated: false }));
    renderHook(() => useSincronizarValidacionesEnCurso([signer()], consultar, vi.fn(), false));

    await act(async () => {
      await vi.advanceTimersByTimeAsync(INTERVALO_CONSULTA_MS * 2);
    });

    expect(consultar).not.toHaveBeenCalled();
  });
});
