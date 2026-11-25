"use client";

import { useCallback, useEffect, useId, useState } from "react";
import Link from "next/link";
import { notFound, useRouter } from "next/navigation";
import { Button } from "@flit/ui/Button";
import { CabeceraModulo } from "@flit/ui/CabeceraModulo";
import { InlineAlert } from "@flit/ui/InlineAlert";
import { VehicleLoader } from "@flit/ui/Loader";
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
  CABECERA_TABLA_CLS,
  CABECERA_TABLA_ESTILO,
  CELDA_TABLA_CLS,
  CONTENEDOR_TABLA_CLS,
  ENLACE_ACCION_CLS,
  FILA_TABLA_CLS,
  FILA_TABLA_HOVER_CLS,
  TARJETA_CLS,
  TEXTO_SECUNDARIO_CLS,
} from "@/components/comun/estilos";
import { ApiError } from "@/lib/api";
import {
  ERRORES_BACKOFFICE,
  ESTADOS_REINTENTABLES,
  ETIQUETA_ACTOR,
  buscarEmpresaBackoffice,
  etiquetaEstadoAprovisionamiento,
  etiquetaMotivoAprovisionamiento,
  obtenerAprovisionamiento,
  reintentarAprovisionamiento,
  type AprovisionamientoDetalle,
  type EmpresaBackoffice,
} from "@/lib/backoffice/api";
import { hrefConEmpresa, useEmpresaSeleccionada } from "@/lib/empresaSeleccionada";
import { mensajeErrorFijo } from "@/lib/errores";
import { rotular } from "@/lib/rotulos";
import { PERMISOS, tienePermiso } from "@/lib/sesion";
import { ChipAprovisionamiento } from "./EmpresasAprovisionamiento";
import { FormularioCredenciales } from "./FormularioCredenciales";

const FECHA = new Intl.DateTimeFormat("es-CO", { day: "2-digit", month: "2-digit", year: "numeric", hour: "2-digit", minute: "2-digit", hour12: false, timeZone: "America/Bogota" });
const fecha = (iso: string) => FECHA.format(new Date(iso)).replace(",", "");
const CELDA_CLS = `${CELDA_TABLA_CLS} ${TABLA_MOVIL_CELL_CLS}`;

type Carga = { tipo: "cargando" } | { tipo: "error"; mensaje: string } | { tipo: "no-encontrada" } | { tipo: "listo"; detalle: AprovisionamientoDetalle; empresa: EmpresaBackoffice | null };
type Reintento = { tipo: "ok" } | { tipo: "error"; mensaje: string } | null;

/**
 * /backoffice/empresas/[tenantId] (spec-visual §3.10; plan §6.1): solo Super Admin. Estado de la conexión con el
 * proveedor (chip, motivo legible del contrato y «Reintentar aprovisionamiento» en ERROR, INCIERTO y
 * REQUIERE_REVISION_NIT), credenciales protegidas, enlace al contrato e historial. El nombre de la compañía sale de la
 * lista del backoffice: el detalle del contrato no lo trae. 404 ⇒ notFound().
 */
