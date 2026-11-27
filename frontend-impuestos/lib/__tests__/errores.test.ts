import { readFileSync } from "node:fs";
import path from "node:path";
import { describe, expect, it } from "vitest";
import { ApiError } from "@/lib/api";
import { ERRORES_COMUNES, ERRORES_EMPRESA, TEXTO_SIN_PERMISO, esErrorDeEmpresa, esRechazoDeEmpresa, mensajeErrorFijo } from "@/lib/errores";
import { ERRORES_BACKOFFICE, ETIQUETA_MODO_RESPALDO_FIRMA, ETIQUETA_MOTIVO_APROVISIONAMIENTO } from "@/lib/backoffice/api";
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
    ["liquidaciones", estados.ERRORES_LIQUIDACIONES],
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

describe("esErrorDeEmpresa — errores de ?empresa que se pintan en línea (liquidaciones, contrato 1.1.0)", () => {
  it.each([
    [400, "EMPRESA_REQUERIDA"],
    [404, "EMPRESA_NO_ENCONTRADA"],
    [403, "EMPRESA_SOLO_SUPER_ADMIN"],
    [403, "EMPRESA_NO_PERMITIDA"],
  ])("%i %s es un error de compañía", (status, code) => {
    expect(esErrorDeEmpresa(new ApiError(status, "x", code))).toBe(true);
  });

  it("otro código, un ApiError sin código, un Error o null no lo son", () => {
    expect(esErrorDeEmpresa(new ApiError(404, "x", "LIQUIDACION_NO_ENCONTRADA"))).toBe(false);
    expect(esErrorDeEmpresa(new ApiError(403, "x", "PRODUCTO_NO_HABILITADO"))).toBe(false);
    expect(esErrorDeEmpresa(new ApiError(404, "x"))).toBe(false);
    expect(esErrorDeEmpresa(new Error("EMPRESA_REQUERIDA"))).toBe(false);
    expect(esErrorDeEmpresa(null)).toBe(false);
  });

  it.each([
    ["FILTRO_INVALIDO", "Un filtro no es válido."],
    ["PAGINACION_INVALIDA", "La paginación no es válida."],
    ["RANGO_INVALIDO", "El rango de fechas no es válido."],
  ])("un 400 %s del grid de liquidaciones se traduce al literal del contrato", (code, literal) => {
    expect(mensajeErrorFijo(new ApiError(400, "detalle interno", code), "respaldo", estados.ERRORES_LIQUIDACIONES)).toBe(literal);
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

/**
 * x-flit-etiquetas del esquema `nombre` de components.schemas (el primer bloque de etiquetas tras su declaración), leído
 * del YAML sin parser: claves en MAYÚSCULAS o entre comillas, valores con o sin comillas.
 */
function etiquetasDelEsquema(nombre: string, desde = 0): Record<string, string> {
  const yaml = contratoYaml();
  const decl = yaml.slice(desde).search(new RegExp(`\n +${nombre}:\n`)) + desde;
  expect(decl, nombre).toBeGreaterThanOrEqual(desde);
  const marca = yaml.indexOf("x-flit-etiquetas:\n", decl);
  expect(marca, nombre).toBeGreaterThan(decl);
  const sangria = marca - yaml.lastIndexOf("\n", marca) - 1;
  const etiquetas: Record<string, string> = {};
  for (const linea of yaml.slice(marca).split("\n").slice(1)) {
    const m = /^( *)"?([A-Za-z_]+)"?: (.*)$/.exec(linea);
    if (!m || m[1].length <= sangria) break;
    etiquetas[m[2]] = m[3].replace(/^"(.*)"$/, "$1");
  }
  return etiquetas;
}

const contratoYaml = () => readFileSync(path.resolve(__dirname, "../../../contracts/openapi/impuestos.v1.yaml"), "utf8").replace(/\r\n/g, "\n");

describe("Backoffice — etiquetas del aprovisionamiento según el contrato 1.2.0 (plan §5.8)", () => {
  it("ETIQUETA_MOTIVO_APROVISIONAMIENTO es exactamente MotivoAprovisionamiento.x-flit-etiquetas (incluidos los retirados)", () => {
    const contrato = etiquetasDelEsquema("MotivoAprovisionamiento");
    expect(Object.keys(contrato)).toEqual(expect.arrayContaining(["EMPRESA_INACTIVA_EN_PROVEEDOR", "CLAVE_ROTADA", "EMPRESA_YA_REGISTRADA", "ALTA_SIN_CONFIRMAR"]));
    expect(ETIQUETA_MOTIVO_APROVISIONAMIENTO).toEqual(contrato);
  });

  it("ETIQUETA_MODO_RESPALDO_FIRMA es la etiqueta \"true\" de AprovisionamientoDetalle.modoRespaldoFirma", () => {
    const yaml = contratoYaml();
    const detalle = yaml.indexOf("\n    AprovisionamientoDetalle:\n");
    expect(detalle).toBeGreaterThanOrEqual(0);
    const etiquetas = etiquetasDelEsquema("modoRespaldoFirma", detalle).true;
    expect(etiquetas).toBeTruthy();
    expect(ETIQUETA_MODO_RESPALDO_FIRMA).toBe(etiquetas);
  });
});

// Uso de ejemplo: mensajeErrorFijo(new ApiError(409, "…", "CONFLICTO_CONCURRENCIA"), "respaldo", ERRORES_CONFIGURACION)
// → «Otro usuario cambió este registro al mismo tiempo. Recarga e inténtalo de nuevo.» (contrato 1.2.2).
const LITERALES_COMUNES = {
  CONFLICTO_CONCURRENCIA: "Otro usuario cambió este registro al mismo tiempo. Recarga e inténtalo de nuevo.",
  ESCRITURA_ENTRE_EMPRESAS: "No puedes modificar datos de otra compañía.",
  SIN_USUARIO: "Tu sesión no identifica a un usuario. Vuelve a iniciar sesión.",
  VALOR_INVALIDO: "Un valor enviado no es válido. Revisa los datos e inténtalo de nuevo.",
} as const;

describe("ERRORES_COMUNES — códigos transversales del contrato 1.2.2", () => {
  it("trae los cuatro códigos con el literal exacto", () => {
    expect(ERRORES_COMUNES).toEqual(LITERALES_COMUNES);
  });

  it.each([
    [409, "CONFLICTO_CONCURRENCIA"],
    [403, "ESCRITURA_ENTRE_EMPRESAS"],
    [401, "SIN_USUARIO"],
    [400, "VALOR_INVALIDO"],
  ] as const)("un %i %s se traduce a su literal en todos los mapas de módulo y sin mapa, nunca al respaldo ni al texto de permiso", (status, code) => {
    const e = new ApiError(status, "detalle interno", code);
    for (const mapa of [ERRORES_CONFIGURACION, ERRORES_DASHBOARD, ERRORES_REPORTES, ERRORES_USUARIOS, estados.ERRORES_LIQUIDACIONES, ERRORES_BACKOFFICE, undefined]) {
      expect(mensajeErrorFijo(e, "respaldo", mapa)).toBe(LITERALES_COMUNES[code]);
    }
  });

  it("la exportación de un reporte con 400 VALOR_INVALIDO muestra el literal común", () => {
    expect(mensajeExportacion(new ApiError(400, "x", "VALOR_INVALIDO"))).toBe(LITERALES_COMUNES.VALOR_INVALIDO);
  });

  it("el mapa del módulo manda sobre el común, y un código desconocido sigue cayendo al respaldo o al texto de permiso", () => {
    expect(mensajeErrorFijo(new ApiError(409, "x", "CONFLICTO_CONCURRENCIA"), "respaldo", { CONFLICTO_CONCURRENCIA: "propio" })).toBe("propio");
    expect(mensajeErrorFijo(new ApiError(400, "x", "OTRO_CODIGO"), "respaldo")).toBe("respaldo");
    expect(mensajeErrorFijo(new ApiError(403, "x", "OTRO_CODIGO"), "respaldo")).toBe(TEXTO_SIN_PERMISO);
  });
});
