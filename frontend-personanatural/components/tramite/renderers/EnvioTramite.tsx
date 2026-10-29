"use client";

import { skipToken, useQuery } from "@tanstack/react-query";
import { Check, FileText, TriangleAlert } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useRef, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { RadicadoTracking } from "@/components/modales/RadicadoTracking";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Card } from "@/components/tramite/piezas";
import type { Adjunto, Cotizacion, DefinicionTipo, EstadoSolicitud, TipoTramite } from "@/lib/api/cliente";
import { ApiError } from "@/lib/api/errores";
import { esEstadoFinal, INTERVALO_SONDEO_MS, pasosDeSeguimiento } from "@/lib/tramite/envio";
import { limpiarContexto } from "@/lib/tramite/contexto";
import { claveCotizacion } from "@/lib/tramite/cotizacion";
import { formatearPesos } from "@/lib/formato";
import { esEditable } from "@/lib/tramite/pasos";
import { archivoRecordado, nombreRecordado } from "@/lib/tramite/presentacion";

export const claveEstado = (id: string) => ["pn", "estado", id] as const;

/**
 * Paso 5 «Envío»: solo la confirmación. El botón de radicar está en Resumen. Si se abre este paso con el
 * trámite todavía en borrador, se vuelve a Resumen.
 */
export function EnvioTramite({ onEstado, activa }: PropsSeccion) {
  const { solicitud, irAPaso } = useWizard();

  useEffect(() => onEstado({ completo: true }), [onEstado]);

  useEffect(() => {
    if (activa && esEditable(solicitud.estado)) irAPaso("resumen");
  }, [activa, solicitud.estado, irAPaso]);

  if (esEditable(solicitud.estado)) return null;
  return <Confirmacion />;
}

// ── Después de enviar ───────────────────────────────────────────────────────────────────────────────────────────────

