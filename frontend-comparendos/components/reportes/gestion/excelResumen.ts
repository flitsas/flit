import { ESTADO_LEGAL } from "@/lib/etiquetas";
import { etiquetaMes, nombreMes, type Dashboard, type Dinero, type PuntoMensual } from "@/lib/reportes";
import type { XlsxSheet } from "@/lib/xlsx";
import { dia, porcentaje } from "./excel";

const mensual = (p: PuntoMensual | null | undefined) =>
  p ? [p.ingresados, p.valorIngresado, p.dpEnviados, p.dpFavorables, p.ahorro, p.pagosCc, p.pagosPc, p.pagosBc] : [null, null, null, null, null, null, null, null];
const COLUMNAS_MES = ["Ingresados", "Valor ingresado", "DP enviados", "DP favorables", "Ahorro", "Pagos CC", "Pagos PC", "Pagos BC"];

/** Hojas del Resumen: los mismos indicadores, series y tablas de la pantalla (y las gráficas de dinero si cargaron). */
export function hojasResumen(d: Dashboard, dinero: Dinero | null): XlsxSheet[] {
  const k = d.kpis;
  const hojas: XlsxSheet[] = [
    {
      name: "Indicadores",
      columns: [{ header: "Indicador", width: 46 }, { header: "Valor", width: 18 }],
      rows: [
        ["Deuda sin pagar (a hoy)", k.deudaActivaTotal],
        ["Comparendos con deuda (a hoy)", k.comparendosConDeuda],
        ["Tasa de éxito de DP (%)", porcentaje(k.tasaExitoDp)],
        ["DP favorables (periodo)", k.dpFavorables],
        ["DP desfavorables (periodo)", k.dpDesfavorables],
        ["DP esperando respuesta (a hoy)", k.dpSinRespuesta],
        ["Ahorro acumulado (periodo)", k.ahorroAcumulado],
        ["Radar de vencimientos: comparendos", k.radarCantidad],
        ["Radar de vencimientos: valor", k.radarValor],
        ["Radar de vencimientos: vencen antes del", dia(k.radarHasta)],
      ],
    },
    {
      name: "Últimos 12 meses",
      columns: [{ header: "Mes", width: 12 }, ...COLUMNAS_MES.map((h) => ({ header: h, width: 16 }))],
      rows: d.mensual.map((p) => [etiquetaMes(p.periodo), ...mensual(p)]),
    },
    {
      name: "Año contra año",
      columns: [{ header: "Mes", width: 10 }, { header: "Año", width: 8 }, ...COLUMNAS_MES.map((h) => ({ header: h, width: 16 }))],
      rows: Array.from({ length: 12 }, (_, i) => [
        [nombreMes(i), "Actual", ...mensual(d.anual.actual[i])],
        [nombreMes(i), "Anterior", ...mensual(d.anual.anterior[i])],
      ]).flat(),
    },
    {
      name: "Totales por NIT",
      columns: [
        { header: "NIT", width: 16 },
        { header: "Titular", width: 40 },
        { header: "Comparendos", width: 14 },
        { header: "Activos", width: 12 },
        { header: "Deuda sin pagar", width: 18 },
        { header: "Con DP", width: 10 },
        { header: "Pagados", width: 10 },
      ],
      rows: d.porNit.map((n) => [n.nit, n.titular, n.total, n.activos, n.deudaActiva, n.conDp, n.pagados]),
    },
  ];
  if (dinero)
    hojas.push(
      {
        name: "Deuda por estado legal",
        columns: [{ header: "Estado legal", width: 24 }, { header: "Comparendos", width: 14 }, { header: "Valor", width: 18 }],
        rows: dinero.estados.map((e) => [ESTADO_LEGAL[e.estado]?.texto ?? "Otro", e.cantidad, e.valor]),
      },
      {
        name: "Ingresado y pagado",
        columns: [{ header: "Mes", width: 12 }, { header: "Valor de lo que ingresó", width: 22 }, { header: "Pagado", width: 18 }],
        rows: dinero.mensual.map((m) => [etiquetaMes(m.periodo), m.valorIngresado, m.pagado]),
      },
      {
        name: "Deuda por organismo",
        columns: [{ header: "Organismo", width: 44 }, { header: "Comparendos", width: 14 }, { header: "Se puede cobrar", width: 18 }, { header: "Caducado o prescrito", width: 20 }],
        rows: dinero.organismos.map((o) => [o.nombre, o.cantidad, o.exigible, o.valor - o.exigible]),
      },
    );
  return hojas;
}
