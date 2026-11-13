import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import { cuerpoAjusteDictamen, errorAjusteDictamen, validarMotivoAjuste } from "@/lib/backoffice/ajusteDictamen";

// Uso de ejemplo: cuerpoAjusteDictamen({ versionEsperada: 3, veredicto: "BLOQUEADO", alertamientos, motivo }) arma el
// PUT del ajuste FLIT (B7, D9: siempre con motivo); errorAjusteDictamen(err) traduce el 409/422 del API a un mensaje.

const ALERTA = { codigo: "AJUSTE_FLIT", texto: "Prenda vigente sin levantar", efecto: "BLOQUEA" as const, fuente: "RUNT" };

describe("HU14562 AC45 ajuste del dictamen", () => {
  it("el cuerpo lleva version_esperada, veredicto, alertamientos y motivo (nunca el cuerpo viejo)", () => {
    const c = cuerpoAjusteDictamen({ versionEsperada: 3, veredicto: "BLOQUEADO", alertamientos: [ALERTA], motivo: "  Corrección por soporte  " });
    expect(c).toEqual({ version_esperada: 3, veredicto: "BLOQUEADO", alertamientos: [ALERTA], motivo: "Corrección por soporte" });
    expect(c).not.toHaveProperty("confirmado");
  });

  it("confirmar_con_tramite solo viaja cuando se confirma", () => {
    const c = cuerpoAjusteDictamen({ versionEsperada: 1, veredicto: "VIABLE", alertamientos: [], motivo: "Motivo suficiente", confirmarConTramite: true });
    expect(c.confirmar_con_tramite).toBe(true);
  });

  it("motivo: obligatorio de 10 a 500 caracteres (sin espacios de borde)", () => {
    expect(validarMotivoAjuste("")).toMatch(/obligatorio/i);
    expect(validarMotivoAjuste("   corto   ")).toMatch(/al menos 10/);
    expect(validarMotivoAjuste("x".repeat(501))).toMatch(/500/);
    expect(validarMotivoAjuste("Motivo válido")).toBeNull();
  });
});

describe("HU14562 AC46 errores del PUT del dictamen", () => {
  it.each([
    [409, "DICTAMEN_DESACTUALIZADO", "El dictamen cambió mientras editabas. Recarga para ver la versión vigente", true],
    [409, "DICTAMEN_EN_CONFLICTO", "Otro ajuste se aplicó al mismo tiempo. Recarga para ver la versión vigente", true],
    [422, "DICTAMEN_INCOHERENTE", "Un dictamen Viable no puede tener alertamientos que bloqueen, y uno Bloqueado necesita al menos uno", false],
    [409, "VEHICULO_CON_TRAMITE", "El vehículo ya tiene un trámite creado; el ajuste no lo modifica. Confirma para continuar", false],
  ])("%s %s → mensaje fijo", (status, code, mensaje, recargar) => {
    const e = errorAjusteDictamen(new ApiError(status, "x", code));
    expect(e.mensaje).toBe(mensaje);
    expect(e.ofrecerRecargar).toBe(recargar);
    expect(e.pedirConfirmarTramite).toBe(code === "VEHICULO_CON_TRAMITE");
  });

  it("P3: 422 por secuencia de 6 o más dígitos se asigna al campo del alertamiento", () => {
    const e = errorAjusteDictamen(new ApiError(422, "El texto no puede llevar secuencias de 6 o más dígitos", "VALOR_INVALIDO", "alertamientos[0].texto"));
    expect(e.campo).toBe("alertamientos[0].texto");
    expect(e.mensaje).toMatch(/6 o más dígitos/);
    expect(e.ofrecerRecargar).toBe(false);
  });

  it("un error desconocido no inventa mensaje de dominio", () => {
    const e = errorAjusteDictamen(new Error("red"));
    expect(e.mensaje).toBe("No se pudo aplicar el ajuste. Intenta de nuevo en un momento.");
    expect(e.campo).toBeNull();
  });
});
