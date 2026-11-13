"use client";

import { useEffect, useId, useState, type ComponentType } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Icono } from "@flit/ui/Icono";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { TAMANO_DE_PAGINA_POR_DEFECTO } from "@flit/ui/usePaginacion";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import {
  BOTON_REINTENTAR_CLS,
  CAMPO_CLS,
  ETIQUETA_CLS,
  MARCA_CLS,
  OPCION_CLS,
  TARJETA_CLS,
} from "@/components/configuracion/estilos";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import { ESTADOS_LOTE, vistaEstadoLote } from "@/components/tablero/EstadoGlobal";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  Etiqueta,
  FILA_TABLA_CLS,
} from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import { listarLotesBackoffice, type DiagnosticoBackoffice, type ListaDiagnosticosBackoffice } from "@/lib/backoffice/api";
import { esEmpresaValida } from "@/lib/configuracion";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { SelectorEmpresaBackoffice } from "./SelectorEmpresaBackoffice";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";

export interface ListaLotesBackofficeProps {
  /** Valores iniciales tomados de la URL (?empresa=&estado=&con_errores=&pagina=). */
  empresa: string;
  estado: EstadoDiagnostico | null;
  conErrores: boolean;
  pagina: number;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; lista: ListaDiagnosticosBackoffice };

const ESTADOS: EstadoDiagnostico[] = ["CREADO", "EN_PROCESO", "COMPLETADO", "INCOMPLETO"];
/** El estándar de las tablas FLIT (HU #13916). Solo cambia desde el selector, que ofrece 10/20/30/50; no viaja si es este. */
const TAMANO_INICIAL = TAMANO_DE_PAGINA_POR_DEFECTO;
/** Celda de la tabla de lotes; por debajo de 900 px, renglón de la tarjeta del lote con su `data-label` (HU #14562 AC56). */
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;

interface Filtros {
  empresa: string;
  estado: EstadoDiagnostico | null;
  conErrores: boolean;
  pagina: number;
  tamano: number;
}

function consultaUrl(f: Filtros): string {
  const q = new URLSearchParams({ empresa: f.empresa });
  if (f.estado) q.set("estado", f.estado);
  if (f.conErrores) q.set("con_errores", "true");
  if (f.pagina > 1) q.set("pagina", String(f.pagina));
  return q.toString();
}

/** Banda de contexto del backoffice (AC43, maqueta BackofficeLotes): avisa que se opera como FLIT. */
function BandaBackoffice() {
  return (
    <p
      role="note"
      aria-label="Contexto del backoffice"
      className="inline-flex items-center gap-2 rounded-full border border-[color:var(--di-border)] bg-[var(--di-surface-sunken)] px-3 py-1 text-xs font-semibold text-[color:var(--di-text-secondary)]"
    >
      <Icono nombre="escudo" className="h-3.5 w-3.5" />
      Backoffice FLIT · lo que haces aquí no lo ve la compañía como intervención manual
    </p>
  );
}

/** AC43 (B3): «ID · modo · trámite». Sin `modo` (API previo a B3) solo el ID; nada se infiere. */
export function contextoLote(d: DiagnosticoBackoffice): string {
  const partes = [d.id];
  if (d.modo) {
    partes.push(d.modo === "CON_ANALISIS" ? "Con análisis IA" : "Simple");
    if (d.tipo_tramite) partes.push(d.tipo_tramite.nombre);
    else if (d.es_express) partes.push("Express");
  }
  return partes.join(" · ");
}

/** Ícono del estado vacío: del kit (`iconoComo`) o de lucide cuando el kit no trae el glifo (SearchX). */
type IconoVacio = ComponentType<{ className?: string; strokeWidth?: number; "aria-hidden"?: boolean | "true" | "false" }>;
const ICONO_VACIO = iconoComo("portapapeles");

function Vacio({ titulo, children, Icono = ICONO_VACIO }: { titulo: string; children?: React.ReactNode; Icono?: IconoVacio }) {
  return (
    <div className="flex flex-col items-center gap-3 py-10 text-center">
      <Icono className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
      <h2 className="text-lg font-semibold">{titulo}</h2>
      {children}
    </div>
  );
}

