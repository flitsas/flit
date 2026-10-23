import { afterEach, describe, expect, it, vi } from "vitest";
import { ApiError } from "@/lib/api";
import * as apiDiagnosticos from "@/lib/diagnosticos/api";
import { armarAlcance, crearDiagnostico, editarDiagnostico, fuentesOfrecidas, listarTiposTramite } from "@/lib/diagnosticos/api";
import type { Fuente } from "@/lib/configuracion";

const SIN_IMPUESTO: Fuente[] = ["RUNT", "RUNT_PROPIETARIO", "RUNT_LOCATARIO", "SIMIT_VEHICULO", "SIMIT_PROPIETARIO", "SIMIT_LOCATARIO", "AVALUO"];
const CON_IMPUESTO: Fuente[] = [...SIN_IMPUESTO, "IMPUESTO_DEPARTAMENTAL"];

// Uso de ejemplo: await crearDiagnostico({ nombre: "Flota norte", modo: "SIMPLE", tipo_tramite_codigo: null,
// alcance: armarAlcance(["RUNT"]) }) → Diagnostico en CREADO

afterEach(() => vi.unstubAllGlobals());

const CABECERA = { nombre: "Flota norte", modo: "SIMPLE" as const, tipo_tramite_codigo: null, alcance: armarAlcanceSeguro() };

function armarAlcanceSeguro() {
  return { por_vehiculo: ["RUNT" as const], por_propietario: [], por_locatario: [] };
}

describe("contrato de la cabecera", () => {
  it("armarAlcance reparte cada fuente en su grupo (R-24)", () => {
    expect(armarAlcance(["SIMIT_LOCATARIO", "RUNT", "RUNT_PROPIETARIO", "AVALUO", "RUNT_LOCATARIO"])).toEqual({
      por_vehiculo: ["RUNT", "AVALUO"],
      por_propietario: ["RUNT_PROPIETARIO"],
      por_locatario: ["RUNT_LOCATARIO", "SIMIT_LOCATARIO"],
    });
  });

  // HU #13865: la disponibilidad sale solo de fuentes_disponibles del backend.
  it("fuentesOfrecidas ofrece el impuesto si está habilitado y llega en fuentes_disponibles", () => {
    expect(fuentesOfrecidas(["SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"], CON_IMPUESTO)).toEqual(["RUNT", "SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"]);
  });

  it("fuentesOfrecidas quita el impuesto habilitado cuando no llega en fuentes_disponibles", () => {
    expect(fuentesOfrecidas(["SIMIT_VEHICULO", "IMPUESTO_DEPARTAMENTAL"], SIN_IMPUESTO)).toEqual(["RUNT", "SIMIT_VEHICULO"]);
  });

  it("fuentesOfrecidas suma RUNT siempre, respeta el orden del enum y quita cualquier fuente no disponible (sin lista fija)", () => {
    expect(fuentesOfrecidas(["AVALUO", "SIMIT_VEHICULO", "RUNT_PROPIETARIO"], ["RUNT", "SIMIT_VEHICULO", "RUNT_PROPIETARIO"])).toEqual([
      "RUNT",
      "RUNT_PROPIETARIO",
      "SIMIT_VEHICULO",
    ]);
    expect(fuentesOfrecidas([], [])).toEqual(["RUNT"]);
  });

  it("ya no exporta la lista fija FUENTES_NO_DISPONIBLES", () => {
    expect(apiDiagnosticos).not.toHaveProperty("FUENTES_NO_DISPONIBLES");
  });

  it("crearDiagnostico hace POST /diagnosticos con el cuerpo en snake_case", async () => {
    const fetchMock = vi.fn(async () => Response.json({ id: "d1" }, { status: 201 }));
    vi.stubGlobal("fetch", fetchMock);

    await crearDiagnostico(CABECERA);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/diagnostico/diagnosticos");
    expect(init.method).toBe("POST");
    expect(JSON.parse(String(init.body))).toEqual(CABECERA);
  });

  it("editarDiagnostico hace PUT /diagnosticos/{id}", async () => {
    const fetchMock = vi.fn(async () => Response.json({ id: "d1" }));
    vi.stubGlobal("fetch", fetchMock);

    await editarDiagnostico("d1", CABECERA);

    const [url, init] = fetchMock.mock.calls[0] as unknown as [string, RequestInit];
    expect(url).toBe("/api/v1/diagnostico/diagnosticos/d1");
    expect(init.method).toBe("PUT");
  });

  it("un 422 conserva code y campo del ErrorRegla", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ code: "NOMBRE_INVALIDO", error: "Nombre inválido", campo: "nombre" }, { status: 422 })));

    const error = await crearDiagnostico(CABECERA).catch((e: unknown) => e);

    expect(error).toBeInstanceOf(ApiError);
    expect(error).toMatchObject({ status: 422, code: "NOMBRE_INVALIDO", campo: "nombre", message: "Nombre inválido" });
  });

  it("listarTiposTramite devuelve la lista y un 503 trae los segundos de Retry-After", async () => {
    vi.stubGlobal("fetch", vi.fn(async () => Response.json({ tipos: [{ codigo: "TRASPASO_STANDARD", nombre: "TRASPASO", familia: "TRASPASO", fuentesObligatorias: [] }] })));
    expect((await listarTiposTramite()).map((t) => t.codigo)).toEqual(["TRASPASO_STANDARD"]);

    vi.stubGlobal(
      "fetch",
      vi.fn(async () => Response.json({ code: "CATALOGO_NO_DISPONIBLE", error: "Catálogo caído" }, { status: 503, headers: { "Retry-After": "30" } })),
    );
    const error = await listarTiposTramite().catch((e: unknown) => e);
    expect(error).toMatchObject({ status: 503, code: "CATALOGO_NO_DISPONIBLE", retryAfter: 30 });
  });
});