function Confirmacion() {
  const router = useRouter();
  const { cliente, definicion, solicitud, reportarError } = useWizard();
  const [consultando, setConsultando] = useState(false);
  const titulo = useRef<HTMLHeadingElement>(null);

  // Sondeo prudente del estado (GET /estado, sin datos personales): se detiene al llegar a un estado final, al salir de la pantalla
  // (la consulta se cancela al desmontar) y si la sesión ya no sirve.
  const estado = useQuery<EstadoSolicitud>({
    queryKey: claveEstado(solicitud.solicitudId),
    queryFn: ({ signal }) => cliente.solicitudes.estado(solicitud.solicitudId, { signal }),
    retry: false,
    gcTime: 0,
    refetchInterval: (consulta) => {
      const error = consulta.state.error;
      if (error instanceof ApiError && (error.code.startsWith("session_") || error.status === 401 || error.status === 403 || error.status === 404)) return false;
      return consulta.state.data && esEstadoFinal(consulta.state.data.estado) ? false : INTERVALO_SONDEO_MS;
    },
  });
  const error = estado.error;
  useEffect(() => {
    if (error) reportarError(error);
  }, [error, reportarError]);

  const codigo = estado.data?.estado ?? solicitud.estado;
  const cancelada = codigo === "cancelada";
  const referencia = estado.data?.numeroReferencia?.trim() || null;
  const seguimiento = pasosDeSeguimiento({ estado: codigo, actualizadoEn: estado.data?.actualizadoEn ?? "" }, definicion);

  // El foco pasa al título cuando aparece la confirmación (lectores de pantalla y teclado).
  useEffect(() => {
    titulo.current?.focus({ preventScroll: true });
  }, []);

  const salir = () => {
    // La cédula del contexto en memoria no se conserva al salir.
    limpiarContexto();
    router.push("/");
  };

  const tipos: TipoTramite[] = [{ code: definicion.code, name: definicion.name, family: definicion.family, version: definicion.version, disponible: true }];

  return (
    <div role="region" aria-label="Confirmación del trámite" className="space-y-6">
      <Card>
        <div className="flex flex-col items-center text-center">
          {cancelada ? (
            <span aria-hidden className="grid h-16 w-16 place-items-center rounded-full bg-alert text-primary-foreground">
              <TriangleAlert className="h-9 w-9" />
            </span>
          ) : (
            <span aria-hidden className="grid h-16 w-16 place-items-center rounded-full bg-success text-primary-foreground">
              <Check className="h-9 w-9" />
            </span>
          )}
          <h2
            ref={titulo}
            tabIndex={-1}
            className={`mt-4 text-[32px] font-bold leading-tight ${cancelada ? "text-alert" : "text-success"}`}
            aria-label={cancelada ? undefined : "¡Muchas gracias, tu trámite se ha enviado con éxito!"}
          >
            {cancelada ? (
              "Este trámite fue cancelado"
            ) : (
              <>
                ¡Muchas gracias,
                <br />
                tu trámite se ha enviado con éxito!
              </>
            )}
          </h2>

          <div className="mt-6 w-full overflow-x-auto rounded-[9px] border border-border">
            <table className="w-full min-w-[600px] text-center text-sm">
              <thead className="bg-primary text-primary-foreground">
                <tr>
                  {["ID del trámite", "Tipo", "Nº radicado", "Organismo"].map((t) => (
                    <th key={t} scope="col" className="p-4 text-center align-middle font-bold">
                      {t}
                    </th>
                  ))}
                </tr>
              </thead>
              <tbody className="bg-card">
                <tr>
                  <td className="border-t border-border p-4 text-center align-middle font-semibold" data-testid="radicado-entregado">{solicitud.codigoPublico}</td>
                  <td className="border-t border-border p-4 text-center align-middle">{definicion.name}</td>
                  <td className="border-t border-border p-4 text-center align-middle">{referencia ?? "Pendiente"}</td>
                  <td className="border-t border-border p-4 text-center align-middle">{solicitud.organismoNombre}</td>
                </tr>
              </tbody>
            </table>
          </div>

          {!cancelada && <RadicadoTracking pasos={seguimiento} />}

          <div aria-live="polite" className="mt-2 max-w-2xl space-y-2">
            {cancelada ? (
              <p>Este trámite fue cancelado y ya no se puede enviar.</p>
            ) : (
              <p>Para consultar el estado de tu trámite o la respuesta del Organismo de Tránsito, utiliza tu ID de trámite o el número de radicado.</p>
            )}
            {estado.isError && !esEstadoFinal(codigo) && (
              <p role="status" className="text-sm text-muted-foreground">
                No pudimos actualizar el estado ahora. Lo intentaremos de nuevo.
              </p>
            )}
          </div>
        </div>
      </Card>

      <FichasEnviadas />

      <div className="flex flex-wrap justify-end gap-3">
        <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={() => setConsultando(true)}>
          Consultar mi trámite
        </button>
        <button type="button" className={boton("flit", "min-h-11 px-6", "none")} onClick={salir}>
          Ir al Dashboard
        </button>
      </div>

      <ConsultarModal abierto={consultando} onCerrar={() => setConsultando(false)} cliente={cliente} tipos={tipos} radicadoInicial={solicitud.codigoPublico} />
    </div>
  );
}

/** Nombre que ve el ciudadano: el del documento en el trámite, nunca el archivo con guiones bajos. */
function nombreDeArchivo(solicitudId: string, doc: Adjunto, definicion: DefinicionTipo): string {
  const definido = definicion.documentos.find((d) => d.docCode === doc.tipo);
  if (definido?.nombre && doc.tipo !== "comprobante_pago") return definido.nombre;
  const recordado = archivoRecordado(solicitudId, doc.id);
  const crudo = recordado || (doc.nombreArchivo && doc.nombreArchivo !== doc.tipo ? doc.nombreArchivo : "");
  return nombreLegible(crudo) || "Documento";
}

/** «Certificado_de_Autoridad.pdf» queda «Certificado de Autoridad». */
function nombreLegible(archivo: string): string {
  const sinExtension = archivo.replace(/\.[a-z0-9]+$/i, "");
  return sinExtension.replace(/[_-]+/g, " ").replace(/\s+/g, " ").trim();
}

