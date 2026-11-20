import { describe, expect, it, vi } from "vitest";
import type { Solicitud } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
import { crearGuardadorSerial } from "../guardadoSerial";

const sol = (version: number) => ({ version }) as Solicitud;

function montar() {
  let version = 1;
  const aplicar = (s: Solicitud) => {
    version = s.version;
  };
  const releer = vi.fn(async () => sol(version + 10));
  return { guardar: crearGuardadorSerial(() => version, aplicar, releer), releer, version: () => version };
}

describe("crearGuardadorSerial", () => {
  it("un guardado que llega durante otro espera y usa la versión que dejó el primero (sin 409)", async () => {
    const { guardar } = montar();
    const usadas: number[] = [];
    let soltar!: () => void;
    const primero = guardar(async (v) => {
      usadas.push(v);
      await new Promise<void>((r) => (soltar = r));
      return sol(v + 1);
    });
    const segundo = guardar(async (v) => {
      usadas.push(v);
      return sol(v + 1);
    });
    await Promise.resolve();
    expect(usadas).toEqual([1]);
    soltar();
    await Promise.all([primero, segundo]);
    expect(usadas).toEqual([1, 2]);
  });

  it("un 409 conflicto_version se reintenta una vez con la versión releída", async () => {
    const { guardar, releer } = montar();
    const escribir = vi
      .fn<(v: number) => Promise<Solicitud>>()
      .mockRejectedValueOnce(new ApiError("conflicto_version", "x", 409))
      .mockImplementation(async (v) => sol(v + 1));
    await guardar(escribir);
    expect(releer).toHaveBeenCalledTimes(1);
    expect(escribir.mock.calls.map((c) => c[0])).toEqual([1, 11]);
  });

  it("si el conflicto persiste, se propaga y la cola sigue viva", async () => {
    const { guardar } = montar();
    const falla = vi.fn(async () => {
      throw new ApiError("conflicto_version", "x", 409);
    });
    await expect(guardar(falla)).rejects.toBeInstanceOf(ApiError);
    expect(falla).toHaveBeenCalledTimes(2);
    await expect(guardar(async (v) => sol(v + 1))).resolves.toBeUndefined();
  });
});
