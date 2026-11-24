import { describe, expect, it } from "vitest";
import { hojasDp } from "@/components/reportes/dp/excelDp";
import type { ReporteDp } from "@/components/reportes/dp/tipos";
import { dia, hojaNotas, nombreArchivo, porcentaje, type CorteExcel } from "@/components/reportes/gestion/excel";
import { hojasGestion } from "@/components/reportes/gestion/excelGestion";
import { hojasResumen } from "@/components/reportes/gestion/excelResumen";
import type { RespuestaGestion } from "@/components/reportes/gestion/tipos";
import { hojasPlataforma } from "@/components/reportes/plataforma/excelPlataforma";
import type { ReportePlataforma } from "@/components/reportes/plataforma/tipos";
import type { Dashboard, Dinero, Operacion } from "@/lib/reportes";
import { ESTADO_LEGAL } from "@/lib/etiquetas";
import { buildXlsxLibro, type XlsxSheet } from "@/lib/xlsx";

// Reportes 2.0 (S3): «Descargar Excel» arma el libro con los datos ya cargados. Las cifras son las de la pantalla.
const hoja = (hojas: XlsxSheet[], nombre: string) => {
  const h = hojas.find((x) => x.name === nombre);
  if (!h) throw new Error(`falta la hoja ${nombre}; hay: ${hojas.map((x) => x.name).join(", ")}`);
  return h;
};
const valor = (h: XlsxSheet, etiqueta: string) => h.rows.find((r) => r[0] === etiqueta)?.[1];
const texto = (hojas: XlsxSheet[]) => JSON.stringify(hojas);

const operacion: Operacion = {
  hoy: "2026-11-24",
  conValores: true,
  resumen: { pendientes: 42, valorPendiente: 130_900_000, vehiculos: 30, nuevos30: 7, resueltos30: 3 },
  gestion: { pasos: [{ estadoProceso: "SinProcesar", cantidad: 20 }, { estadoProceso: "RespuestaFavorable", cantidad: 1 }], tasaExito: 0.25 },
  vehiculos: { conTresOMas: 2, concentracionDiezPorCiento: 0.3, top: [{ placa: "AAA111", cantidad: 5, organismos: 2, valor: 3_020_500 }] },
  infracciones: [{ codigo: "C29", descripcion: "Exceso de velocidad", cantidad: 30, valor: 90_000_000 }],
  evitables: { soat: { cantidad: 4, valor: 5_000_000 }, tecnicoMecanica: { cantidad: 2, valor: 1_000_000 } },
  calendario: [{ periodo: "2026-11", caducan: 1, prescriben: 2 }],
  antiguedad: [{ tramo: "Menos de 30 días", cantidad: 7, valor: 1_000_000, desde: "2026-10-25", hasta: null }],
  dpPorOrganismo: [{ organismoId: "o1", nombre: "Movilidad Bogotá", enviados: 4, favorables: 1, respondidos: 4, diasPromedio: 12.5 }],
};

