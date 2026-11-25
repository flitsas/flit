"use client";

import Link from "next/link";
import { useSelectedLayoutSegment, useSearchParams } from "next/navigation";
import { PESTANAS_LISTA_CLS, pestanaClass } from "@flit/ui/Pestanas";
import { useSesion } from "@/components/ComparendosShell";
import { P } from "@/lib/nav";
import { tienePermiso, type SesionComparendos } from "@/lib/sesion";

/**
 * Reportes 2.0 — un solo menú «Reportes» y todo dentro, en pestañas que son rutas (cada una conserva su enlace).
 * Productividad dejó de ser una entrada aparte del dock. Una pestaña sin su permiso no aparece: mostrarla para
 * responder «no tienes acceso» es ofrecer algo que no se puede usar.
 */
export interface PestanaReporte {
  id: string;
  label: string;
  /** Segmento bajo /reportes; null = la raíz (Resumen). */
  segmento: string | null;
}

const TODAS: (PestanaReporte & { visible: (s: SesionComparendos | null) => boolean })[] = [
  { id: "resumen", label: "Resumen", segmento: null, visible: (s) => tienePermiso(s, P.reportesVer) },
  { id: "gestion", label: "Gestión", segmento: "gestion", visible: (s) => tienePermiso(s, P.reportesVer) },
  { id: "dp", label: "Derechos de petición", segmento: "dp", visible: (s) => tienePermiso(s, P.reportesVer) },
  { id: "productividad", label: "Productividad", segmento: "productividad", visible: (s) => tienePermiso(s, P.productividadVer) },
  { id: "consultas", label: "Consultas", segmento: "consultas", visible: (s) => tienePermiso(s, P.reportesConsultas) },
  { id: "plataforma", label: "Plataforma", segmento: "plataforma", visible: (s) => !!s?.usuario.esSuperAdmin },
];

export function pestanasVisibles(sesion: SesionComparendos | null): PestanaReporte[] {
  return TODAS.filter((p) => p.visible(sesion)).map(({ id, label, segmento }) => ({ id, label, segmento }));
}

/** Lo que viaja de una pestaña a otra: el mismo periodo, NIT y empresa se siguen leyendo igual. */
const PARAMS_COMPARTIDOS = ["desde", "hasta", "nit", "empresa"] as const;

export function hrefPestana(segmento: string | null, actuales: URLSearchParams): string {
  const q = new URLSearchParams();
  for (const k of PARAMS_COMPARTIDOS) {
    const v = actuales.get(k);
    if (v) q.set(k, v);
  }
  const base = segmento ? `/reportes/${segmento}` : "/reportes";
  const s = q.toString();
  return s ? `${base}?${s}` : base;
}

export function PestanasReportes() {
  const { sesion, cargando } = useSesion();
  const segmento = useSelectedLayoutSegment();
  const params = useSearchParams();
  if (cargando) return null;
  const pestanas = pestanasVisibles(sesion);
  if (pestanas.length <= 1) return null;
  return (
    <nav aria-label="Secciones de reportes" data-testid="cmp-rep-pestanas" className={PESTANAS_LISTA_CLS}>
      {pestanas.map((p) => {
        const activa = (segmento ?? null) === p.segmento;
        return (
          <Link
            key={p.id}
            href={hrefPestana(p.segmento, new URLSearchParams(params.toString()))}
            aria-current={activa ? "page" : undefined}
            className={pestanaClass(activa)}
            data-testid={`cmp-rep-pestana-${p.id}`}
          >
            {p.label}
          </Link>
        );
      })}
    </nav>
  );
}
