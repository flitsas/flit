// Estado de Reportes › Productividad en la URL (rediseño S1): periodo y la cifra abierta vuelven igual del detalle.
import type { Periodo } from "@/components/reportes/FiltrosPeriodo";
import type { LectorParams } from "@/lib/filtros";
import { METRICAS, type Metrica } from "@/lib/reportes";

export interface CifraAbierta {
  usuarioId: string;
  metrica: Metrica;
}

const FECHA = /^\d{4}-\d{2}-\d{2}$/;

/** Periodo y cifra abierta como parámetros. El periodo de fábrica (`porDefecto`, el mes en curso) no se escribe. */
export function productividadAParams(periodo: Periodo, cifra: CifraAbierta | null, porDefecto: Periodo): URLSearchParams {
  const p = new URLSearchParams();
  if (periodo.desde !== porDefecto.desde || periodo.hasta !== porDefecto.hasta) {
    p.set("desde", periodo.desde);
    p.set("hasta", periodo.hasta);
  }
  if (periodo.empresa) p.set("empresa", periodo.empresa);
  if (cifra) {
    p.set("usuario", cifra.usuarioId);
    p.set("metrica", cifra.metrica);
  }
  return p;
}

/** La inversa. Un periodo incompleto o mal escrito vuelve al de fábrica; una métrica desconocida no abre nada. */
export function productividadDeParams(p: LectorParams, porDefecto: Periodo): { periodo: Periodo; cifra: CifraAbierta | null } {
  const desde = p.get("desde") ?? "";
  const hasta = p.get("hasta") ?? "";
  const rango = FECHA.test(desde) && FECHA.test(hasta) && desde <= hasta;
  const periodo: Periodo = { desde: rango ? desde : porDefecto.desde, hasta: rango ? hasta : porDefecto.hasta, empresa: p.get("empresa") || null };
  const usuarioId = p.get("usuario");
  const metrica = METRICAS.find((m) => m.clave === p.get("metrica"))?.clave;
  return { periodo, cifra: usuarioId && metrica ? { usuarioId, metrica } : null };
}
