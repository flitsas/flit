import { act, renderHook, waitFor } from "@testing-library/react";
import { beforeEach, describe, expect, it, vi } from "vitest";
import { useMandatoFormatos } from "@/hooks/useMandatoFormatos";

const listMandatoFormats = vi.fn();
vi.mock("@/lib/api/admin-plataforma-mandatos", () => ({
  listMandatoFormats: (...a: unknown[]) => listMandatoFormats(...a),
}));

const item = { code: "generico", name: "Genérico", assignmentMode: "signer", baseRedaction: "generico", selectableAsRedaction: true, delegatesToOfficeTemplate: false };

describe("useMandatoFormatos (HU #13174)", () => {
  beforeEach(() => listMandatoFormats.mockReset());

  it("empieza cargando y termina con los formatos del backend", async () => {
    listMandatoFormats.mockResolvedValue([item]);
    const { result } = renderHook(() => useMandatoFormatos());
    expect(result.current.status).toBe("loading");
    await waitFor(() => expect(result.current.status).toBe("ready"));
    expect(result.current.formatos).toEqual([item]);
  });

  it("si falla queda en error y reload vuelve a pedir", async () => {
    listMandatoFormats.mockRejectedValueOnce(new Error("x")).mockResolvedValueOnce([item]);
    const { result } = renderHook(() => useMandatoFormatos());
    await waitFor(() => expect(result.current.status).toBe("error"));
    expect(result.current.formatos).toEqual([]);
    act(() => result.current.reload());
    await waitFor(() => expect(result.current.status).toBe("ready"));
    expect(listMandatoFormats).toHaveBeenCalledTimes(2);
  });
});