/**
 * Lotes de una compañía para el backoffice FLIT (HU #13802, F06-H5; F06-C6, C14) en /backoffice/diagnosticos: selector
 * de la compañía con búsqueda por nombre o NIT (SelectorEmpresaBackoffice, el mismo del editor de contrato; Punto 6 de la
 * ola 2026-11-11), filtros por estado y «con errores» y la tabla de lotes (orden de creación, el más reciente primero) con
 * enlace a la grilla de cada uno. Sin compañía elegida no consulta. Los filtros viven en la URL. Cuatro estados:
 * cargando, error con Reintentar (403 a la página 403), vacío (sin compañía o sin lotes con esos filtros) y lleno.
 */
export function ListaLotesBackoffice(props: ListaLotesBackofficeProps) {
  const router = useRouter();
  const id = useId();
  const [nombreEmpresa, setNombreEmpresa] = useState<string | null>(null);
  const [filtros, setFiltros] = useState<Filtros>({
    empresa: esEmpresaValida(props.empresa) ? props.empresa : "",
    estado: props.estado,
    conErrores: props.conErrores,
    pagina: Math.max(1, props.pagina || 1),
    tamano: TAMANO_INICIAL,
  });
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const hayEmpresa = filtros.empresa !== "";

  useEffect(() => {
    if (!hayEmpresa) return;
    const control = new AbortController();
    listarLotesBackoffice(filtros.empresa, { estado: filtros.estado, conErrores: filtros.conErrores, pagina: filtros.pagina, tamano: filtros.tamano === TAMANO_INICIAL ? undefined : filtros.tamano }, control.signal).then(
      (lista) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", lista });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error });
      },
    );
    return () => control.abort();
  }, [hayEmpresa, filtros, intento]);

  const es403 = carga.tipo === "error" && carga.error instanceof ApiError && carga.error.status === 403;
  useEffect(() => {
    if (es403) router.replace("/403?code=SIN_PERMISO_BACKOFFICE");
  }, [es403, router]);

  function aplicar(cambios: Partial<Filtros>) {
    const siguiente = { ...filtros, ...cambios };
    setFiltros(siguiente);
    setCarga({ tipo: "cargando" });
    if (siguiente.empresa) router.replace(`/backoffice/diagnosticos?${consultaUrl(siguiente)}`, { scroll: false });
  }

  // AC43: «{compañía} · N lotes». El nombre solo se conoce si se eligió en el selector; con la compañía solo en la URL
  // se muestra el total, sin inventar el nombre.
  const total = carga.tipo === "listo" ? carga.lista.total : null;
  const lotesTexto = total === null ? null : total === 1 ? "1 lote" : `${total} lotes`;
  const resumen = hayEmpresa && lotesTexto ? (nombreEmpresa ? `${nombreEmpresa} · ${lotesTexto}` : lotesTexto) : null;

  let contenido: React.ReactNode;
  if (!hayEmpresa) {
    contenido = (
      <Vacio titulo="Elige una compañía" Icono={iconoComo("empresas")}>
        <p className="max-w-md text-sm text-muted-foreground">Indica la compañía cuyos lotes de diagnóstico quieres revisar.</p>
      </Vacio>
    );
  } else if (carga.tipo === "cargando" || es403) {
    contenido = <VehicleLoader label="Cargando los lotes…" />;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar los lotes"
        action={
          <button
            type="button"
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
            className={BOTON_REINTENTAR_CLS}
          >
            Reintentar
          </button>
        }
      >
        {carga.error instanceof Error ? carga.error.message : "Intenta de nuevo en un momento."}
      </InlineAlert>
    );
  } else if (carga.lista.diagnosticos.length === 0) {
    contenido = (
      <Vacio titulo="Esta compañía no tiene lotes con estos filtros">
        <p className="max-w-md text-sm text-muted-foreground">Cambia el estado o quita «Solo lotes con errores».</p>
      </Vacio>
    );
  } else {
    const { lista } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <div className="overflow-x-auto rounded-xl border border-border">
          <table aria-label="Lotes de la compañía" className={`w-full min-w-[640px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {["Lote", "Estado", "Creado", "Vehículos", "Celdas en error"].map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {lista.diagnosticos.map((d) => {
                const estado = vistaEstadoLote(d.estado, d.estado_etiqueta);
                return (
                  <tr key={d.id} className={`${FILA_TABLA_CLS} bg-card ${TABLA_MOVIL_ROW_CLS}`}>
                    <td data-label="Lote" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS}`}>
                      <Link
                        href={`/backoffice/diagnosticos/${encodeURIComponent(d.id)}?empresa=${encodeURIComponent(filtros.empresa)}`}
                        className="text-sm font-semibold text-[var(--flit-brand-ink)] underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-ring"
                      >
                        {d.nombre}
                      </Link>
                      <p className="mt-0.5 break-all text-xs text-muted-foreground">{contextoLote(d)}</p>
                    </td>
                    <td data-label="Estado" className={CELDA_CLS}>
                      <Etiqueta tono={estado.tono}>{estado.texto}</Etiqueta>
                    </td>
                    <td data-label="Creado" className={`${CELDA_CLS} tabular-nums`}>
                      {fechaDelDato(d.creado_en)}
                    </td>
                    <td data-label="Vehículos" className={`${CELDA_CLS} tabular-nums`}>
                      {d.vehiculos}
                    </td>
                    <td data-label="Celdas en error" className={CELDA_CLS}>
                      {d.celdas_en_error > 0 ? <Etiqueta tono="danger">{`${d.celdas_en_error} con error`}</Etiqueta> : <Etiqueta tono="success">Sin errores</Etiqueta>}
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination
          page={lista.pagina}
          pageSize={lista.tamano}
          totalCount={lista.total}
          onPageChange={(pagina) => aplicar({ pagina })}
          onPageSizeChange={(tamano) => aplicar({ tamano, pagina: 1 })}
          ariaLabel="Paginación de lotes"
          noun="lotes"
        />
      </div>
    );
  }

  return (
    <>
      <CabeceraModulo
        as="section"
        aria-labelledby="lotes-bo-titulo"
        tituloId="lotes-bo-titulo"
        titulo="Lotes por compañía"
        descripcion="Revisa lotes con errores, reprocesa consultas y llena datos a mano"
        icono="diagnostico"
        encima={<BandaBackoffice />}
        acciones={
          <Link href="/backoffice/fuentes-obligatorias" className={BOTON_REINTENTAR_CLS}>
            Fuentes obligatorias
          </Link>
        }
      />
      <section aria-label="Compañía y filtros" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        <SelectorEmpresaBackoffice
          id={`${id}-empresa`}
          value={filtros.empresa}
          onChange={(empresa) => {
            setNombreEmpresa(empresa.nombre);
            aplicar({ empresa: empresa.id, pagina: 1 });
          }}
        />
        {hayEmpresa && (
          <div className="flex flex-wrap items-end gap-4">
            <div className="flex flex-col gap-1">
              <label htmlFor={`${id}-estado`} className={ETIQUETA_CLS}>
                Estado
              </label>
              <select
                id={`${id}-estado`}
                value={filtros.estado ?? ""}
                onChange={(e) => aplicar({ estado: (e.target.value || null) as EstadoDiagnostico | null, pagina: 1 })}
                className={`${CAMPO_CLS} w-56`}
              >
                <option value="">Todos</option>
                {ESTADOS.map((e) => (
                  <option key={e} value={e}>
                    {ESTADOS_LOTE[e].texto}
                  </option>
                ))}
              </select>
            </div>
            <label className={OPCION_CLS}>
              <input type="checkbox" checked={filtros.conErrores} onChange={(e) => aplicar({ conErrores: e.target.checked, pagina: 1 })} className={MARCA_CLS} />
              Solo lotes con errores
            </label>
          </div>
        )}
      </section>
      <section aria-labelledby={`${id}-lotes`} className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <div className="flex flex-wrap items-baseline justify-between gap-2">
          <h2 id={`${id}-lotes`} className="text-base font-semibold text-[color:var(--di-heading)]">
            Lotes
          </h2>
          {resumen && <p className="text-sm text-muted-foreground">{resumen}</p>}
        </div>
        {contenido}
      </section>
    </>
  );
}
