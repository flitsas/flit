"use client";

// HU #14238 — Paso Actores del canal OT: mismas fichas de vendedor/comprador que el portal PN + identidad in-situ e invitaciones.
import { useCallback, useEffect, useRef, useState } from "react";
import { AlertTriangle, Check, Copy } from "lucide-react";
import { MobilityLoader } from "@/components/MobilityLoader";
import type { TonoAviso } from "@/components/landing/AvisoModal";
import { boton } from "@/components/landing/estilos";
import { PersonaActor } from "@/components/tramite/renderers/PersonaActor";
import { Card, Nav } from "@/components/tramite/piezas";
import type {
  ActorRol,
  ClientePortal,
  DefinicionTipo,
  EstadoIdentidad,
  EstadoValidacion,
  ParteIdentidad,
  ResultadoParte,
  Solicitud,
} from "@/lib/api/cliente";
import { mensajeDeFallo } from "@/lib/api/errores";
import {
  actoresParaGuardar,
  estadoDeFila,
  filaVacia,
  filasIniciales,
  parteDe,
  sembrarDocumentoInicial,
  type FilaActor,
} from "@/lib/tramite/actores";

const MENSAJE_VALIDANDO = "Validando tu identidad…";
const MENSAJE_NO_DUENO = "No puedes realizar este trámite porque no eres el propietario del vehículo.";
const COLOR_OK = "#8CC63F";
const COLOR_ERROR = "#C10000";

const ROL_VENDEDOR: ActorRol = {
  rol: "vendedor",
  etiqueta: "Propietario (vendedor)",
  min: 1,
  max: 1,
  biometria: true,
  firma: false,
  personaNaturalPermitida: true,
};

const ROL_COMPRADOR: ActorRol = {
  rol: "comprador",
  etiqueta: "Comprador",
  min: 1,
  max: 1,
  biometria: true,
  firma: false,
  personaNaturalPermitida: true,
};

const ROLES_PUNTO = [ROL_VENDEDOR, ROL_COMPRADOR];

type Fase =
  | { tipo: "cargando"; mensaje: string }
  | { tipo: "no_dueno"; mensaje: string }
  | { tipo: "error"; mensaje: string }
  | {
      tipo: "listo";
      solicitudId: string;
      version: number;
      estado: EstadoIdentidad;
      capturaUrlVendedor?: string | null;
      invitacion?: ResultadoParte | null;
    };

function sesionActoresReutilizable(estado: EstadoIdentidad | null | undefined): boolean {
  return Boolean(estado?.solicitudId && estado.partes.length > 0);
}

function radicanteYaAprobado(estado: EstadoIdentidad): boolean {
  return estado.partes.some((p) => p.parte === "vendedor" && p.estado === "aprobada");
}

function filasDesdeSolicitud(solicitud: Solicitud, documentoInicial: string): FilaActor[] {
  const definicion = { actores: ROLES_PUNTO } as DefinicionTipo;
  const base = filasIniciales(definicion, solicitud.actores);
  const conMinimos = ROLES_PUNTO.flatMap((rol) => {
    const delRol = base.filter((f) => f.rol === rol.rol);
    if (delRol.length > 0) return delRol;
    return [filaVacia(rol.rol, 1)];
  });
  return sembrarDocumentoInicial(conMinimos, { tipo: "CC", numero: documentoInicial });
}