/** Fichas del paso Envío del diseño: vehículo, actores, condiciones, valor y los archivos que sí se cargaron. */
function FichasEnviadas() {
  const { definicion, solicitud } = useWizard();
  const transformaciones = solicitud.datos.requisitos?.transformaciones ?? [];
  const cotizacion = useQuery<Cotizacion>({
    queryKey: claveCotizacion(solicitud.organismoId, definicion.code, transformaciones),
    queryFn: skipToken,
    staleTime: Infinity,
  });
  const actores = [...solicitud.actores].sort((a, b) => a.rol.localeCompare(b.rol) || a.ordinal - b.ordinal);
  const venta = solicitud.datos.comercial?.valor;
  const prenda = solicitud.datos.prenda?.tiene === true;
  const vehiculo = [solicitud.placa, solicitud.vin].filter(Boolean).join(" · ");
  const documentos = solicitud.adjuntos.filter((a) => a.tipo !== "comprobante_pago");

  return (
    <>
      <div className="grid gap-4 md:grid-cols-2">
        <Card title="Vehículo" id="envio-vehiculo">
          <div className="space-y-1">
            <p>{vehiculo || "—"}</p>
            <p className="text-xs font-light text-muted-foreground">{solicitud.organismoNombre}</p>
          </div>
        </Card>
        <Card title="Actores" id="envio-actores">
          {actores.length === 0 ? (
            <p className="text-sm text-muted-foreground">Sin personas registradas.</p>
          ) : (
            <div className="space-y-1">
              {actores.map((actor) => {
                const rol = definicion.actores.find((r) => r.rol === actor.rol);
                const etiqueta = rol?.etiqueta || actor.rol;
                const nombre = nombreRecordado(solicitud.solicitudId, actor.rol, actor.ordinal) || actor.nombre?.trim() || "—";
                return (
                  <p key={`${actor.rol}-${actor.ordinal}`}>
                    {etiqueta}: {nombre}
                  </p>
                );
              })}
            </div>
          )}
        </Card>
        <Card title="Condiciones comerciales" id="envio-comercial">
          <div className="space-y-1">
            <p>Venta: {venta != null && venta > 0 ? formatearPesos(venta) : "—"}</p>
            <p>Prenda: {prenda ? "Sí" : "No"}</p>
            {documentos.length > 0 && <p className="text-xs font-light text-muted-foreground">{documentos.map((d) => nombreDeArchivo(solicitud.solicitudId, d, definicion)).join(", ")}</p>}
          </div>
        </Card>
        <Card title="Valor total del trámite" id="envio-valor">
          <p className="text-2xl font-bold">{cotizacion.data ? formatearPesos(cotizacion.data.totalCop) : "—"}</p>
          {cotizacion.data?.lineas.map((linea, i) => (
            <p key={linea.codigo} className={`${i === 0 ? "mt-3" : "mt-1"} text-xs font-light text-muted-foreground`}>
              {linea.nombre}: {formatearPesos(linea.valorCop)}
            </p>
          ))}
        </Card>
      </div>
      <Card title="Documentos cargados" id="envio-documentos">
        {documentos.length === 0 ? (
          <p className="text-sm text-muted-foreground">Todavía no hay documentos cargados.</p>
        ) : (
          <div className="grid grid-cols-[repeat(auto-fit,minmax(16rem,1fr))] gap-4">
            {documentos.map((doc) => (
              <div key={doc.id} className="flex h-full min-w-0 flex-col items-center gap-3 rounded-[9px] border border-border p-4 text-center">
                <FileText aria-hidden className="h-7 w-7 text-primary" />
                <p className="w-full text-sm font-medium [overflow-wrap:anywhere]">{nombreDeArchivo(solicitud.solicitudId, doc, definicion)}</p>
                <span className="mt-auto rounded-full bg-success/10 px-3 py-1 text-xs font-semibold text-success">Cargado</span>
              </div>
            ))}
          </div>
        )}
      </Card>
    </>
  );
}
