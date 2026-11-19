import { describe, expect, it } from "vitest";
import { VACIO, etiquetaFiltro, etiquetaValor, formatoCelda, ordenarCatalogo } from "@/lib/reportes/formato";
import { CONSULTAS, CONSUMO, DERIVACIONES, SANEAMIENTOS, VEHICULOS } from "./datosReportes";

// Uso de ejemplo: formatoCelda(fila[col.clave], col, "vehiculos") → texto de la celda (fechas dd/mm/aaaa hh:mm en hora
// Colombia, moneda COP, porcentaje 0..1, enums con su nombre de negocio, null → «—»). ordenarCatalogo(catalogo.reportes)
// pone las pestañas en el orden de spec-ui §5.2.

const col = (tipo: "texto" | "numero" | "fecha" | "fecha_hora" | "moneda" | "porcentaje", clave = "x") => ({ clave, titulo: "X", tipo, pii: false });

describe("formatoCelda — formato por tipo de columna (contrato §3)", () => {
  it("fecha y fecha_hora en es-CO con hora de Colombia", () => {
    expect(formatoCelda("2026-10-22", col("fecha"), "vehiculos")).toBe("22/10/2026");
    expect(formatoCelda("2026-10-22T14:05:00Z", col("fecha_hora"), "vehiculos")).toBe("22/10/2026 09:05");
  });

  it("moneda COP entera, número con miles y porcentaje con un decimal", () => {
    expect(formatoCelda(1234567, col("moneda"), "consumo")).toBe("$ 1.234.567");
    expect(formatoCelda(10980, col("numero"), "consumo")).toBe("10.980");
    expect(formatoCelda(0.9722, col("porcentaje"), "consumo")).toBe("97,2 %");
  });

  it("null, vacío o un valor que no es del tipo salen «—» sin lanzar", () => {
    expect(formatoCelda(null, col("texto"), "vehiculos")).toBe(VACIO);
    expect(formatoCelda("", col("texto"), "vehiculos")).toBe(VACIO);
    expect(formatoCelda("no es fecha", col("fecha_hora"), "vehiculos")).toBe("no es fecha");
    expect(formatoCelda("x", col("numero"), "vehiculos")).toBe("x");
  });

  it("los enums de texto se muestran con su nombre de negocio; el texto libre queda igual", () => {
    expect(formatoCelda("EXPRESS", col("texto", "modalidad"), "vehiculos")).toBe("Express");
    expect(formatoCelda("SIMIT_VEHICULO", col("texto", "fuente"), "consultas")).toBe("SIMIT vehículo");
    expect(formatoCelda("EN_GESTION", col("texto", "estado"), "saneamientos")).toBe("En gestión");
    expect(formatoCelda("CC 0000000", col("texto", "propietarios_documentos"), "vehiculos")).toBe("CC 0000000");
  });
});

describe("etiquetas de filtros y orden del catálogo", () => {
  it("etiquetaFiltro y etiquetaValor nombran las claves y valores del catálogo", () => {
    expect(etiquetaFiltro("tipo_analisis")).toBe("Tipo de análisis");
    expect(etiquetaValor("consultas", "estado", "NA")).toBe("No aplica");
    expect(etiquetaValor("derivaciones", "estado", "FALLIDO")).toBe("Fallido");
  });

  it("ordena vehiculos, consultas, saneamientos, derivaciones, consumo", () => {
    expect(ordenarCatalogo([CONSUMO, DERIVACIONES, CONSULTAS, SANEAMIENTOS, VEHICULOS]).map((r) => r.tipo)).toEqual([
      "vehiculos",
      "consultas",
      "saneamientos",
      "derivaciones",
      "consumo",
    ]);
  });
});
