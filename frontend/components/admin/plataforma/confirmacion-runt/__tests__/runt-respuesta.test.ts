import { describe, expect, it } from "vitest";
import { leerRespuestaRunt } from "../runt-respuesta";

describe("leerRespuestaRunt", () => {
  it("un no encontrado sintetizado por FLIT se lee como no_encontrado con el mensaje del proveedor", () => {
    const vista = leerRespuestaRunt({ ok: false, notFound: true, providerKey: "kyverum_runt", message: "no corresponde" });
    expect(vista.resultado).toBe("no_encontrado");
    expect(vista.proveedor).toBe("kyverum");
    expect(vista.mensaje).toBe("no corresponde");
  });

  it("un error HTTP guardado como evidencia (Verifik 409) se lee como error, no como no encontrado", () => {
    const vista = leerRespuestaRunt({
      ok: false,
      error: true,
      providerKey: "verifik",
      statusCode: 409,
      providerBody: { code: "MissingParameter", message: "missing plate" },
    });
    expect(vista.resultado).toBe("error");
    expect(vista.proveedor).toBe("verifik");
    expect(vista.mensaje).toBe("HTTP 409 — MissingParameter: missing plate");
  });

  it("una respuesta Kyverum con vehículo se lee como encontrado", () => {
    const vista = leerRespuestaRunt({ ok: true, data: { vehiculo: { placa: "JNH38H", estadoAutomotor: "ACTIVO" }, solicitudes: [] } });
    expect(vista.resultado).toBe("encontrado");
    expect(vista.proveedor).toBe("kyverum");
  });
});
