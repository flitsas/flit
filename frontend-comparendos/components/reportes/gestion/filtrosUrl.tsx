"use client";

import { usePathname, useRouter, useSearchParams } from "next/navigation";
import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";
import { FiltrosPeriodo, periodoInicial, type Periodo } from "@/components/reportes/FiltrosPeriodo";
import { ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { P } from "@/lib/nav";
import { errorRango } from "@/lib/reportes";
import { tienePermiso } from "@/lib/sesion";
import type { Nit } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

/**
 * Filtros de las pestañas de análisis (Reportes 2.0, §8b del contrato): viven en la URL con los nombres exactos
 * `desde`, `hasta`, `nit` y `empresa`, para que viajen entre pestañas (`hrefPestana`) y desde el Inicio.
 */
export interface FiltrosUrl {
  desde: string | null;
  hasta: string | null;
  nit: string | null;
  /** Solo el SuperAdmin: para la gestora siempre null (la API usa su empresa). */
  empresa: string | null;
}

export function useFiltrosUrl(): { filtros: FiltrosUrl; escribir: (cambio: Partial<FiltrosUrl>) => void; errorUrl: string | null } {
  const params = useSearchParams();
  const router = useRouter();
  const ruta = usePathname();
  const { sesion } = useSesion();
  const esSuperAdmin = !!sesion?.usuario.esSuperAdmin;
  const filtros: FiltrosUrl = {
    desde: params.get("desde"),
    hasta: params.get("hasta"),
    nit: params.get("nit"),
    empresa: esSuperAdmin ? params.get("empresa") : null,
  };
  // R8 (lo de V1 que no se copia): un rango invertido escrito a mano en la URL no se consulta.
  const errorUrl = filtros.desde && filtros.hasta ? errorRango(filtros.desde, filtros.hasta) : null;
  const escribir = (cambio: Partial<FiltrosUrl>) => {
    const q = new URLSearchParams(params.toString());
    for (const [k, v] of Object.entries(cambio)) {
      if (v) q.set(k, v);
      else q.delete(k);
    }
    const s = q.toString();
    router.replace(s ? `${ruta}?${s}` : ruta, { scroll: false });
  };
  return { filtros, escribir, errorUrl };
}

/** Selector de NIT de la empresa (el mismo del Resumen); solo con una empresa conocida y el permiso de monitoreo. */
export function SelectorNit({ empresa, valor, onCambio }: { empresa: string | null; valor: string | null; onCambio: (id: string | null) => void }) {
  const nits = useCarga(`nits-reporte:${empresa}`, (signal) => apiFetch<Nit[]>("/monitoreo/nits", { signal, query: { empresa } }));
  return (
    <label className={ETIQUETA_CLS}>
      NIT
      <select className={`${INPUT_CLS} w-full sm:w-60`} value={valor ?? ""} onChange={(e) => onCambio(e.target.value || null)} disabled={nits.estado !== "ready"}>
        <option value="">Todos los NIT</option>
        {(nits.datos ?? []).map((n) => (
          <option key={n.id} value={n.id}>
            {n.numero} {n.nombreTitular ? `· ${n.nombreTitular}` : ""}
          </option>
        ))}
      </select>
    </label>
  );
}

/**
 * Barra de filtros de una pestaña: periodo (y empresa del SuperAdmin) con «Consultar», y el NIT al lado. Todo se escribe en
 * la URL. `conNit` en false para Plataforma, que no corta por NIT.
 */
export function BarraFiltros({ filtros, escribir, conNit = true, errorUrl }: { filtros: FiltrosUrl; escribir: (c: Partial<FiltrosUrl>) => void; conNit?: boolean; errorUrl: string | null }) {
  const { sesion } = useSesion();
  const inicial = periodoInicial();
  const valor: Periodo = { desde: filtros.desde ?? inicial.desde, hasta: filtros.hasta ?? inicial.hasta, empresa: filtros.empresa };
  const empresaNits = sesion?.usuario.esSuperAdmin ? filtros.empresa : (sesion?.empresa?.id ?? null);
  const puedeVerNits = conNit && !!empresaNits && tienePermiso(sesion, P.monitoreoVer);
  return (
    <div className="flex flex-col gap-2">
      <FiltrosPeriodo
        // La URL manda: si cambia por «atrás» o por otra pestaña, el borrador vuelve a leerla.
        key={`${valor.desde}|${valor.hasta}|${valor.empresa ?? ""}`}
        valor={valor}
        onAplicar={(p) => escribir({ desde: p.desde, hasta: p.hasta, empresa: p.empresa, ...(p.empresa !== filtros.empresa ? { nit: null } : {}) })}
        extra={puedeVerNits ? <SelectorNit key={empresaNits} empresa={empresaNits} valor={filtros.nit} onCambio={(nit) => escribir({ nit })} /> : undefined}
      />
      {errorUrl && (
        <InlineAlert tone="warning" compact>
          {errorUrl} Corrige las fechas y pulsa «Consultar».
        </InlineAlert>
      )}
    </div>
  );
}

/** Periodo efectivo de las pestañas con periodo obligatorio (DP, Plataforma): el de la URL o, sin él, el mes en curso. */
export function periodoEfectivo(f: FiltrosUrl): { desde: string; hasta: string } {
  const inicial = periodoInicial();
  return { desde: f.desde ?? inicial.desde, hasta: f.hasta ?? inicial.hasta };
}
