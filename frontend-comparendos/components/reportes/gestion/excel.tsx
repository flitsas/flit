"use client";

import { useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { useSesion } from "@/components/ComparendosShell";
import { useToast } from "@/components/Toast";
import { controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import type { EmpresaResumen, Nit } from "@/lib/tipos";
import { bogotaClock, buildXlsxLibro, XLSX_MIME, type XlsxCell, type XlsxDate, type XlsxSheet } from "@/lib/xlsx";

/**
 * «Descargar Excel» de las pestañas de Reportes 2.0. El libro se arma EN EL NAVEGADOR con los datos ya cargados (sin
 * endpoints nuevos): así las cifras del archivo son las de la pantalla. Una hoja por tabla o gráfico y una hoja «Notas»
 * con el corte, para que quien reciba el archivo por correo sepa qué está mirando.
 */

/** Lo que dice la hoja «Notas» y el nombre del archivo. */
export interface CorteExcel {
  /** gestion, dp, resumen, plataforma: va en el nombre del archivo. */
  pestana: string;
  /** Nombre visible de la pestaña. */
  titulo: string;
  empresa: string | null;
  nit: string | null;
  /** null = sin periodo («Pendiente a hoy»). */
  desde: string | null;
  hasta: string | null;
  /** Cómo se describe el corte en pantalla («Pendiente a hoy», «Comparendos impuestos del … al …»). */
  corte: string;
  /** Las aclaraciones de la pantalla (p. ej. que la deuda incluye lo caducado y lo prescrito). */
  aclaraciones: string[];
}

/**
 * Una fecha «yyyy-MM-dd» de la API como fecha de Excel, SIN pasar por `bogotaDay`: esa función lee la cadena como un
 * instante UTC y la corre al día anterior en Colombia.
 */
export function dia(valor: string | null | undefined): XlsxDate | null {
  const m = valor ? /^(\d{4})-(\d{2})-(\d{2})$/.exec(valor) : null;
  return m ? { year: Number(m[1]), month: Number(m[2]), day: Number(m[3]) } : null;
}

/** Tasa 0–1 como porcentaje con un decimal (la columna se llama «… (%)»); sin respuestas, vacía, no 0. */
export const porcentaje = (t: number | null | undefined): XlsxCell => (t === null || t === undefined ? null : Math.round(t * 1000) / 10);

/** «24/11/2026» para el texto de las notas. */
export function diaTexto(valor: string): string {
  const d = dia(valor);
  return d ? `${String(d.day).padStart(2, "0")}/${String(d.month).padStart(2, "0")}/${d.year}` : valor;
}

export function hojaNotas(c: CorteExcel, nombres: { empresa: string; nit: string }, generado: Date): XlsxSheet {
  const g = bogotaClock(generado.toISOString())!;
  const dos = (n: number) => String(n).padStart(2, "0");
  return {
    name: "Notas",
    columns: [
      { header: "Dato", width: 24 },
      { header: "Valor", width: 90 },
    ],
    rows: [
      ["Reporte", c.titulo],
      ["Empresa", nombres.empresa],
      ["NIT", nombres.nit],
      ["Corte", c.corte],
      ["Desde", c.desde ? diaTexto(c.desde) : "Sin periodo"],
      ["Hasta", c.hasta ? diaTexto(c.hasta) : "Hoy"],
      ["Generado (hora de Colombia)", `${dos(g.day)}/${dos(g.month)}/${g.year} ${dos(g.hour ?? 0)}:${dos(g.minute ?? 0)}`],
      ...c.aclaraciones.map((a, i): XlsxCell[] => [i === 0 ? "Aclaraciones" : "", a]),
    ],
  };
}

/** Solo letras, números y guiones: el nombre viaja por correo y por sistemas de archivos distintos. */
function slug(texto: string): string {
  return (
    texto
      .normalize("NFD")
      .replace(/[̀-ͯ]/g, "")
      .toLowerCase()
      .replace(/[^a-z0-9]+/g, "-")
      .replace(/^-+|-+$/g, "")
      .slice(0, 40) || "empresa"
  );
}

/** `reporte-<pestaña>-<empresa o todas>-<desde>_<hasta o hoy>.xlsx`. */
export function nombreArchivo(c: CorteExcel, empresa: string | null): string {
  return `reporte-${c.pestana}-${empresa ? slug(empresa) : "todas"}-${c.desde ?? "sin-periodo"}_${c.hasta ?? "hoy"}.xlsx`;
}

/** Nombres de negocio de la empresa y del NIT elegidos (no sus identificadores). */
async function nombresDe(c: CorteExcel, empresaPropia: string | null): Promise<{ empresa: string; nit: string; empresaArchivo: string | null }> {
  let empresa = empresaPropia;
  if (c.empresa) {
    const empresas = await apiFetch<EmpresaResumen[]>("/admin/empresas").catch(() => [] as EmpresaResumen[]);
    empresa = empresas.find((e) => e.id === c.empresa)?.nombre ?? "Empresa elegida";
  }
  let nit = "Todos los NIT";
  if (c.nit) {
    const nits = await apiFetch<Nit[]>("/monitoreo/nits", { query: { empresa: c.empresa } }).catch(() => [] as Nit[]);
    const n = nits.find((x) => x.id === c.nit);
    nit = n ? `${n.numero}${n.nombreTitular ? ` · ${n.nombreTitular}` : ""}` : "NIT elegido";
  }
  return { empresa: empresa ?? "Todas las empresas", nit, empresaArchivo: empresa };
}

function descargar(bytes: Uint8Array<ArrayBuffer>, nombre: string) {
  const url = URL.createObjectURL(new Blob([bytes], { type: XLSX_MIME }));
  const a = document.createElement("a");
  a.href = url;
  a.download = nombre;
  document.body.appendChild(a);
  a.click();
  a.remove();
  setTimeout(() => URL.revokeObjectURL(url), 0);
}

/**
 * Botón «Descargar Excel» (MAIA: la ubicación de `ExportButtons` de Trámites, a la derecha de la fila de filtros; el
 * estilo del «Exportar a Excel» de Productividad). Deshabilitado mientras carga, sin datos o con un rango inválido.
 */
export function BotonExcel({
  corte,
  hojas,
  deshabilitado,
}: {
  corte: CorteExcel;
  /** Las hojas de datos; null mientras no hay datos cargados. */
  hojas: (() => XlsxSheet[]) | null;
  deshabilitado: boolean;
}) {
  const toast = useToast();
  const { sesion } = useSesion();
  // La gestora siempre ve su empresa; el SuperAdmin, la que eligió en el filtro o todas (nunca la de su propia sesión).
  const empresaPropia = sesion?.usuario.esSuperAdmin ? null : (sesion?.empresa?.nombre ?? null);
  const [ocupado, setOcupado] = useState(false);
  const generar = async () => {
    if (!hojas) return;
    setOcupado(true);
    try {
      const n = await nombresDe(corte, empresaPropia);
      const libro = buildXlsxLibro([...hojas(), hojaNotas(corte, n, new Date())]);
      descargar(libro, nombreArchivo(corte, n.empresaArchivo));
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo generar el Excel.", "error");
    } finally {
      setOcupado(false);
    }
  };
  return (
    <button type="button" className={controlCls(false)} onClick={generar} disabled={deshabilitado || !hojas || ocupado} aria-busy={ocupado} data-testid="descargar-excel">
      <Icono nombre={ocupado ? "girador" : "nube-bajar"} className={`h-3.5 w-3.5 ${ocupado ? "animate-spin" : ""}`} />
      {ocupado ? "Generando…" : "Descargar Excel"}
    </button>
  );
}
