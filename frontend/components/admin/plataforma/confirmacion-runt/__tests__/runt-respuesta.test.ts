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

  // Bug #13203 — el RNGM (Kyverum y Verifik) entrega cada garantía con entidad / numeroDocumentoEntidad /
  // tipoDocumentoEntidad / fechaRegistro: el panel de Confirmación RUNT pintaba «—».
  const GARANTIA_RNGM = {
    idPrenda: "1000001",
    idVehiculoPrenda: "1000002",
    fechaRegistro: "30/09/2026",
    tipoDocumentoEntidad: "NIT",
    numeroDocumentoEntidad: "900000001",
    entidad: "BANCO DE PRUEBA S.A.",
    estado: "Registro de la garantía en el RNGM por parte de RUNT",
  };

  it("Bug #13203 — Kyverum: una garantía con el shape del RNGM muestra acreedor, documento y fecha", () => {
    const vista = leerRespuestaRunt({
      ok: true,
      data: { vehiculo: { placa: "AAA000" }, solicitudes: [], garantiasPrendas: [GARANTIA_RNGM] },
    });
    expect(vista.garantias).toEqual([
      { acreedor: "BANCO DE PRUEBA S.A.", documento: "NIT 900000001", fechaInscripcion: "30/09/2026" },
    ]);
  });

  it("Bug #13203 — Verifik: garantiasMobiliarias con el shape del RNGM muestra acreedor, documento y fecha", () => {
    const vista = leerRespuestaRunt({
      ok: true,
      data: { informacionGeneral: { noPlaca: "AAA000" }, garantiasMobiliarias: [GARANTIA_RNGM] },
    });
    expect(vista.garantias).toEqual([
      { acreedor: "BANCO DE PRUEBA S.A.", documento: "NIT 900000001", fechaInscripcion: "30/09/2026" },
    ]);
  });

  it("Bug #13203 — nombreAcreedor es respaldo de acreedor, y las claves actuales siguen ganando al RNGM", () => {
    const vista = leerRespuestaRunt({
      ok: true,
      data: {
        vehiculo: { placa: "AAA000" },
        garantias: [
          { ...GARANTIA_RNGM, nombreAcreedor: "NORMALIZADO S.A." },
          {
            ...GARANTIA_RNGM,
            acreedor: "ACTUAL S.A.",
            tipoDocumentoAcreedor: "CC",
            numeroDocumentoAcreedor: "800000002",
            fechaInscripcion: "2026-01-15T00:00:00",
          },
        ],
      },
    });
    expect(vista.garantias).toEqual([
      { acreedor: "NORMALIZADO S.A.", documento: "NIT 900000001", fechaInscripcion: "30/09/2026" },
      { acreedor: "ACTUAL S.A.", documento: "CC 800000002", fechaInscripcion: "15/01/2026" },
    ]);
  });
});
