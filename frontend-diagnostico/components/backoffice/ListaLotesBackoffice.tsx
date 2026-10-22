"use client";

import { useEffect, useId, useState } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { Building2, ClipboardList } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import {
  BOTON_REINTENTAR_CLS,
  CAMPO_CLS,
  ETIQUETA_CLS,
  MARCA_CLS,
  OPCION_CLS,
  TARJETA_CLS,
} from "@/components/configuracion/estilos";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import { ESTADOS_LOTE } from "@/components/tablero/EstadoGlobal";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  Etiqueta,
  FILA_TABLA_CLS,
} from "@/components/tablero/vistasDetalle/comun";
import { ApiError } from "@/lib/api";
import { listarLotesBackoffice, type ListaDiagnosticosBackoffice } from "@/lib/backoffice/api";
import { esEmpresaValida } from "@/lib/configuracion";
import type { EstadoDiagnostico } from "@/lib/diagnosticos/api";
import { SelectorEmpresa } from "./SelectorEmpresa";

export interface ListaLotesBackofficeProps {
  /** Valores iniciales tomados de la URL (?empresa=&estado=&con_errores=&pagina=). */
  empresa: string;
  estado: EstadoDiagnostico | null;
  conErrores: boolean;
  pagina: number;
}

type Carga = { tipo: "cargando" } | { tipo: "error"; error: unknown } | { tipo: "listo"; lista: ListaDiagnosticosBackoffice };

const ESTADOS: EstadoDiagnostico[] = ["CREADO", "EN_PROCESO", "COMPLETADO", "INCOMPLETO"];
const TAMANOS = [25, 50, 100, 200] as const;
const TAMANO_INICIAL = 50;

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

function Vacio({ titulo, children, Icono = ClipboardList }: { titulo: string; children?: React.ReactNode; Icono?: typeof ClipboardList }) {
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
 * explícito de la compañía, filtros por estado y «con errores» y la tabla de lotes (orden de creación, el más reciente
 * primero) con enlace a la grilla de cada uno. Sin compañía elegida no consulta. Los filtros viven en la URL. Cuatro estados:
 * cargando, error con Reintentar (403 a la página 403), vacío (sin compañía o sin lotes con esos filtros) y lleno.
 */
export function ListaLotesBackoffice(props: ListaLotesBackofficeProps) {
  const router = useRouter();
  const id = useId();
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

  let contenido: React.ReactNode;
  if (!hayEmpresa) {
    contenido = (
      <Vacio titulo="Elige una compañía" Icono={Building2}>
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Indica la compañía cuyos lotes de diagnóstico quieres revisar.</p>
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
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">Cambia el estado o quita «Solo lotes con errores».</p>
      </Vacio>
    );
  } else {
    const { lista } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
          <table aria-label="Lotes de la compañía" className="w-full min-w-[640px] border-collapse text-sm">
            <thead>
              <tr>
                {["Lote", "Estado", "Creado", "Vehículos", "Celdas en error"].map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {lista.diagnosticos.map((d) => {
                const estado = ESTADOS_LOTE[d.estado] ?? { texto: d.estado, tono: "neutral" as const };
                return (
                  <tr key={d.id} className={`${FILA_TABLA_CLS} bg-white dark:bg-transparent`}>
                    <td className={CELDA_TABLA_CLS}>
                      <Link
                        href={`/backoffice/diagnosticos/${encodeURIComponent(d.id)}?empresa=${encodeURIComponent(filtros.empresa)}`}
                        className="text-sm font-semibold text-[var(--flit-brand-ink)] underline-offset-4 hover:underline focus-visible:outline-2 focus-visible:outline-offset-2 focus-visible:outline-[var(--nav-focus)] dark:text-white"
                      >
                        {d.nombre}
                      </Link>
                    </td>
                    <td className={CELDA_TABLA_CLS}>
                      <Etiqueta tono={estado.tono}>{estado.texto}</Etiqueta>
                    </td>
                    <td className={`${CELDA_TABLA_CLS} tabular-nums`}>{fechaDelDato(d.creado_en)}</td>
                    <td className={`${CELDA_TABLA_CLS} tabular-nums`}>{d.vehiculos}</td>
                    <td className={CELDA_TABLA_CLS}>
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
          pageSizeOptions={TAMANOS}
          ariaLabel="Paginación de lotes"
          noun="lotes"
        />
      </div>
    );
  }

  return (
    <>
      <section aria-labelledby="lotes-bo-titulo" className={TARJETA_CLS}>
        <h1 id="lotes-bo-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
          Backoffice de diagnósticos
        </h1>
        <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">
          Revisa los lotes de una compañía cliente, abre su grilla y llena a mano las celdas en error.
        </p>
      </section>
      <section aria-label="Compañía y filtros" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        <SelectorEmpresa valor={filtros.empresa} onElegir={(empresa) => aplicar({ empresa, pagina: 1 })} />
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
      <section aria-label="Lotes" className={TARJETA_CLS}>
        {contenido}
      </section>
    </>
  );
}
