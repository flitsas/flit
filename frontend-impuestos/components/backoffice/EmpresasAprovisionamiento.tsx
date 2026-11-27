"use client";

import { useEffect, useId, useState, type FormEvent, type ReactNode } from "react";
import Link from "next/link";
import { useRouter } from "next/navigation";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { EstadoVacio } from "@flit/ui/EstadoVacio";
import { EtiquetaEnProceso } from "@flit/ui/EtiquetaEnProceso";
import { iconoComo } from "@flit/ui/iconoComo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
import { Pagination } from "@flit/ui/Pagination";
import { ETIQUETA_PUNTO_CLS, StatusBadge, etiquetaKit } from "@flit/ui/StatusBadge";
import {
  TABLA_MOVIL_CELL_CLS,
  TABLA_MOVIL_CELL_TITULO_CLS,
  TABLA_MOVIL_ROW_CLS,
  TABLA_MOVIL_TABLE_CLS,
  TABLA_MOVIL_TBODY_CLS,
  TABLA_MOVIL_THEAD_CLS,
} from "@flit/ui/table-styles";
import { useSesion } from "@/components/ImpuestosShell";
import {
  BOTON_REINTENTAR_CLS,
  BOTON_SECUNDARIO_CLS,
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CAMPO_CLS,
  CELDA_TABLA_CLS,
  CONTENEDOR_TABLA_CLS,
  ENLACE_ACCION_CLS,
  ETIQUETA_CLS,
  FILA_TABLA_CLS,
  FILA_TABLA_HOVER_CLS,
  TARJETA_CLS,
  TEXTO_SECUNDARIO_CLS,
} from "@/components/comun/estilos";
import {
  ERRORES_BACKOFFICE,
  ETIQUETA_USUARIOS_EMPRESA,
  TONO_ESTADO_APROVISIONAMIENTO,
  etiquetaEstadoAprovisionamiento,
  etiquetaMotivoAprovisionamiento,
  listarEmpresasBackoffice,
  type AprovisionamientoResumen,
  type PaginaEmpresasBackoffice,
} from "@/lib/backoffice/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { PERMISOS, tienePermiso } from "@/lib/sesion";

const IconoEmpresas = iconoComo("empresas");
const IconoBuscar = iconoComo("buscar");
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;

// La búsqueda vive solo en el estado de la pantalla: ni en la URL (un NIT puede ser una cédula, como en Diagnóstico) ni
// en el almacenamiento del navegador (AC-FE-43, independencia.test.ts).

/**
 * Chip del aprovisionamiento (spec-visual §1.5): etiqueta del contrato con el tono del spec; PENDIENTE con el punto
 * pulsante de EtiquetaEnProceso. En tablas (`vivo` false) va sin role="status"; en el detalle, StatusBadge. Se suman
 * «Firma sin confirmar» (LISTO sin firma), «Respaldo de firma» y, con `suspendida` (EmpresaBackoffice), la etiqueta
 * SUSPENDIDO del contrato si el estado no lo dice ya; siempre con texto.
 */
export function ChipAprovisionamiento({
  aprovisionamiento,
  modoRespaldoFirma = false,
  suspendida = false,
  vivo = false,
}: {
  aprovisionamiento: AprovisionamientoResumen;
  modoRespaldoFirma?: boolean;
  suspendida?: boolean;
  vivo?: boolean;
}) {
  const { estado, firmaConfirmada } = aprovisionamiento;
  const etiqueta = etiquetaEstadoAprovisionamiento(estado);
  const tono = TONO_ESTADO_APROVISIONAMIENTO[estado] ?? "neutral";
  let principal: ReactNode;
  if (estado === "PENDIENTE") principal = <EtiquetaEnProceso etiqueta={etiqueta} />;
  else if (vivo) principal = <StatusBadge tone={tono} label={etiqueta} />;
  else
    principal = (
      <span {...etiquetaKit(tono)}>
        <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
        {etiqueta}
      </span>
    );
  return (
    <span className="flex flex-wrap items-center gap-1.5">
      {principal}
      {estado === "LISTO" && !firmaConfirmada && (
        <span {...etiquetaKit("purple")}>
          <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
          Firma sin confirmar
        </span>
      )}
      {modoRespaldoFirma && (
        <span {...etiquetaKit("purple")}>
          <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
          Respaldo de firma
        </span>
      )}
      {suspendida && estado !== "SUSPENDIDO" && (
        <span {...etiquetaKit(TONO_ESTADO_APROVISIONAMIENTO.SUSPENDIDO ?? "neutral")}>
          <span className={ETIQUETA_PUNTO_CLS} aria-hidden="true" />
          {etiquetaEstadoAprovisionamiento("SUSPENDIDO")}
        </span>
      )}
    </span>
  );
}

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "listo"; datos: PaginaEmpresasBackoffice };

/**
 * /backoffice/empresas (spec-visual §3.10; plan §6.1): solo Super Admin. Compañías con Impuestos, incluidas las
 * suspendidas (F4-CF10, marcadas con texto), con su número de usuarios (1.1.0; celda vacía si no llega), el estado de
 * su conexión con el proveedor, el motivo legible del contrato y «Ver detalle».
 */