export function ActoresPunto({
  cliente,
  placa,
  documento,
  tipoCodigo,
  onAtras,
  onContinuarRequisitos,
  onSolicitudLista,
  onAviso,
}: {
  cliente: ClientePortal;
  placa: string;
  documento: string;
  tipoCodigo: string;
  onAtras: () => void;
  onContinuarRequisitos: () => void;
  /** Expone el borrador PN abierto para Requisitos (adjuntos/comercial). */
  onSolicitudLista?: (solicitudId: string) => void;
  onAviso: (titulo: string, mensaje: string, tono?: TonoAviso) => void;
}) {
  const [fase, setFase] = useState<Fase>({ tipo: "cargando", mensaje: MENSAJE_VALIDANDO });
  const [filas, setFilas] = useState<FilaActor[]>([]);
  const [intentoAvance, setIntentoAvance] = useState(0);
  const [copiado, setCopiado] = useState(false);
  const [versionEstado, setVersionEstado] = useState(0);
  const vivo = useRef(true);
  const generacion = useRef(0);
  const sondeo = useRef<ReturnType<typeof setInterval> | null>(null);
  const versionRef = useRef(0);

  const detenerSondeo = useCallback(() => {
    if (sondeo.current) {
      clearInterval(sondeo.current);
      sondeo.current = null;
    }
  }, []);

  const refrescarEstado = useCallback(
    async (solicitudId: string, gen?: number) => {
      try {
        const estado = await cliente.punto.actoresIdentidadEstado();
        if (!vivo.current || (gen != null && gen !== generacion.current)) return;
        setFase((prev) => {
          if (prev.tipo !== "listo") return prev;
          return { ...prev, estado };
        });
        setVersionEstado((n) => n + 1);
        if (radicanteYaAprobado(estado)) detenerSondeo();
      } catch {
        /* el sondeo reintenta */
      }
    },
    [cliente, detenerSondeo],
  );

  const aplicarSolicitud = useCallback(
    (solicitud: Solicitud, estado: EstadoIdentidad, capturaUrlVendedor?: string | null, invitacion?: ResultadoParte | null) => {
      onSolicitudLista?.(solicitud.solicitudId);
      versionRef.current = solicitud.version;
      setFilas(filasDesdeSolicitud(solicitud, documento));
      setFase({
        tipo: "listo",
        solicitudId: solicitud.solicitudId,
        version: solicitud.version,
        estado,
        capturaUrlVendedor: capturaUrlVendedor ?? null,
        invitacion: invitacion ?? null,
      });
    },
    [documento, onSolicitudLista],
  );

  useEffect(() => {
    vivo.current = true;
    const gen = ++generacion.current;
    const timer = window.setTimeout(() => {
      void (async () => {
        setFase({ tipo: "cargando", mensaje: MENSAJE_VALIDANDO });
        try {
          try {
            const existente = await cliente.punto.actoresIdentidadEstado();
            if (!vivo.current || gen !== generacion.current) return;
            if (sesionActoresReutilizable(existente)) {
              const solicitud = await cliente.solicitudes.obtener(existente.solicitudId);
              if (!vivo.current || gen !== generacion.current) return;
              aplicarSolicitud(solicitud, existente);
              detenerSondeo();
              if (!radicanteYaAprobado(existente)) {
                sondeo.current = setInterval(() => {
                  void refrescarEstado(existente.solicitudId, gen);
                }, 3000);
              }
              return;
            }
          } catch {
            /* sin sesión → apertura normal */
          }

          const abierto = await cliente.punto.actoresAbrir({ placa, documento, tipoCodigo });
          if (!vivo.current || gen !== generacion.current) return;
          if (!abierto.puedeSerRadicante) {
            setFase({ tipo: "no_dueno", mensaje: abierto.message?.trim() || MENSAJE_NO_DUENO });
            return;
          }

          const solicitud = await cliente.solicitudes.obtener(abierto.solicitudId);
          if (!vivo.current || gen !== generacion.current) return;
          const estado = await cliente.punto.actoresIdentidadEstado();
          if (!vivo.current || gen !== generacion.current) return;
          aplicarSolicitud(solicitud, estado);
          detenerSondeo();
          if (!radicanteYaAprobado(estado)) {
            sondeo.current = setInterval(() => {
              void refrescarEstado(abierto.solicitudId, gen);
            }, 3000);
          }
        } catch (err) {
          if (!vivo.current || gen !== generacion.current) return;
          const mensaje = mensajeDeFallo(err);
          setFase({ tipo: "error", mensaje });
          onAviso("No pudimos continuar", mensaje, "alerta");
        }
      })();
    }, 0);
    return () => {
      window.clearTimeout(timer);
      vivo.current = false;
      detenerSondeo();
    };
  }, [cliente, placa, documento, tipoCodigo, onAviso, detenerSondeo, refrescarEstado, aplicarSolicitud]);

  const radicanteOk = fase.tipo === "listo" && radicanteYaAprobado(fase.estado);
  const puedeContinuar = radicanteOk;

  const cambiarFila = (clave: string, patch: Partial<FilaActor>) => {
    setFilas((actuales) => actuales.map((f) => (f.clave === clave ? { ...f, ...patch } : f)));
  };

  const guardarRol = async (rol: string) => {
    if (fase.tipo !== "listo") return;
    const delRol = filas.filter((f) => f.rol === rol && f.consultado);
    if (delRol.length === 0) return;
    const actualizada = await cliente.solicitudes.guardarActores(fase.solicitudId, {
      version: versionRef.current,
      actores: actoresParaGuardar(delRol),
    });
    versionRef.current = actualizada.version;
    setFase((prev) => (prev.tipo === "listo" ? { ...prev, version: actualizada.version } : prev));
  };

  const validarVendedor = async (): Promise<{ estado: string; capturaUrl?: string | null }> => {
    if (fase.tipo !== "listo") throw new Error("Sesión no lista");
    const fila = filas.find((f) => f.rol === "vendedor" && f.ordinal === 1);
    if (!fila) throw new Error("Falta el vendedor");
    const resultado = await cliente.punto.actoresIdentidadRadicante({
      solicitudId: fase.solicitudId,
      parte: "vendedor",
      ordinal: 1,
      tipoDocumento: fila.tipoDocumento,
      documento: fila.numeroDocumento,
      nombre: fila.nombre,
      correo: fila.correo,
      celular: fila.celular,
      direccion: fila.direccion,
      ciudad: fila.ciudad,
    });
    setFase((prev) =>
      prev.tipo === "listo" ? { ...prev, capturaUrlVendedor: resultado.capturaUrl ?? prev.capturaUrlVendedor } : prev,
    );
    await refrescarEstado(fase.solicitudId);
    return resultado;
  };

  const validarComprador = async (): Promise<{ estado: string; capturaUrl?: string | null }> => {
    if (fase.tipo !== "listo") throw new Error("Sesión no lista");
    if (!radicanteYaAprobado(fase.estado)) {
      onAviso("Identifica al vendedor primero", "Primero debe quedar identificado quien radica en el punto.", "alerta");
      throw new Error("invitaciones_bloqueadas");
    }
    const fila = filas.find((f) => f.rol === "comprador" && f.ordinal === 1);
    if (!fila) throw new Error("Falta el comprador");
    const resultado = await cliente.punto.actoresInvitar({
      parte: "comprador",
      ordinal: 1,
      tipoDocumento: fila.tipoDocumento,
      documento: fila.numeroDocumento,
      nombre: fila.nombre,
      correo: fila.correo,
      celular: fila.celular,
      direccion: fila.direccion,
      ciudad: fila.ciudad,
    });
    setCopiado(false);
    setFase((prev) => (prev.tipo === "listo" ? { ...prev, invitacion: resultado } : prev));
    await refrescarEstado(fase.solicitudId);
    onAviso("Correo enviado", "Se envió el enlace de validación al correo indicado. También puedes copiarlo.", "exito");
    return resultado;
  };

  async function copiarEnlace(url: string) {
    try {
      await navigator.clipboard.writeText(url);
      setCopiado(true);
    } catch {
      onAviso("No se pudo copiar", "Copia el enlace manualmente desde la barra de direcciones del validador.", "alerta");
    }
  }

  const estadoDe = (f: FilaActor): EstadoValidacion =>
    fase.tipo === "listo" ? estadoDeFila(f, fase.estado, []) : "sin_solicitar";

  const parteDeFila = (f: FilaActor): ParteIdentidad | undefined =>
    fase.tipo === "listo" ? parteDe(f, fase.estado) : undefined;

  return (
    <div className="space-y-6" id="punto-actores">
      {fase.tipo === "cargando" && (
        <Card title="Actores" id="punto-actores-carga">
          <MobilityLoader mensaje={fase.mensaje} />
        </Card>
      )}

      {fase.tipo === "no_dueno" && (
        <Card title="Actores" id="punto-actores-no-dueno">
          <div role="alert" className="flex gap-3 rounded-[9px] p-4" style={{ backgroundColor: `${COLOR_ERROR}18` }}>
            <AlertTriangle aria-hidden className="h-6 w-6 shrink-0" style={{ color: COLOR_ERROR }} />
            <p className="font-medium" style={{ color: COLOR_ERROR }}>
              {fase.mensaje}
            </p>
          </div>
          <Nav onBack={onAtras} onNext={onAtras} nextLabel="Volver al diagnóstico" />
        </Card>
      )}

      {fase.tipo === "error" && (
        <Card title="Actores" id="punto-actores-error">
          <div role="alert" className="flex flex-col gap-3 rounded-[9px] border border-alert bg-alert/10 p-4 text-alert">
            <p className="flex gap-2">
              <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
              <span>{fase.mensaje}</span>
            </p>
            <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={() => window.location.reload()}>
              Reintentar
            </button>
          </div>
          <Nav onBack={onAtras} onNext={onAtras} nextLabel="Volver" />
        </Card>
      )}

      {fase.tipo === "listo" && (
        <>
          {radicanteOk && (
            <div
              className="flex gap-3 rounded-[9px] p-4"
              style={{ backgroundColor: `${COLOR_OK}18` }}
              role="status"
              id="punto-actores-dueno-ok"
            >
              <Check aria-hidden className="mt-0.5 h-5 w-5 shrink-0" style={{ color: COLOR_OK }} />
              <div className="space-y-1 text-sm">
                <p className="font-semibold" style={{ color: COLOR_OK }}>
                  Identidad validada · propietario del vehículo
                </p>
                <p className="text-muted-foreground">
                  Quien radica en el punto quedó identificado y coincide con el propietario RUNT de la placa{" "}
                  <span className="font-medium text-foreground">{placa}</span> (documento{" "}
                  <span className="font-medium text-foreground">{documento}</span>). Completa o invita al comprador, o
                  continúa.
                </p>
              </div>
            </div>
          )}

          {!radicanteOk && fase.capturaUrlVendedor && (
            <p className="text-sm text-muted-foreground">
              Completa la validación en la ventana abierta.{" "}
              <a className="text-primary underline" href={fase.capturaUrlVendedor} target="_blank" rel="noreferrer">
                Abrir de nuevo
              </a>
            </p>
          )}

          <Card title={ROL_VENDEDOR.etiqueta} id="punto-actores-vendedor">
            {filas
              .filter((f) => f.rol === "vendedor")
              .map((fila) => (
                <PersonaActor
                  key={fila.clave}
                  fila={fila}
                  rol={ROL_VENDEDOR}
                  esPropietario
                  solicitudId={fase.solicitudId}
                  cliente={cliente}
                  mostrarErrores={intentoAvance > 0}
                  estado={estadoDe(fila)}
                  parte={parteDeFila(fila)}
                  versionEstado={versionEstado}
                  onCambio={(patch) => cambiarFila(fila.clave, patch)}
                  guardarRol={() => guardarRol("vendedor")}
                  refrescarIdentidad={() => void refrescarEstado(fase.solicitudId)}
                  reportarError={(err) => onAviso("No pudimos continuar", mensajeDeFallo(err), "alerta")}
                  onValidarIdentidad={validarVendedor}
                  textoBotonValidar="Validar identidad en el punto"
                />
              ))}
          </Card>

          <Card title={ROL_COMPRADOR.etiqueta} id="punto-actores-comprador">
            {filas
              .filter((f) => f.rol === "comprador")
              .map((fila) => (
                <PersonaActor
                  key={fila.clave}
                  fila={fila}
                  rol={ROL_COMPRADOR}
                  solicitudId={fase.solicitudId}
                  cliente={cliente}
                  mostrarErrores={intentoAvance > 0}
                  estado={estadoDe(fila)}
                  parte={parteDeFila(fila)}
                  versionEstado={versionEstado}
                  onCambio={(patch) => cambiarFila(fila.clave, patch)}
                  guardarRol={() => guardarRol("comprador")}
                  refrescarIdentidad={() => void refrescarEstado(fase.solicitudId)}
                  reportarError={(err) => onAviso("No pudimos invitar", mensajeDeFallo(err), "alerta")}
                  onValidarIdentidad={validarComprador}
                  textoBotonValidar="Enviar validación"
                  puedeValidarIdentidad={radicanteOk}
                />
              ))}
            {fase.invitacion?.capturaUrl && (
              <div className="mt-4">
                <button type="button" className={boton("flit", "min-h-11 px-6", "none")} onClick={() => void copiarEnlace(fase.invitacion!.capturaUrl!)}>
                  <Copy aria-hidden className="mr-2 inline h-4 w-4" />
                  {copiado ? "Copiado" : "Copiar enlace"}
                </button>
              </div>
            )}
          </Card>

          <Nav
            onBack={onAtras}
            onNext={() => {
              if (!puedeContinuar) {
                setIntentoAvance((n) => n + 1);
                return;
              }
              onContinuarRequisitos();
            }}
            nextLabel="Continuar"
            nextDisabled={!puedeContinuar}
            motivo={!puedeContinuar ? "Debes quedar identificado como propietario para continuar." : undefined}
          />
        </>
      )}
    </div>
  );
}
