import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import { ERRORES_EMPRESA, TEXTO_SIN_PERMISO, esRechazoDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { ERRORES_BACKOFFICE } from "@/lib/backoffice/api";
import { ERRORES_CONFIGURACION } from "@/lib/configuracion/api";
import { ERRORES_DASHBOARD } from "@/lib/estadisticas/api";
import { ERRORES_REPORTES, mensajeExportacion } from "@/lib/reportes/api";
import { ERRORES_USUARIOS } from "@/lib/usuarios/api";
import * as estados from "@/lib/liquidaciones/estados";
import * as catalogo from "@/lib/reportes/catalogo";

// Uso de ejemplo: mensajeErrorFijo(new ApiError(403, "…", "EMPRESA_SOLO_SUPER_ADMIN"), "respaldo", ERRORES_DASHBOARD)
// → «Solo el Super Admin puede elegir la compañía.» (contrato impuestos.v1.yaml 1.1.0, delta 8).

/** x-flit-literales.errores de la raíz del contrato, leído del YAML (sin dependencia de un parser). */
function literalesDelContrato(): Record<string, string> {
  const yaml = readFileSync(path.resolve(__dirname, "../../../contracts/openapi/impuestos.v1.yaml"), "utf8").replace(/\r\n/g, "\n");
  const inicio = yaml.indexOf("x-flit-literales:\n  errores:\n");
  expect(inicio).toBeGreaterThanOrEqual(0);
  const literales: Record<string, string> = {};
  for (const linea of yaml.slice(inicio).split(/\r?\n/).slice(2)) {
    if (/^\s{4}#/.test(linea)) continue;
    const m = /^ {4}([A-Z_]+): (.*)$/.exec(linea);
    if (!m) break;
    literales[m[1]] = m[2].replace(/^"(.*)"$/, "$1");
  }
  return literales;
}

const CODIGOS_EMPRESA = ["EMPRESA_SOLO_SUPER_ADMIN", "EMPRESA_NO_PERMITIDA", "EMPRESA_REQUERIDA", "EMPRESA_NO_ENCONTRADA"] as const;

describe("ERRORES_EMPRESA — códigos de ?empresa del contrato 1.1.0", () => {
  it("trae los cuatro códigos con el literal exacto de x-flit-literales.errores", () => {
    const contrato = literalesDelContrato();
    for (const codigo of CODIGOS_EMPRESA) {
      expect(contrato[codigo], codigo).toBeTruthy();
      expect(ERRORES_EMPRESA[codigo], codigo).toBe(contrato[codigo]);
    }
  });

  it("EMPRESA_NO_PERMITIDA (retirado) es alias con el mismo texto que EMPRESA_SOLO_SUPER_ADMIN", () => {
    expect(ERRORES_EMPRESA.EMPRESA_NO_PERMITIDA).toBe(ERRORES_EMPRESA.EMPRESA_SOLO_SUPER_ADMIN);
  });

  it.each([
    ["configuración", ERRORES_CONFIGURACION],
    ["dashboard", ERRORES_DASHBOARD],
    ["reportes", ERRORES_REPORTES],
    ["usuarios", ERRORES_USUARIOS],
    ["liquidaciones", estados.ERRORES_LIQUIDACIONES],
    ["backoffice", ERRORES_BACKOFFICE],
  ])("el mapa de %s incluye los cuatro códigos de empresa", (_nombre, mapa) => {
    expect(mapa).toMatchObject(ERRORES_EMPRESA);
  });

  it.each([
    ["configuración", ERRORES_CONFIGURACION],
    ["usuarios", ERRORES_USUARIOS],
    ["backoffice", ERRORES_BACKOFFICE],
  ])("cada código del mapa de %s lleva el literal exacto de x-flit-literales.errores (contrato 1.1.0)", (_nombre, mapa) => {
    const contrato = literalesDelContrato();
    for (const [codigo, texto] of Object.entries(mapa)) {
      expect(contrato[codigo], codigo).toBeTruthy();
      expect(texto, codigo).toBe(contrato[codigo]);
    }
  });

  it("el backoffice trae ESTADO_NO_ADMITE_CARGA, CREDENCIAL_REQUERIDA, PROVEEDOR_NO_DISPONIBLE y EMPRESA_NO_ENCONTRADA", () => {
    for (const codigo of ["ESTADO_NO_ADMITE_CARGA", "CREDENCIAL_REQUERIDA", "CREDENCIAL_INVALIDA", "PROVEEDOR_NO_DISPONIBLE", "EMPRESA_NO_ENCONTRADA"]) {
      expect(ERRORES_BACKOFFICE[codigo], codigo).toBeTruthy();
    }
  });

  it("un 403 EMPRESA_SOLO_SUPER_ADMIN en el backoffice se traduce al literal, no al texto genérico de permiso", () => {
    expect(mensajeErrorFijo(new ApiError(403, "x", "EMPRESA_SOLO_SUPER_ADMIN"), "respaldo", ERRORES_BACKOFFICE)).toBe("Solo el Super Admin puede elegir la compañía.");
  });

  it("un 403 EMPRESA_SOLO_SUPER_ADMIN se traduce al literal, no al texto genérico de permiso", () => {
    const e = new ApiError(403, "detalle interno", "EMPRESA_SOLO_SUPER_ADMIN");
    expect(mensajeErrorFijo(e, "respaldo", ERRORES_DASHBOARD)).toBe("Solo el Super Admin puede elegir la compañía.");
    expect(mensajeErrorFijo(e, "respaldo", ERRORES_DASHBOARD)).not.toBe(TEXTO_SIN_PERMISO);
  });

  it("un 400 EMPRESA_REQUERIDA se traduce a «Elige una compañía.» en reportes y liquidaciones", () => {
    const e = new ApiError(400, "x", "EMPRESA_REQUERIDA");
    expect(mensajeErrorFijo(e, "respaldo", ERRORES_REPORTES)).toBe("Elige una compañía.");
    expect(mensajeErrorFijo(e, "respaldo", estados.ERRORES_LIQUIDACIONES)).toBe("Elige una compañía.");
    expect(mensajeErrorFijo(e, "respaldo", ERRORES_DASHBOARD)).toBe("Elige una compañía.");
  });

  it("la exportación de un reporte con 403 EMPRESA_SOLO_SUPER_ADMIN muestra el literal", () => {
    expect(mensajeExportacion(new ApiError(403, "x", "EMPRESA_SOLO_SUPER_ADMIN"))).toBe("Solo el Super Admin puede elegir la compañía.");
  });

  it("esRechazoDeEmpresa: 403 EMPRESA_SOLO_SUPER_ADMIN y el alias sí; otro 403, un 400 o un Error no", () => {
    expect(esRechazoDeEmpresa(new ApiError(403, "x", "EMPRESA_SOLO_SUPER_ADMIN"))).toBe(true);
    expect(esRechazoDeEmpresa(new ApiError(403, "x", "EMPRESA_NO_PERMITIDA"))).toBe(true);
    expect(esRechazoDeEmpresa(new ApiError(400, "x", "EMPRESA_NO_PERMITIDA"))).toBe(true);
    expect(esRechazoDeEmpresa(new ApiError(403, "x", "PRODUCTO_NO_HABILITADO"))).toBe(false);
    expect(esRechazoDeEmpresa(new ApiError(400, "x", "EMPRESA_REQUERIDA"))).toBe(false);
    expect(esRechazoDeEmpresa(new Error("x"))).toBe(false);
    expect(esRechazoDeEmpresa(null)).toBe(false);
  });
});

describe("Catálogo de reportes — una sola fuente de etiquetas (estados.ts)", () => {
  it("las etiquetas de tipo, estados, resultado, clase de error y campo faltante son las de estados.ts", () => {
    expect(catalogo.ETIQUETA_TIPO_LIQUIDACION).toBe(estados.TIPOS);
    expect(catalogo.ETIQUETA_ESTADO_LIQUIDACION).toBe(estados.ESTADOS_LIQUIDACION);
    expect(catalogo.ETIQUETA_ESTADO_ITEM).toBe(estados.ESTADOS_ITEM);
    expect(catalogo.ETIQUETA_RESULTADO).toBe(estados.RESULTADOS);
    expect(catalogo.ETIQUETA_CLASE_ERROR).toBe(estados.CLASES_ERROR);
    expect(catalogo.ETIQUETA_CAMPO_FALTANTE).toBe(estados.CAMPOS_FALTANTES);
  });

  it("el orden de EstadoItem (filtro «Estado») se conserva", () => {
    expect(Object.keys(catalogo.ETIQUETA_ESTADO_ITEM)).toEqual([
      "PENDIENTE",
      "CONSULTANDO_RUNT",
      "REINTENTO_PROGRAMADO",
      "ENVIANDO",
      "ENVIADO",
      "INCIERTO",
      "INCIERTO_REQUIERE_DECISION",
      "DATOS_INCOMPLETOS",
      "DEPARTAMENTO_NO_HABILITADO",
      "EXITOSO",
      "ERROR",
      "SIN_RESULTADO_LOTE_CERRADO",
    ]);
  });

  it("rotuloDeColumna sigue dando el literal del contrato y «Sin clasificar» para lo desconocido", () => {
    expect(catalogo.rotuloDeColumna("lotes", "estado", "CERRADO_MANUALMENTE")).toBe("Cerrado manualmente");
    expect(catalogo.rotuloDeColumna("detalle", "estado", "INCIERTO_REQUIERE_DECISION")).toBe("Incierto — requiere decisión");
    expect(catalogo.rotuloDeColumna("detalle", "tipo", "MASIVA")).toBe("Masiva");
    expect(catalogo.rotuloDeColumna("detalle", "estado", "OTRO")).toBe("Sin clasificar");
  });
});
