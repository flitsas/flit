import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import { errorTope, esAutomatizable, estadoTexto, mensajeError } from "@/lib/envioAutomaticoDp";

describe("envío automático de DP (HU #14614)", () => {
  it("el tope es un entero de 1 a 500", () => {
    for (const v of [1, 50, 500]) expect(errorTope(v)).toBeNull();
    for (const v of [0, 501, -1, 2.5, Number.NaN]) expect(errorTope(v)).toMatch(/entre 1 y 500/);
  });

  it("solo DCI, DBN, DPR, DLL y DRP se pueden automatizar", () => {
    for (const c of ["DCI", "DBN", "DPR", "DLL", "DRP"]) expect(esAutomatizable(c)).toBe(true);
    expect(esAutomatizable("DXX")).toBe(false);
  });

  it("cada error del contrato sale en español claro, sin el código", () => {
    for (const c of ["CAPACIDAD_DP_INACTIVA", "TOPE_INVALIDO", "TIPOLOGIA_NO_HABILITADA", "TIPOLOGIA_SIN_REGLA_AUTOMATICA", "TIPOLOGIA_SIN_REVISION_JURIDICA", "VERSION_NO_PUBLICADA"]) {
      const m = mensajeError(new ApiError(409, "texto de la API", c), "x");
      expect(m).not.toContain("_");
      expect(m).not.toBe("texto de la API");
    }
    expect(mensajeError(new ApiError(422, "Escribe la nota.", "NOTA_REQUERIDA"), "x")).toBe("Escribe la nota.");
    expect(mensajeError("raro", "No se pudo guardar.")).toBe("No se pudo guardar.");
  });

  it("el texto publicado sin aprobar se puede aprobar; el aprobado o el que no existe, no", () => {
    expect(estadoTexto(true, null)).toMatchObject({ aprobable: true, tono: "warning" });
    expect(estadoTexto(true, "2026-11-18T10:00:00Z")).toMatchObject({ aprobable: false, tono: "success" });
    expect(estadoTexto(false, null)).toMatchObject({ aprobable: false });
  });
});
