"use client";

import { useEffect, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter, useSearchParams } from "next/navigation";
import { ClipboardList, SearchX } from "lucide-react";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, FOCO_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { fechaDelDato } from "@/components/tablero/CeldaSemaforo";
import {
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  Etiqueta,
  FILA_TABLA_CLS,
} from "@/components/tablero/vistasDetalle/comun";
import {
  destinoDiagnostico,
  etiquetaEstado,
  filtrosDesdeUrl,
  hayFiltros,
  idCorto,
  listarDiagnosticos,
  nombreModo,
  TAMANOS_LISTADO,
  traducirErrorListado,
  urlListado,
  type ErrorListado,
  type FiltrosListado as Filtros,
  type ListaDiagnosticos,
} from "@/lib/diagnosticos/listado";
import { FiltrosListado } from "./FiltrosListado";

type Carga = { tipo: "cargando" } | { tipo: "error"; error: ErrorListado } | { tipo: "listo"; lista: ListaDiagnosticos };

const COLUMNAS = ["ID", "Nombre", "Creado", "Tipo", "Trámite", "Estado", "Creador"] as const;

const TEXTO_SECUNDARIO = "max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300";

function Vacio({ titulo, Icono, children }: { titulo: string; Icono: typeof ClipboardList; children?: ReactNode }) {
  return (
    <div className="flex flex-col items-center gap-3 py-10 text-center">
      <Icono className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
      <h3 className="text-lg font-semibold">{titulo}</h3>
      {children}
    </div>
  );
}

/**
 * Listado de diagnósticos de la compañía en el inicio (HU #13852, F08-H2; F08-C1/C2/C5/C6/C7), debajo de la captura
 * Express. Los filtros (fechas, tipo, estado y nombre) salen de la URL y cada cambio la reescribe y vuelve a la página 1.
 * Tabla con ID corto, nombre, fecha (hora Colombia), tipo, trámite, chip de estado («Sin lanzar» para CREADO) y creador
 * tal como llega («No disponible» incluido). Clic en la fila o Enter en su enlace abre el tablero, o la carga de vehículos
 * si el lote está sin lanzar. Cuatro estados: cargando, error (400 con campo junto a su filtro, 403 a la página de acceso
 * negado, el resto con Reintentar), vacío (compañía sin diagnósticos, distinto de filtros sin resultados) y lleno.
 */