export function EmpresasAprovisionamiento() {
  const router = useRouter();
  const { sesion } = useSesion();
  const { empresa } = useEmpresaSeleccionada();
  const puede = tienePermiso(sesion, PERMISOS.backofficeVer) && sesion?.esSuperAdmin === true;
  const sinAcceso = sesion !== null && !puede;
  const buscarId = useId();
  const [texto, setTexto] = useState("");
  const [filtros, setFiltros] = useState({ q: "", page: 1 });
  const [intento, setIntento] = useState(0);
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  useEffect(() => {
    if (!puede) return;
    const controller = new AbortController();
    listarEmpresasBackoffice(filtros, controller.signal).then(
      (datos) => {
        if (!controller.signal.aborted) setCarga({ tipo: "listo", datos });
      },
      (e: unknown) => {
        if (!controller.signal.aborted) setCarga({ tipo: "error", mensaje: mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.", ERRORES_BACKOFFICE) });
      },
    );
    return () => controller.abort();
  }, [puede, filtros, intento]);

  if (!sesion || !puede) return null;

  const releer = () => {
    setCarga({ tipo: "cargando" });
    setIntento((n) => n + 1);
  };
  const buscar = (ev: FormEvent) => {
    ev.preventDefault();
    const q = texto.trim();
    setFiltros({ q, page: 1 });
    releer();
  };

  let contenido: ReactNode;
  if (carga.tipo === "cargando") contenido = <VehicleLoader label="Cargando compañías…" />;
  else if (carga.tipo === "error")
    contenido = (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar las compañías"
        action={
          <button type="button" className={BOTON_REINTENTAR_CLS} onClick={releer}>
            Reintentar
          </button>
        }
      >
        {carga.mensaje}
      </InlineAlert>
    );
  else if (carga.datos.items.length === 0)
    contenido = filtros.q ? (
      <EstadoVacio
        anunciar
        nivelTitulo={3}
        icono={<IconoBuscar className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />}
        titulo="Ninguna compañía coincide"
        acciones={[
          <button
            key="limpiar"
            type="button"
            className={BOTON_SECUNDARIO_CLS}
            onClick={() => {
              setTexto("");
              setFiltros({ q: "", page: 1 });
              releer();
            }}
          >
            Limpiar búsqueda
          </button>,
        ]}
      />
    ) : (
      <EstadoVacio nivelTitulo={3} icono={<IconoEmpresas className="h-7 w-7" aria-hidden="true" strokeWidth={1.5} />} titulo="Ninguna compañía tiene Impuestos activo todavía" />
    );
  else {
    const { datos } = carga;
    contenido = (
      <div className="flex flex-col gap-3">
        <div className={CONTENEDOR_TABLA_CLS}>
          <table aria-label="Compañías con Impuestos" className={`w-full min-w-[900px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
            <thead className={TABLA_MOVIL_THEAD_CLS}>
              <tr>
                {["Compañía", "NIT", ETIQUETA_USUARIOS_EMPRESA, "Estado", "Motivo", "Acción"].map((t) => (
                  <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                    {t}
                  </th>
                ))}
              </tr>
            </thead>
            <tbody className={TABLA_MOVIL_TBODY_CLS}>
              {datos.items.map((e) => {
                const motivo = etiquetaMotivoAprovisionamiento(e.aprovisionamiento.motivo);
                return (
                  <tr key={e.id} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                    <td data-label="Compañía" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} font-semibold`}>
                      {e.nombre}
                    </td>
                    <td data-label="NIT" className={`${CELDA_CLS} tabular-nums`}>
                      {e.nit ?? <span className={TEXTO_SECUNDARIO_CLS}>Sin dato</span>}
                    </td>
                    <td data-label={ETIQUETA_USUARIOS_EMPRESA} className={`${CELDA_CLS} tabular-nums`}>
                      {typeof e.usuarios === "number" ? e.usuarios : null}
                    </td>
                    <td data-label="Estado" className={CELDA_CLS}>
                      <ChipAprovisionamiento aprovisionamiento={e.aprovisionamiento} suspendida={e.suspendida === true} />
                    </td>
                    <td data-label="Motivo" className={CELDA_CLS}>
                      {motivo ?? <span className={TEXTO_SECUNDARIO_CLS}>Sin dato</span>}
                    </td>
                    <td data-label="Acción" className={CELDA_CLS}>
                      <Link href={hrefConEmpresa(`/backoffice/empresas/${encodeURIComponent(e.id)}`, empresa)} className={ENLACE_ACCION_CLS}>
                        Ver detalle <span className="sr-only">{`de ${e.nombre}`}</span>
                      </Link>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
        <Pagination
          page={datos.page}
          pageSize={datos.pageSize}
          totalCount={datos.total}
          onPageChange={(page) => {
            setCarga({ tipo: "cargando" });
            setFiltros((f) => ({ ...f, page }));
          }}
          ariaLabel="Paginación de compañías"
          noun="compañías"
        />
      </div>
    );
  }

  return (
    <>
      <CabeceraModulo titulo="Compañías con Impuestos" icono="empresas" descripcion="Estado de la conexión de cada compañía con el proveedor" />
      <InlineAlert tone="info" vivo={false}>
        Estás en el backoffice de FLIT: ves todas las compañías, incluidas las suspendidas.
      </InlineAlert>
      <section aria-label="Compañías" className={`${TARJETA_CLS} flex flex-col gap-4`}>
        <form role="search" aria-label="Buscar compañías" onSubmit={buscar} className="flex flex-wrap items-end gap-2">
          <div className="flex min-w-0 flex-1 flex-col gap-1 sm:max-w-sm">
            <label htmlFor={buscarId} className={ETIQUETA_CLS}>
              Buscar por nombre o NIT
            </label>
            <input id={buscarId} type="search" value={texto} maxLength={100} onChange={(ev) => setTexto(ev.target.value)} className={CAMPO_CLS} />
          </div>
          <button type="submit" className={BOTON_SECUNDARIO_CLS}>
            Buscar
          </button>
        </form>
        <div>{contenido}</div>
      </section>
    </>
  );
}