describe("Descargar Excel de Reportes 2.0", () => {
  it("Gestión: una hoja por bloque, con las cifras y las etiquetas de la pantalla", () => {
    const r: RespuestaGestion = { alcance: { empresa: null, nit: null, desde: null, hasta: null }, gestion: { conPeriodo: false, operacion, topOrganismos: [{ organismoId: "o1", nombre: "Movilidad Bogotá", cantidad: 40, valor: 120_000_000 }] } };
    const hojas = hojasGestion(r, "en los últimos 30 días");
    expect(hojas.map((h) => h.name)).toEqual([
      "Resumen", "Gestión", "Vehículos que más repiten", "Infracciones frecuentes", "Lo que se pudo evitar", "Calendario legal", "Antigüedad", "DP por organismo", "Organismos con más",
    ]);
    expect(hoja(hojas, "Resumen").rows[0]).toEqual(["Pendientes (activos y sin pagar)", 42, 130_900_000]);
    expect(valor(hoja(hojas, "Resumen"), "Comparendos que entraron en los últimos 30 días")).toBe(7);
    // Etiquetas de negocio, no la clave de la API.
    expect(hoja(hojas, "Gestión").rows).toEqual([["Sin procesar", 20], ["Respuesta favorable", 1]]);
    expect(hoja(hojas, "Lo que se pudo evitar").rows[0]).toEqual(["SOAT vencido (D02)", 4, 5_000_000]);
    expect(hoja(hojas, "Calendario legal").rows).toEqual([["nov 26", 1, 2]]);
    expect(texto(hojas)).not.toMatch(/SinProcesar|RespuestaFavorable|estadoProceso|Simit|fuente/);
  });

  it("DP: la tasa es la de la API en porcentaje (vacía sin respuestas) y las fechas no se corren un día", () => {
    const fila = (clave: string, tasaExito: number | null) => ({ clave, nombre: "Movilidad Bogotá", enviados: 3, favorables: 1, desfavorables: 1, sinRespuesta: 1, diasPromedio: 4.5, tasaExito });
    const d: ReporteDp = {
      tasaExito: 0.5, ahorro: 604_100, favorables: 1, desfavorables: 1, sinRespuesta: 4, enviados: 3,
      porOrganismo: [fila("o1", 0.5), { ...fila("o2", null), nombre: "Tránsito Cali" }], porInfraccion: [{ ...fila("C29", 0.5), nombre: "Exceso de velocidad" }],
      cruce: [], totalPositivos: 3, totalVencidos: 0, vencidos: [], consolidado: null,
      positivos: [{ comparendoId: "c1", numero: "1100100000001", placa: "AAA111", radicado: "FLIT-1", organismo: "Movilidad Bogotá", fechaRadicacion: "2026-11-01", fechaRespuesta: "2026-11-10", valorAlRadicar: 604_100 }],
      acumulado: [{ dia: "2026-11-01", enviados: 1, respondidos: 0 }],
    };
    const hojas = hojasDp(d);
    expect(hojas.map((h) => h.name)).toEqual(["Indicadores", "Enviados y respondidos", "Por organismo", "Por infracción", "Organismo e infracción", "Favorables", "Vencidos sin respuesta"]);
    expect(valor(hoja(hojas, "Indicadores"), "Tasa de éxito (%)")).toBe(50);
    expect(valor(hoja(hojas, "Indicadores"), "Ahorro (valor al radicar de los favorables)")).toBe(604_100);
    expect(hoja(hojas, "Por organismo").rows.map((r) => r[5])).toEqual([50, null]);
    expect(hoja(hojas, "Por infracción").rows[0][0]).toBe("C29 Exceso de velocidad");
    expect(hoja(hojas, "Favorables").rows[0][4]).toEqual({ year: 2026, month: 11, day: 1 });
    expect(hoja(hojas, "Favorables").notes).toEqual(["Se incluyen los 1 más recientes de 3."]);
    expect(hojasDp({ ...d, consolidado: [] }).map((h) => h.name)).toContain("Consolidado por empresa");
  });

  it("Plataforma (solo SuperAdmin) sí nombra la fuente, con su etiqueta de negocio", () => {
    const p: ReportePlataforma = {
      desde: "2026-11-01", hasta: "2026-11-24",
      fuentes: [{ fuente: "SimitNit", consultas: 10, conError: 1, obtenidos: 30, nuevos: 2 }],
      porDia: [{ dia: "2026-11-02", fuente: "SimitNit", consultas: 10, conError: 1, obtenidos: 30, nuevos: 2 }],
      erroresPorTipo: [{ fuente: "SimitNit", tipo: "Timeout", cantidad: 1 }],
      correos: { fallidos: 1, rebotados: 0, motivos: [{ motivo: "Buzón lleno", cantidad: 1 }], recientes: [] },
      empresasConMasErrores: [{ empresaId: "e1", empresa: "Gestora Uno", consultasConError: 1, correosFallidos: 1 }],
    };
    const hojas = hojasPlataforma(p);
    expect(hoja(hojas, "Por fuente").rows).toEqual([["SIMIT por NIT", 10, 1, 30, 2]]);
    expect(hoja(hojas, "Errores técnicos").rows).toEqual([["SIMIT por NIT", "Tiempo de espera agotado", 1]]);
    expect(hoja(hojas, "Por día y fuente").rows[0][0]).toEqual({ year: 2026, month: 11, day: 2 });
    expect(hojasPlataforma({ ...p, erroresPorTipo: null }).map((h) => h.name)).not.toContain("Errores técnicos");
  });

  it("Resumen: indicadores, series y, si cargaron, las gráficas de dinero con el estado legal en negocio", () => {
    const punto = (periodo: string) => ({ periodo, ingresados: 4, valorIngresado: 100, dpEnviados: 2, dpFavorables: 1, ahorro: 50, pagosCc: 0, pagosPc: 1, pagosBc: 0 });
    const d: Dashboard = {
      alcance: { empresa: null, nit: null, desde: "2026-11-01", hasta: "2026-11-24" },
      kpis: { deudaActivaTotal: 1_604_100, comparendosConDeuda: 2, dpFavorables: 1, dpDesfavorables: 1, tasaExitoDp: 0.5, dpSinRespuesta: 0, ahorroAcumulado: 604_100, radarCantidad: 1, radarValor: 1_000_000, radarHasta: "2026-12-03" },
      mensual: [punto("2026-11")],
      anual: { actual: Array.from({ length: 12 }, (_, i) => (i === 10 ? punto("2026-11") : null)), anterior: Array(12).fill(null) },
      porNit: [{ nitId: "n1", nit: "900123456-7", titular: "TITULAR", total: 4, activos: 3, deudaActiva: 1_604_100, conDp: 2, pagados: 1 }],
    };
    const sinDinero = hojasResumen(d, null);
    expect(sinDinero.map((h) => h.name)).toEqual(["Indicadores", "Últimos 12 meses", "Año contra año", "Totales por NIT"]);
    expect(valor(hoja(sinDinero, "Indicadores"), "Deuda sin pagar (a hoy)")).toBe(1_604_100);
    expect(valor(hoja(sinDinero, "Indicadores"), "Tasa de éxito de DP (%)")).toBe(50);
    expect(valor(hoja(sinDinero, "Indicadores"), "Radar de vencimientos: vencen antes del")).toEqual({ year: 2026, month: 12, day: 3 });
    expect(hoja(sinDinero, "Año contra año").rows).toHaveLength(24);
    const dinero = { estados: [{ estado: "Caducado", cantidad: 2, valor: 900_000 }], mensual: [{ periodo: "2026-11", valorIngresado: 10, pagado: 5 }], organismos: [{ organismoId: "o1", nombre: "Movilidad", cantidad: 3, valor: 300, exigible: 200 }] } as unknown as Dinero;
    const conDinero = hojasResumen(d, dinero);
    expect(hoja(conDinero, "Deuda por estado legal").rows[0]).toEqual([ESTADO_LEGAL.Caducado.texto, 2, 900_000]);
    expect(hoja(conDinero, "Deuda por organismo").rows[0]).toEqual(["Movilidad", 3, 200, 100]);
  });

  it("Notas y nombre del archivo: el corte, la empresa, el NIT, la hora de Colombia y las aclaraciones", () => {
    const corte: CorteExcel = { pestana: "gestion", titulo: "Reportes › Gestión", empresa: "e1", nit: null, desde: null, hasta: null, corte: "Pendiente a hoy", aclaraciones: ["La deuda incluye lo caducado y lo prescrito."] };
    // 03:30 UTC del 25 = 22:30 del 24 en Colombia.
    const notas = hojaNotas(corte, { empresa: "Gestora Uno S.A.S.", nit: "Todos los NIT" }, new Date("2026-11-25T03:30:00Z"));
    expect(notas.name).toBe("Notas");
    expect(valor(notas, "Corte")).toBe("Pendiente a hoy");
    expect(valor(notas, "Empresa")).toBe("Gestora Uno S.A.S.");
    expect(valor(notas, "Desde")).toBe("Sin periodo");
    expect(valor(notas, "Generado (hora de Colombia)")).toBe("24/11/2026 22:30");
    expect(valor(notas, "Aclaraciones")).toBe("La deuda incluye lo caducado y lo prescrito.");
    expect(nombreArchivo(corte, "Gestora Uno S.A.S.")).toBe("reporte-gestion-gestora-uno-s-a-s-sin-periodo_hoy.xlsx");
    expect(nombreArchivo({ ...corte, pestana: "dp", desde: "2026-11-01", hasta: "2026-11-24" }, null)).toBe("reporte-dp-todas-2026-11-01_2026-11-24.xlsx");
    expect(dia("2026-11-01")).toEqual({ year: 2026, month: 11, day: 1 });
    expect(porcentaje(null)).toBeNull();
  });

  it("el libro lleva todas las hojas con nombres únicos", () => {
    const h = (name: string): XlsxSheet => ({ name, columns: [{ header: "A" }], rows: [[1]] });
    const bytes = buildXlsxLibro([h("Datos"), h("Datos"), h("Notas")]);
    const xml = new TextDecoder().decode(bytes);
    expect(xml).toContain('<sheet name="Datos" sheetId="1" r:id="rId1"/>');
    expect(xml).toContain('<sheet name="Datos 2" sheetId="2" r:id="rId2"/>');
    expect(xml).toContain("xl/worksheets/sheet3.xml");
  });
});
