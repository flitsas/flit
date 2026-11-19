import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import {
  cuerpoAjusteDictamen,
  errorAjusteDictamen,
  puedeAplicarAjuste,
  largoTexto,
  validarAlertamiento,
  validarAlertamientos,
  validarMotivoAjuste,
  type FilaAlertamiento,
} from "@/lib/backoffice/ajusteDictamen";

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

describe("HU14562 AC45 alertamientos del ajuste", () => {
  const fila = (n: number, cambios: Partial<FilaAlertamiento> = {}): FilaAlertamiento => ({ clave: `Alertamiento ${n}`, ...ALERTA, ...cambios });

  it("el cuerpo manda solo codigo, texto, efecto y fuente (sin la clave ni la marca de la fila)", () => {
    const c = cuerpoAjusteDictamen({ versionEsperada: 2, veredicto: "BLOQUEADO", alertamientos: [fila(1, { ajuste: "AGREGADO" })], motivo: "Motivo suficiente" });
    expect(c.alertamientos).toEqual([ALERTA]);
  });

  it("valida texto obligatorio y ≤ 500, P3 en AJUSTE_FLIT y la coherencia con el veredicto", () => {
    expect(validarAlertamientos("BLOQUEADO", [fila(1, { texto: "  " })])).toEqual({ campo: "alertamientos[0].texto", mensaje: "Escribe el texto del alertamiento." });
    expect(validarAlertamientos("BLOQUEADO", [fila(1, { texto: "x".repeat(501) })])?.mensaje).toMatch(/500/);
    expect(validarAlertamientos("BLOQUEADO", [fila(1, { texto: "Radicado 1234567 pendiente" })])).toEqual({
      campo: "alertamientos[0].texto",
      mensaje: "No escribas secuencias de 6 o más dígitos (pueden ser documentos).",
    });
    expect(validarAlertamientos("BLOQUEADO", [fila(1, { codigo: "SOAT_NO_VIGENTE", texto: "Póliza 1234567" })])).toBeNull();
    expect(validarAlertamientos("VIABLE", [fila(1)])?.campo).toBe("veredicto");
    expect(validarAlertamientos("BLOQUEADO", [fila(1, { efecto: "ADVIERTE" })])?.campo).toBe("veredicto");
    expect(validarAlertamientos("VIABLE", [fila(1, { efecto: "ADVIERTE" })])).toBeNull();
  });

  it("puedeAplicarAjuste exige veredicto, motivo válido y alertamientos válidos", () => {
    expect(puedeAplicarAjuste({ veredicto: null, motivo: "Motivo suficiente", alertamientos: [] })).toBe(false);
    expect(puedeAplicarAjuste({ veredicto: "VIABLE", motivo: "corto", alertamientos: [] })).toBe(false);
    expect(puedeAplicarAjuste({ veredicto: "VIABLE", motivo: "Motivo suficiente", alertamientos: [] })).toBe(true);
    expect(puedeAplicarAjuste({ veredicto: "VIABLE", motivo: "Motivo suficiente", alertamientos: [fila(1)] })).toBe(false);
  });
});

// Observación 2 del code review (HU #14562): el BE cuenta el largo en puntos de código, no en unidades UTF-16. Un emoji
// cuenta 1, así que el FE no rechaza un motivo que el BE acepta ni acepta uno que el BE rechaza.
describe("HU14562 code review obs. 2: largos en puntos de código", () => {
  const EMOJI = "\u{1F697}"; // 1 punto de código, 2 unidades UTF-16.

  it("largoTexto cuenta puntos de código", () => {
    expect(EMOJI.length).toBe(2);
    expect(largoTexto(EMOJI)).toBe(1);
    expect(largoTexto("ñandú")).toBe(5);
    expect(largoTexto("")).toBe(0);
  });

  it("el mínimo del motivo se mide en puntos de código: 5 emojis no alcanzan 10", () => {
    expect(validarMotivoAjuste(EMOJI.repeat(5))).toBe("Escribe al menos 10 caracteres.");
    expect(validarMotivoAjuste(EMOJI.repeat(10))).toBeNull();
  });

  it("el máximo del motivo se mide en puntos de código: 500 emojis caben y 501 no", () => {
    expect(validarMotivoAjuste(EMOJI.repeat(500))).toBeNull();
    expect(validarMotivoAjuste(EMOJI.repeat(501))).toBe("El motivo admite máximo 500 caracteres.");
  });

  it("el texto del alertamiento también se mide en puntos de código", () => {
    expect(validarAlertamiento({ ...ALERTA, texto: EMOJI.repeat(500) })).toBeNull();
    expect(validarAlertamiento({ ...ALERTA, texto: EMOJI.repeat(501) })).toBe("El texto admite máximo 500 caracteres.");
  });
});