export function DetalleAprovisionamiento({ tenantId }: { tenantId: string }) {
  const router = useRouter();
  const { sesion } = useSesion();
  const { empresa: empresaUrl } = useEmpresaSeleccionada();
  const puede = tienePermiso(sesion, PERMISOS.backofficeVer) && sesion?.esSuperAdmin === true;
  const sinAcceso = sesion !== null && !puede;
  const estadoId = useId();
  const [carga, setCarga] = useState<Carga>({ tipo: "cargando" });
  const [intento, setIntento] = useState(0);
  const [reintento, setReintento] = useState<Reintento>(null);
  const [reintentando, setReintentando] = useState(false);

  useEffect(() => {
    if (sinAcceso) router.replace("/403?code=SIN_PERMISO");
  }, [sinAcceso, router]);

  useEffect(() => {
    if (!puede) return;
    const controller = new AbortController();
    Promise.all([obtenerAprovisionamiento(tenantId, controller.signal), buscarEmpresaBackoffice(tenantId, controller.signal)]).then(
      ([detalle, empresa]) => {
        if (!controller.signal.aborted) setCarga({ tipo: "listo", detalle, empresa });
      },
      (e: unknown) => {
        if (controller.signal.aborted) return;
        if (e instanceof ApiError && e.status === 404) setCarga({ tipo: "no-encontrada" });
        else setCarga({ tipo: "error", mensaje: mensajeErrorFijo(e, "Revisa tu conexión e intenta de nuevo.", ERRORES_BACKOFFICE) });
      },
    );
    return () => controller.abort();
  }, [puede, tenantId, intento]);

  /** Relee el estado sin volver al «cargando»: el aviso del reintento o de las credenciales sigue a la vista. */
  const releerEstado = useCallback(() => {
    obtenerAprovisionamiento(tenantId).then(
      (detalle) => setCarga((c) => (c.tipo === "listo" ? { ...c, detalle } : c)),
      () => {},
    );
  }, [tenantId]);

  if (carga.tipo === "no-encontrada") {
    notFound();
    return null;
  }
  if (!sesion || !puede) return null;

  const reintentar = async () => {
    if (reintentando) return;
    setReintentando(true);
    setReintento(null);
    try {
      await reintentarAprovisionamiento(tenantId);
      setReintento({ tipo: "ok" });
      releerEstado();
    } catch (e) {
      setReintento({ tipo: "error", mensaje: mensajeErrorFijo(e, "No se pudo pedir el reintento. Intenta de nuevo.", ERRORES_BACKOFFICE) });
    } finally {
      setReintentando(false);
    }
  };

  if (carga.tipo === "cargando") return <VehicleLoader label="Cargando la compañía…" />;
  if (carga.tipo === "error")
    return (
      <InlineAlert
        tone="error"
        title="No se pudieron cargar las compañías"
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

  const { detalle, empresa } = carga;
  const nombre = empresa?.nombre ?? "Compañía";
  const motivo = etiquetaMotivoAprovisionamiento(detalle.motivo);
  const etiquetaDe = (estado: string | null) => (estado ? etiquetaEstadoAprovisionamiento(estado) : "Sin dato");

  return (
    <>
      <CabeceraModulo
        titulo={nombre}
        icono="empresas"
        miga={[{ etiqueta: "Backoffice", href: hrefConEmpresa("/backoffice/empresas", empresaUrl) }, { etiqueta: nombre }]}
        descripcion={empresa?.nit ? `NIT ${empresa.nit}` : undefined}
      />
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-2">
        <section role="group" aria-labelledby={estadoId} className={`${TARJETA_CLS} flex flex-col gap-3`}>
          <h2 id={estadoId} className="text-base font-semibold text-flit-primary">
            Estado
          </h2>
          <ChipAprovisionamiento aprovisionamiento={detalle} modoRespaldoFirma={detalle.modoRespaldoFirma} vivo />
          {motivo && <p className="text-sm text-flit-primary">{motivo}</p>}
          <p className={TEXTO_SECUNDARIO_CLS}>
            Actualizado el <time dateTime={detalle.actualizadoEn}>{fecha(detalle.actualizadoEn)}</time>
          </p>
          {ESTADOS_REINTENTABLES.has(detalle.estado) && (
            <div>
              <Button variant="secondary" size="sm" onClick={() => void reintentar()} cargando={reintentando}>
                Reintentar aprovisionamiento
              </Button>
            </div>
          )}
          {reintento?.tipo === "error" && (
            <InlineAlert tone="error" title="No se pudo reintentar">
              {reintento.mensaje}
            </InlineAlert>
          )}
          <div role="status" aria-live="polite" aria-atomic="true">
            {reintento?.tipo === "ok" && (
              <InlineAlert tone="success" vivo={false}>
                Reintento en cola. El estado se actualiza en unos segundos.
              </InlineAlert>
            )}
          </div>
        </section>
        <section className={TARJETA_CLS}>
          <FormularioCredenciales tenantId={tenantId} estado={detalle.estado} onGuardado={releerEstado} />
        </section>
        <section aria-labelledby={`${estadoId}-contrato`} className={`${TARJETA_CLS} flex flex-col gap-2 lg:col-span-2`}>
          <h2 id={`${estadoId}-contrato`} className="text-base font-semibold text-flit-primary">
            Contrato
          </h2>
          <div>
            <Link href={`/configuracion?pestana=contractual&empresa=${encodeURIComponent(tenantId)}`} className={ENLACE_ACCION_CLS}>
              Editar contrato
            </Link>
          </div>
        </section>
      </div>
      <section aria-labelledby={`${estadoId}-historial`} className={`${TARJETA_CLS} flex flex-col gap-3`}>
        <h2 id={`${estadoId}-historial`} className="text-base font-semibold text-flit-primary">
          Historial
        </h2>
        {detalle.historial.length === 0 ? (
          <p className={TEXTO_SECUNDARIO_CLS}>Sin cambios registrados.</p>
        ) : (
          <div className={CONTENEDOR_TABLA_CLS}>
            <table aria-label="Historial del aprovisionamiento" className={`w-full min-w-[720px] border-collapse text-sm ${TABLA_MOVIL_TABLE_CLS}`}>
              <thead className={TABLA_MOVIL_THEAD_CLS}>
                <tr>
                  {["Fecha", "De", "A", "Motivo", "Por"].map((t) => (
                    <th key={t} scope="col" className={CABECERA_TABLA_CLS} style={CABECERA_TABLA_ESTILO}>
                      {t}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className={TABLA_MOVIL_TBODY_CLS}>
                {detalle.historial.map((h, i) => (
                  <tr key={`${h.en}-${i}`} className={`${FILA_TABLA_CLS} ${FILA_TABLA_HOVER_CLS} ${TABLA_MOVIL_ROW_CLS}`}>
                    <td data-label="Fecha" className={`${CELDA_CLS} ${TABLA_MOVIL_CELL_TITULO_CLS} tabular-nums`}>
                      <time dateTime={h.en}>{fecha(h.en)}</time>
                    </td>
                    <td data-label="De" className={CELDA_CLS}>
                      {etiquetaDe(h.desde)}
                    </td>
                    <td data-label="A" className={CELDA_CLS}>
                      {etiquetaDe(h.hacia)}
                    </td>
                    <td data-label="Motivo" className={CELDA_CLS}>
                      {etiquetaMotivoAprovisionamiento(h.motivo) ?? <span className={TEXTO_SECUNDARIO_CLS}>Sin dato</span>}
                    </td>
                    <td data-label="Por" className={CELDA_CLS}>
                      {h.actorTipo ? rotular({ contexto: "actor del historial", codigo: h.actorTipo, respaldo: ETIQUETA_ACTOR }) : "Sistema"}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </section>
    </>
  );
}
