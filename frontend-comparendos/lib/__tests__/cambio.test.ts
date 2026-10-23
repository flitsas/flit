import { describe, expect, it } from "vitest";
import { armarRespuestaCambio, erroresRespuestaCambio, solicitudPendiente, type FormRespuestaCambio } from "@/lib/cambio";

const F: FormRespuestaCambio = { resultado: "Aceptado", causal: "", fecha: "2026-10-08", soporte: null };

describe("respuesta del organismo al cambio de contraventor", () => {
  it("una aceptación no pide causal", () => {
    expect(erroresRespuestaCambio(F, "2026-10-05", "2026-10-09")).toEqual([]);
  });

  it("un rechazo exige causal de al menos 10 caracteres", () => {
    expect(erroresRespuestaCambio({ ...F, resultado: "Rechazado", causal: "corta" }, "2026-10-05", "2026-10-09")).toEqual([
      "Escribe la causal del rechazo (al menos 10 caracteres).",
    ]);
  });

  it("la fecha va entre la solicitud y hoy", () => {
    expect(erroresRespuestaCambio({ ...F, fecha: "2026-10-04" }, "2026-10-05", "2026-10-09")).toEqual(["La fecha de respuesta debe estar entre la solicitud y hoy."]);
    expect(erroresRespuestaCambio({ ...F, fecha: "2026-10-10" }, "2026-10-05", "2026-10-09")).toHaveLength(1);
    expect(erroresRespuestaCambio({ ...F, resultado: "" }, "2026-10-05", "2026-10-09")).toEqual(["Elige si el organismo aceptó o rechazó."]);
  });

  it("solo manda la causal cuando es un rechazo", () => {
    expect(armarRespuestaCambio({ ...F, causal: "ignorada" }).get("causal")).toBeNull();
    expect(armarRespuestaCambio({ ...F, resultado: "Rechazado", causal: "  Faltó la licencia  " }).get("causal")).toBe("Faltó la licencia");
  });
});

describe("solicitud pendiente", () => {
  it("es la más reciente que sigue en Solicitado", () => {
    const s = [
      { id: "a", estado: "RechazadoOt", numeroIntento: 1 },
      { id: "b", estado: "Solicitado", numeroIntento: 2 },
      { id: "c", estado: "EnvioFallido", numeroIntento: 3 },
    ];
    expect(solicitudPendiente(s)?.id).toBe("b");
    expect(solicitudPendiente([{ id: "a", estado: "AceptadoOt", numeroIntento: 1 }])).toBeNull();
  });
});