export function ListadoDiagnosticos() {
  const router = useRouter();
  const params = useSearchParams();
  const clave = params.toString();
  const [claveVista, setClaveVista] = useState(clave);
  const [filtros, setFiltros] = useState<Filtros>(() => filtrosDesdeUrl(params));
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);

  // La URL cambió desde fuera (atrás/adelante, enlace compartido): los filtros la siguen.
  if (clave !== claveVista) {
    setClaveVista(clave);
    const deUrl = filtrosDesdeUrl(new URLSearchParams(clave));
    if (urlListado(deUrl) !== urlListado(filtros)) {
      setFiltros(deUrl);
      setCarga({ tipo: "cargando" });
    }
  }

  useEffect(() => {
    const control = new AbortController();
    listarDiagnosticos(filtros, control.signal).then(
      (lista) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", lista });
      },
      (error: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", error: traducirErrorListado(error) });
      },
    );
    return () => control.abort();
  }, [filtros, intento]);

  const acceso = carga.tipo === "error" && carga.error.tipo === "ACCESO" ? carga.error.code : null;
  useEffect(() => {
    if (acceso) router.replace(`/403?code=${encodeURIComponent(acceso)}`);
  }, [acceso, router]);

  /** Cambia filtros: sin página explícita vuelve a la 1; reescribe la URL sin agregar entradas al historial. */
  function aplicar(cambios: Partial<Filtros>) {
    const siguiente: Filtros = { ...filtros, ...cambios, pagina: cambios.pagina ?? 1 };
    if (urlListado(siguiente) === urlListado(filtros)) return;
    setFiltros(siguiente);
    setCarga({ tipo: "cargando" });
    router.replace(urlListado(siguiente), { scroll: false });
  }

  function reintentar() {
    if (carga.tipo === "error" && carga.error.tipo === "AVISO" && carga.error.reiniciarPagina) {
      aplicar({ pagina: 1 });
      return;
    }
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  }

  const limpiar = () => aplicar({ desde: "", hasta: "", modo: null, estado: null, nombre: "" });
  const errorCampo = carga.tipo === "error" && carga.error.tipo === "CAMPO" ? carga.error : null;

  let contenido: ReactNode;
  if (carga.tipo === "cargando" || acceso) {
    contenido = <VehicleLoader label="Cargando los diagnósticos…" />;
  } else if (carga.tipo === "error" && carga.error.tipo === "CAMPO") {
    contenido = (
      <InlineAlert tone="warning" title="Revisa los filtros">
        Corrige el filtro marcado para ver los diagnósticos.
      </InlineAlert>
    );
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar los diagnósticos"
        action={
          <button type="button" onClick={reintentar} className={BOTON_REINTENTAR_CLS}>
            Reintentar
          </button>
        }
      >
        {carga.error.tipo === "AVISO" ? carga.error.mensaje : "Intenta de nuevo en un momento."}
      </InlineAlert>
    );
  } else if (carga.lista.diagnosticos.length === 0 && hayFiltros(filtros)) {
    contenido = (
      <Vacio titulo="No hay diagnósticos con estos filtros" Icono={SearchX}>
        <p className={TEXTO_SECUNDARIO}>Cambia las fechas, el tipo, el estado o el nombre, o quita los filtros.</p>
        <button type="button" onClick={limpiar} className={BOTON_SECUNDARIO_CLS}>
          Limpiar filtros
        </button>
      </Vacio>
    );
  } else if (carga.lista.diagnosticos.length === 0) {
    contenido = (
      <Vacio titulo="Todavía no hay diagnósticos" Icono={ClipboardList}>
        <p className={TEXTO_SECUNDARIO}>Aquí verás los diagnósticos vehiculares de tu compañía cuando empiecen a registrarse.</p>
      </Vacio>
    );
  } else {
    const { lista } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <div className="overflow-x-auto rounded-xl border border-[var(--color-flit-gray)] dark:border-white/10">
          <table aria-label="Diagnósticos de la compañía" className="w-full min-w-[860px] border-collapse text-sm">
            <thead>
              <tr>
                {COLUMNAS.map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody>
              {lista.diagnosticos.map((d) => {
                const destino = destinoDiagnostico(d);
                const estado = etiquetaEstado(d.estado);
                return (
                  <tr
                    key={d.id}
                    onClick={(e) => {
                      // El enlace del nombre navega solo (clic o Enter); el resto de la fila lleva al mismo destino.
                      if ((e.target as HTMLElement).closest("a")) return;
                      router.push(destino);
                    }}
                    className={`${FILA_TABLA_CLS} cursor-pointer bg-white transition-colors hover:bg-[var(--color-flit-gray)]/40 dark:bg-transparent dark:hover:bg-white/[0.06]`}
                  >
                    <td className={`${CELDA_TABLA_CLS} font-mono tabular-nums`}>
                      <span title={d.id}>{idCorto(d.id)}</span>
                    </td>
                    <td className={CELDA_TABLA_CLS}>
                      <Link
                        href={destino}
                        className={`text-sm font-semibold text-[var(--flit-brand-ink)] underline-offset-4 hover:underline dark:text-white ${FOCO_CLS}`}
                      >
                        {d.nombre}
                      </Link>
                    </td>
                    <td className={`${CELDA_TABLA_CLS} whitespace-nowrap tabular-nums`}>{fechaDelDato(d.creado_en)}</td>
                    <td className={CELDA_TABLA_CLS}>{nombreModo(d.modo)}</td>
                    <td className={CELDA_TABLA_CLS}>
                      {d.tipo_tramite ? (
                        d.tipo_tramite.nombre
                      ) : (
                        <span role="img" aria-label="Sin trámite">
                          —
                        </span>
                      )}
                    </td>
                    <td className={CELDA_TABLA_CLS}>
                      <Etiqueta tono={estado.tono}>{estado.texto}</Etiqueta>
                    </td>
                    <td className={CELDA_TABLA_CLS}>{d.usuario_creador.nombre}</td>
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
          pageSizeOptions={TAMANOS_LISTADO}
          ariaLabel="Paginación de diagnósticos"
          noun="diagnósticos"
        />
      </div>
    );
  }

  return (
    <section aria-labelledby="listado-diagnosticos-titulo" className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <h2 id="listado-diagnosticos-titulo" className="text-lg font-semibold text-flit-primary dark:text-white">
        Diagnósticos
      </h2>
      <FiltrosListado valor={filtros} error={errorCampo} onCambiar={aplicar} />
      <div>{contenido}</div>
    </section>
  );
}
