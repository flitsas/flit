"use client";

import { useEffect, useEffectEvent, useId, useState, type ReactNode } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { Pagination } from "@flit/ui/Pagination";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { BOTON_REINTENTAR_CLS, BOTON_SECUNDARIO_CLS, CAMPO_CLS, ENLACE_ACCION_CLS, ETIQUETA_CLS, TARJETA_CLS } from "@/components/configuracion/estilos";
import { CABECERA_TABLA_CLS, CABECERA_TABLA_ESTILO, CELDA_TABLA_CLS, FILA_TABLA_CLS } from "@/components/tablero/vistasDetalle/comun";
import { listarEmpresasUsuarios, traducirErrorUsuarios } from "@/lib/usuarios/api";
import { BUSQUEDA_MAXIMA, ESPERA_BUSQUEDA_MS, type FiltroConUsuarios, type FiltrosEmpresas, type ListaEmpresasUsuarios } from "@/lib/usuarios/tipos";
import { EsqueletoTabla } from "./TablaUsuarios";

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; lista: ListaEmpresasUsuarios };

const COLUMNAS = ["Compañía", "NIT", "Usuarios", "Administradores", "Acción"] as const;
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;
const IconoBuscar = iconoComo("buscar");
const IconoEmpresas = iconoComo("empresas");

/** Filtros de la tabla de compañías en la URL de /usuarios (sin `empresa`): para volver a ellos desde las migas. */
export function consultaEmpresas(f: Pick<FiltrosEmpresas, "busqueda" | "usuarios">): URLSearchParams {
  const q = new URLSearchParams();
  if (f.busqueda.trim()) q.set("busqueda", f.busqueda.trim());
  if (f.usuarios) q.set("usuarios", f.usuarios);
  return q;
}

/** Destino de una compañía: /usuarios?empresa=<id> conservando los filtros del paso 1. */
export function destinoEmpresa(id: string, f: Pick<FiltrosEmpresas, "busqueda" | "usuarios">): string {
  const q = new URLSearchParams({ empresa: id });
  consultaEmpresas(f).forEach((v, k) => q.set(k, v));
  return `/usuarios?${q.toString()}`;
}

export interface TablaCompaniasProps {
  filtros: FiltrosEmpresas;
  onCambiar: (cambios: Partial<FiltrosEmpresas>) => void;
}

/**
 * Paso 1 del SuperAdmin (HU #14588, spec-ui §10.2): compañías con Diagnóstico activo (GET /backoffice/usuarios/empresas)
 * en la tabla por defecto del kit, con Compañía · NIT · Usuarios · Administradores · Acción. Buscador por nombre o NIT y
 * filtro «Usuarios» (con o sin); no hay filtro de estado porque Identidad solo devuelve activas (H-U3). La fila y su
 * enlace «Gestionar usuarios de {Compañía}» llevan a /usuarios?empresa=<id>. Cuatro estados.
 */
