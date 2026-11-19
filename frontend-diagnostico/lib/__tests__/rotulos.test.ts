import { afterEach, describe, expect, it, vi } from "vitest";
import {
  ALERTAS_DICTAMEN,
  pareceCodigo,
  rotular,
  ROTULO_SIN_CLASIFICAR,
  TIPOS_IDENTIFICADOR,
} from "@/lib/rotulos";
import { nombreFuente } from "@/lib/configuracion";
import { etiquetaEstado, nombreModo } from "@/lib/diagnosticos/listado";
import type { TipoIdentificadorVehiculo } from "@/lib/carga/api";

// Uso de ejemplo: rotular({ contexto: "alerta del dictamen", codigo: "SOAT_NO_VIGENTE", etiqueta: alerta.etiqueta, respaldo: ALERTAS_DICTAMEN })
// → la etiqueta de la API; si no llegó, el diccionario local; si tampoco, «Novedad sin clasificar» (nunca el código) y un
// console.warn técnico con el contexto y el código.

afterEach(() => vi.restoreAllMocks());

describe("rotular — etiqueta de la API, respaldo local y genérico", () => {
  it("usa la etiqueta de la API cuando llega", () => {
    expect(rotular({ contexto: "alerta", codigo: "SOAT_NO_VIGENTE", etiqueta: "SOAT vencido", respaldo: ALERTAS_DICTAMEN })).toBe("SOAT vencido");
  });

  it("sin etiqueta, o con la etiqueta igual al código, cae al diccionario local", () => {
    expect(rotular({ contexto: "alerta", codigo: "SOAT_NO_VIGENTE", etiqueta: null, respaldo: ALERTAS_DICTAMEN })).toBe("SOAT no vigente");
    expect(rotular({ contexto: "alerta", codigo: "SOAT_NO_VIGENTE", etiqueta: "  ", respaldo: ALERTAS_DICTAMEN })).toBe("SOAT no vigente");
    expect(rotular({ contexto: "alerta", codigo: "SOAT_NO_VIGENTE", etiqueta: "SOAT_NO_VIGENTE", respaldo: ALERTAS_DICTAMEN })).toBe("SOAT no vigente");
  });

  it("sin etiqueta ni respaldo devuelve «Novedad sin clasificar» y deja un aviso técnico, nunca el código", () => {
    const aviso = vi.spyOn(console, "warn").mockImplementation(() => {});
    const texto = rotular({ contexto: "alerta del dictamen", codigo: "REGLA_NUEVA_X", respaldo: ALERTAS_DICTAMEN });
    expect(texto).toBe(ROTULO_SIN_CLASIFICAR);
    expect(texto).not.toContain("REGLA_NUEVA_X");
    expect(aviso).toHaveBeenCalledTimes(1);
    expect(String(aviso.mock.calls[0].join(" "))).toMatch(/alerta del dictamen/);
    expect(String(aviso.mock.calls[0].join(" "))).toMatch(/REGLA_NUEVA_X/);
  });

  it("no lanza con código nulo o vacío", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(rotular({ contexto: "x", codigo: null })).toBe(ROTULO_SIN_CLASIFICAR);
    expect(rotular({ contexto: "x", codigo: "" })).toBe(ROTULO_SIN_CLASIFICAR);
  });

  it("pareceCodigo distingue un código interno de un texto de negocio", () => {
    expect(pareceCodigo("PAGINACION_INVALIDA")).toBe(true);
    expect(pareceCodigo("SIN_DICTAMEN")).toBe(true);
    expect(pareceCodigo("No se pudo cargar.")).toBe(false);
    expect(pareceCodigo("RUNT")).toBe(false);
    expect(pareceCodigo("")).toBe(false);
  });
});

describe("diccionarios de respaldo — todos los códigos con rótulo", () => {
  it("las 21 reglas del catálogo v2 tienen rótulo aprobado (tabla 5.2)", () => {
    const codigos = [
      "SOAT_NO_VIGENTE",
      "RTM_NO_VIGENTE",
      "ESTADO_VEHICULO",
      "SIMIT_VEHICULO",
      "SIMIT_PROPIETARIO",
      "SIMIT_LOCATARIO",
      "SIMIT_SIN_VERIFICAR",
      "SIMIT_LICENCIA",
      "LICENCIA_CONDUCTOR",
      "SIMIT_ACUERDO_PAGO",
      "LIMITACION",
      "GRAVAMEN",
      "GARANTIA_MOBILIARIA",
      "PRENDA",
      "INFRACCION_RUNT",
      "IMPUESTO",
    ];
    for (const c of codigos) expect(ALERTAS_DICTAMEN[c], c).toBeTruthy();
    expect(ALERTAS_DICTAMEN.SIMIT_SIN_VERIFICAR).toBe("No se pudo verificar multas");
    expect(ALERTAS_DICTAMEN.SIMIT_LICENCIA).toBe("Licencia suspendida o cancelada (SIMIT)");
  });

  it("cada tipo de identificador tiene rótulo, incluido el motocarro", () => {
    const tipos: Record<TipoIdentificadorVehiculo, true> = {
      PLACA_CARRO: true,
      PLACA_MOTO: true,
      PLACA_REMOLQUE: true,
      PLACA_MAQUINARIA: true,
      PLACA_MOTOCARRO: true,
      VIN: true,
    };
    for (const t of Object.keys(tipos)) expect(TIPOS_IDENTIFICADOR[t], t).toBeTruthy();
    expect(TIPOS_IDENTIFICADOR.PLACA_MOTOCARRO).toBe("Placa de motocarro");
  });

  it("los diccionarios existentes ya no devuelven el código cuando no lo conocen", () => {
    vi.spyOn(console, "warn").mockImplementation(() => {});
    expect(nombreFuente("FUENTE_NUEVA" as never)).toBe(ROTULO_SIN_CLASIFICAR);
    expect(nombreModo("MODO_NUEVO" as never)).toBe(ROTULO_SIN_CLASIFICAR);
    expect(etiquetaEstado("ESTADO_NUEVO" as never).texto).toBe(ROTULO_SIN_CLASIFICAR);
    expect(nombreFuente("RUNT")).toBe("RUNT vehículo");
  });
});