export function TablaCompanias({ filtros, onCambiar }: TablaCompaniasProps) {
  const router = useRouter();
  const id = useId();
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [busqueda, setBusqueda] = useState(filtros.busqueda);
  const [externa, setExterna] = useState(filtros.busqueda);
  const clave = JSON.stringify(filtros);
  const [claveVista, setClaveVista] = useState(clave);

  if (filtros.busqueda !== externa) {
    setExterna(filtros.busqueda);
    setBusqueda(filtros.busqueda);
  }
  if (clave !== claveVista) {
    setClaveVista(clave);
    setCarga({ tipo: "cargando" });
  }

  useEffect(() => {
    const control = new AbortController();
    listarEmpresasUsuarios(JSON.parse(clave) as FiltrosEmpresas, control.signal).then(
      (lista) => {
        if (!control.signal.aborted) setCarga({ tipo: "listo", lista });
      },
      (e: unknown) => {
        if (!control.signal.aborted) setCarga({ tipo: "error", mensaje: traducirErrorUsuarios(e, "No pudimos cargar las compañías. Revisa tu conexión.") });
      },
    );
    return () => control.abort();
  }, [clave, intento]);

  const emitir = useEffectEvent((texto: string) => {
    if (texto !== filtros.busqueda) onCambiar({ busqueda: texto, pagina: 1 });
  });
  useEffect(() => {
    const espera = setTimeout(() => emitir(busqueda), ESPERA_BUSQUEDA_MS);
    return () => clearTimeout(espera);
  }, [busqueda]);

  const hayFiltros = Boolean(filtros.busqueda.trim() || filtros.usuarios);
  const limpiar = () => onCambiar({ busqueda: "", usuarios: null, pagina: 1 });

  let contenido: ReactNode;
  if (carga.tipo === "cargando") {
    contenido = <EsqueletoTabla etiqueta="Cargando compañías…" />;
  } else if (carga.tipo === "error") {
    contenido = (
      <InlineAlert
        tone="error"
        title="No pudimos cargar las compañías"
        action={
          <button
            type="button"
            className={BOTON_REINTENTAR_CLS}
            onClick={() => {
              setCarga({ tipo: "cargando" });
              setIntento((n) => n + 1);
            }}
          >
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  } else if (carga.lista.empresas.length === 0 && hayFiltros) {
    contenido = (
      <EstadoVacio
        anunciar
        nivelTitulo={3}
        icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ninguna compañía coincide"
        mensaje="Prueba con otro nombre o NIT, o quita el filtro de usuarios."
        acciones={[
          <button key="limpiar" type="button" onClick={limpiar} className={BOTON_SECUNDARIO_CLS}>
            Limpiar filtros
          </button>,
        ]}
      />
    );
  } else if (carga.lista.empresas.length === 0) {
    contenido = (
      <EstadoVacio
        nivelTitulo={3}
        icono={<IconoEmpresas className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ninguna compañía tiene Diagnóstico activo"
        mensaje="Cuando una compañía active Diagnóstico, aparecerá aquí."
      />
    );
  } else {
    const { lista } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <p className="text-sm text-muted-foreground">{`Mostrando ${lista.empresas.length} de ${lista.total} compañías`}</p>
        <div className="overflow-x-auto rounded-xl border border-border">
          <table aria-label="Compañías con Diagnóstico activo" className={`w-full min-w-[720px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {COLUMNAS.map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {lista.empresas.map((e) => {
                const destino = destinoEmpresa(e.id, filtros);
                return (
                  <tr
                    key={e.id}
                    onClick={(ev) => {
                      // El enlace navega solo (clic o Enter); el resto de la fila lleva al mismo destino.
                      if ((ev.target as HTMLElement).closest("a")) return;
                      router.push(destino);
                    }}
                    className={`${FILA_TABLA_CLS} cursor-pointer bg-card transition-colors duration-[var(--dx-motion-rapido)] hover:bg-[var(--table-row-hover)] motion-reduce:transition-none ${TABLA_MOVIL_ROW_CLS}`}
                  >
                    <td data-label="Compañía" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} font-semibold`}>
                      {e.nombre}
                    </td>
                    <td data-label="NIT" className={`${CELDA_CLS} tabular-nums`}>
                      {e.nit ?? "Sin NIT"}
                    </td>
                    <td data-label="Usuarios" className={`${CELDA_CLS} tabular-nums`}>
                      {e.usuarios_diagnostico}
                    </td>
                    <td data-label="Administradores" className={`${CELDA_CLS} tabular-nums`}>
                      {e.administradores_diagnostico}
                    </td>
                    <td data-label="Acción" className={CELDA_CLS}>
                      <Link href={destino} aria-label={`Gestionar usuarios de ${e.nombre}`} className={ENLACE_ACCION_CLS}>
                        Gestionar usuarios
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination
          page={lista.pagina}
          pageSize={filtros.tamano}
          totalCount={lista.total}
          onPageChange={(pagina) => onCambiar({ pagina })}
          onPageSizeChange={(tamano) => onCambiar({ tamano, pagina: 1 })}
          ariaLabel="Paginación de compañías"
          noun="compañías"
        />
      </div>
    );
  }

  return (
    <section aria-labelledby={`${id}-titulo`} className={`${TARJETA_CLS} flex flex-col gap-4`}>
      <h2 id={`${id}-titulo`} className="text-lg font-semibold text-flit-primary">
        Compañías
      </h2>
      <div role="group" aria-label="Filtros de compañías" className="flex flex-wrap items-end gap-4">
        <div className="flex w-full min-w-56 flex-col gap-1 sm:w-auto sm:flex-1">
          <label htmlFor={`${id}-busqueda`} className={ETIQUETA_CLS}>
            Buscar por nombre o NIT
          </label>
          <input
            id={`${id}-busqueda`}
            type="text"
            enterKeyHint="search"
            autoComplete="off"
            value={busqueda}
            maxLength={BUSQUEDA_MAXIMA}
            onChange={(ev) => setBusqueda(ev.target.value)}
            className={CAMPO_CLS}
          />
        </div>
        <div className="flex flex-col gap-1">
          <label htmlFor={`${id}-usuarios`} className={ETIQUETA_CLS}>
            Usuarios
          </label>
          <select
            id={`${id}-usuarios`}
            value={filtros.usuarios ?? ""}
            onChange={(ev) => onCambiar({ usuarios: (ev.target.value || null) as FiltroConUsuarios | null, pagina: 1 })}
            className={`${CAMPO_CLS} w-48`}
          >
            <option value="">Todas</option>
            <option value="CON">Con usuarios</option>
            <option value="SIN">Sin usuarios</option>
          </select>
        </div>
        {hayFiltros && (
          <button type="button" onClick={limpiar} className={`${BOTON_SECUNDARIO_CLS} min-h-11`}>
            Limpiar filtros
          </button>
        )}
      </div>
      <div>{contenido}</div>
    </section>
  );
}
