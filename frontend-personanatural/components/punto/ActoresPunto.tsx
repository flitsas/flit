"use client";

// HU #14238 — Paso Actores del canal OT: mismas fichas por rol que el portal PN (roles de la definición del tipo) + identidad in-situ e invitaciones.
// El primer rol es quien radica en el punto (documento fijo, identidad in-situ); los demás roles son partes invitables.
import { useCallback, useEffect, useRef, useState } from "react";
import { AlertTriangle, Check, Copy } from "lucide-react";
import { MobilityLoader } from "@/components/MobilityLoader";
import type { TonoAviso } from "@/components/landing/AvisoModal";
import { boton } from "@/components/landing/estilos";
import { PersonaActor } from "@/components/tramite/renderers/PersonaActor";
import { Badge, Card, Nav } from "@/components/tramite/piezas";
import { AvisoModoPruebas } from "@/components/tramite/AvisoModoPruebas";
import { useOmitirIdentidad } from "@/components/landing/useConfiguracion";
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
import { ApiError, mensajeDeFallo } from "@/lib/api/errores";
import { correoSeguro } from "@/lib/punto/contacto";
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

/** Roles de la lista que devuelve /punto/actores/abrir: { rol, etiqueta, esRadicante, minimo, maximo }. */
interface RolAbrirPunto {
  rol: string;
  etiqueta: string;
  esRadicante?: boolean;
  minimo?: number;
  maximo?: number;
}

/** El primer rol (o el marcado esRadicante) es quien radica en el punto; los demás se invitan. */
function rolesDeAbrir(roles: readonly RolAbrirPunto[], definicion: readonly ActorRol[]): ActorRol[] {
  const base = roles.map((r): ActorRol => {
    const def = definicion.find((d) => d.rol === r.rol);
    return {
      rol: r.rol,
      etiqueta: r.etiqueta || def?.etiqueta || r.rol,
      min: r.minimo ?? def?.min ?? 1,
      max: r.maximo ?? def?.max ?? 1,
      biometria: def?.biometria ?? true,
      firma: def?.firma ?? false,
      personaNaturalPermitida: def?.personaNaturalPermitida ?? true,
    };
  });
  const iRad = roles.findIndex((r) => r.esRadicante);
  if (iRad > 0) base.unshift(base.splice(iRad, 1)[0]);
  return base;
}

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
      invitaciones: Record<string, ResultadoParte>;
    };

function sesionActoresReutilizable(estado: EstadoIdentidad | null | undefined): boolean {
  return Boolean(estado?.solicitudId && estado.partes.length > 0);
}

function radicanteYaAprobado(estado: EstadoIdentidad, rolRadicante: string): boolean {
  return estado.partes.some((p) => p.parte === rolRadicante && p.estado === "aprobada");
}

function filasDesdeSolicitud(solicitud: Solicitud, documentoInicial: string, roles: readonly ActorRol[]): FilaActor[] {
  const definicion = { actores: roles } as DefinicionTipo;
  const base = filasIniciales(definicion, solicitud.actores);
  const conMinimos = roles.flatMap((rol) => {
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
  const omitirIdentidad = useOmitirIdentidad(cliente);
  const [fase, setFase] = useState<Fase>({ tipo: "cargando", mensaje: MENSAJE_VALIDANDO });
  const [filas, setFilas] = useState<FilaActor[]>([]);
  const [roles, setRoles] = useState<ActorRol[]>([]);
  const [intentoAvance, setIntentoAvance] = useState(0);
  const [copiado, setCopiado] = useState<string | null>(null);
  const [versionEstado, setVersionEstado] = useState(0);
  const [lanzando, setLanzando] = useState(false);
  const [falloIdentidad, setFalloIdentidad] = useState<string | null>(null);
  const [esperandoIdentidad, setEsperandoIdentidad] = useState(false);
  const lanzado = useRef(false);
  const onAvisoRef = useRef(onAviso);
  const onSolicitudListaRef = useRef(onSolicitudLista);
  useEffect(() => {
    onAvisoRef.current = onAviso;
    onSolicitudListaRef.current = onSolicitudLista;
  });
  const vivo = useRef(true);
  const generacion = useRef(0);
  const sondeo = useRef<ReturnType<typeof setInterval> | null>(null);
  const versionRef = useRef(0);
  const rolesRef = useRef<ActorRol[]>([]);
  const rolRadicanteRef = useRef("");

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
        if (radicanteYaAprobado(estado, rolRadicanteRef.current)) detenerSondeo();
      } catch {
        /* el sondeo reintenta */
      }
    },
    [cliente, detenerSondeo],
  );

  const aplicarSolicitud = useCallback(
    (solicitud: Solicitud, estado: EstadoIdentidad, capturaUrlVendedor?: string | null) => {
      onSolicitudListaRef.current?.(solicitud.solicitudId);
      versionRef.current = solicitud.version;
      setFilas(filasDesdeSolicitud(solicitud, documento, rolesRef.current));
      setFase({
        tipo: "listo",
        solicitudId: solicitud.solicitudId,
        version: solicitud.version,
        estado,
        capturaUrlVendedor: capturaUrlVendedor ?? null,
        invitaciones: {},
      });
    },
    [documento],
  );

  useEffect(() => {
    vivo.current = true;
    const gen = ++generacion.current;
    const timer = window.setTimeout(() => {
      void (async () => {
        setFase({ tipo: "cargando", mensaje: MENSAJE_VALIDANDO });
        try {
          // Los roles salen de la definición del tipo (misma fuente que el portal web).
          const def = await cliente.catalogos.definicion(tipoCodigo);
          if (!vivo.current || gen !== generacion.current) return;
          const delTipo = def.actores.map((a) => ({ rol: a.rol, etiqueta: a.etiqueta, minimo: a.min, maximo: a.max }));
          const lista = rolesDeAbrir(delTipo, def.actores);
          if (lista.length === 0) throw new Error("El tipo de trámite no define actores.");
          rolesRef.current = lista;
          rolRadicanteRef.current = lista[0].rol;
          setRoles(lista);

          try {
            const existente = await cliente.punto.actoresIdentidadEstado();
            if (!vivo.current || gen !== generacion.current) return;
            if (sesionActoresReutilizable(existente)) {
              const solicitud = await cliente.solicitudes.obtener(existente.solicitudId);
              if (!vivo.current || gen !== generacion.current) return;
              aplicarSolicitud(solicitud, existente);
              detenerSondeo();
              if (!radicanteYaAprobado(existente, rolRadicanteRef.current)) {
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
          if (abierto.roles && abierto.roles.length > 0) {
            const delBack = rolesDeAbrir(abierto.roles, rolesRef.current);
            rolesRef.current = delBack;
            rolRadicanteRef.current = delBack[0].rol;
            setRoles(delBack);
          }

          const solicitud = await cliente.solicitudes.obtener(abierto.solicitudId);
          if (!vivo.current || gen !== generacion.current) return;
          const estado = await cliente.punto.actoresIdentidadEstado();
          if (!vivo.current || gen !== generacion.current) return;
          aplicarSolicitud(solicitud, estado);
          detenerSondeo();
          if (!radicanteYaAprobado(estado, rolRadicanteRef.current)) {
            sondeo.current = setInterval(() => {
              void refrescarEstado(abierto.solicitudId, gen);
            }, 3000);
          }
        } catch (err) {
          if (!vivo.current || gen !== generacion.current) return;
          const mensaje = mensajeDeFallo(err);
          setFase({ tipo: "error", mensaje });
          onAvisoRef.current("No pudimos continuar", mensaje, "alerta");
        }
      })();
    }, 0);
    return () => {
      window.clearTimeout(timer);
      vivo.current = false;
      detenerSondeo();
    };
  }, [cliente, placa, documento, tipoCodigo, detenerSondeo, refrescarEstado, aplicarSolicitud]);

  const rolRadicante: ActorRol | undefined = roles[0];
  const invitables = roles.slice(1);
  const radicanteOk = fase.tipo === "listo" && Boolean(rolRadicante) && radicanteYaAprobado(fase.estado, rolRadicante!.rol);
  // Modo de pruebas: la identidad no bloquea el avance ni a las demás partes (la identidad se sigue lanzando sola).
  const identidadCumplida = radicanteOk || omitirIdentidad;
  const puedeContinuar = identidadCumplida;

  const cambiarFila = (clave: string, patch: Partial<FilaActor>) => {
    setFilas((actuales) => actuales.map((f) => (f.clave === clave ? { ...f, ...patch } : f)));
  };

  const guardarRol = async (rol: string) => {
    if (fase.tipo !== "listo") return;
    const delRol = filas.filter((f) => f.rol === rol && f.consultado);
    if (delRol.length === 0) return;
    const actualizada = await cliente.solicitudes.guardarActores(fase.solicitudId, {
      version: versionRef.current,
      // Un correo con caracteres no permitidos (dato de origen dañado) no se guarda.
      actores: actoresParaGuardar(delRol).map((a) => ({ ...a, correo: correoSeguro(a.correo) ?? "" })),
    });
    versionRef.current = actualizada.version;
    setFase((prev) => (prev.tipo === "listo" ? { ...prev, version: actualizada.version } : prev));
  };

  const validarRadicante = async (): Promise<{ estado: string; capturaUrl?: string | null }> => {
    if (fase.tipo !== "listo" || !rolRadicante) throw new Error("Sesión no lista");
    const fila = filas.find((f) => f.rol === rolRadicante.rol && f.ordinal === 1);
    if (!fila) throw new Error("Falta quien radica");
    const resultado = await cliente.punto.actoresIdentidadRadicante({
      solicitudId: fase.solicitudId,
      parte: rolRadicante.rol,
      ordinal: 1,
      tipoDocumento: fila.tipoDocumento,
      documento: fila.numeroDocumento,
      nombre: fila.nombre || undefined,
      correo: correoSeguro(fila.correo),
      celular: fila.celular || undefined,
      direccion: fila.direccion || undefined,
      ciudad: fila.ciudad || undefined,
    });
    setFase((prev) =>
      prev.tipo === "listo" ? { ...prev, capturaUrlVendedor: resultado.capturaUrl ?? prev.capturaUrlVendedor } : prev,
    );
    await refrescarEstado(fase.solicitudId);
    return resultado;
  };

  // La identidad de quien radica se lanza sola al entrar (in-situ, sin enlace ni botón de envío).
  const lanzarIdentidad = async () => {
    setLanzando(true);
    setFalloIdentidad(null);
    setEsperandoIdentidad(false);
    try {
      const resultado = await validarRadicante();
      setEsperandoIdentidad(true);
      // Sin gesto del usuario el navegador puede bloquear la ventana: queda el botón «Abrir validación».
      if (resultado.capturaUrl) window.open(resultado.capturaUrl, "_blank", "noopener,noreferrer");
    } catch (err) {
      if (err instanceof ApiError && err.code === "no_es_propietario") {
        setFase({ tipo: "no_dueno", mensaje: mensajeDeFallo(err) });
      } else {
        const mensaje = mensajeDeFallo(err);
        setFalloIdentidad(mensaje);
        onAviso("No pudimos validar tu identidad", mensaje, "alerta");
      }
    } finally {
      setLanzando(false);
    }
  };

  const parteRadicante = fase.tipo === "listo" && rolRadicante ? fase.estado.partes.find((p) => p.parte === rolRadicante.rol) : undefined;
  const estadoRadicante = parteRadicante?.estado;
  const hayFila = Boolean(rolRadicante && filas.some((f) => f.rol === rolRadicante.rol && f.ordinal === 1));
  const solicitudListaId = fase.tipo === "listo" ? fase.solicitudId : null;
  const lanzar = useRef(lanzarIdentidad);
  useEffect(() => {
    lanzar.current = lanzarIdentidad;
  });
  useEffect(() => {
    if (!solicitudListaId || !hayFila || radicanteOk) return;
    if (estadoRadicante && estadoRadicante !== "sin_solicitar") return;
    if (lanzado.current) return;
    lanzado.current = true;
    void lanzar.current();
  }, [solicitudListaId, hayFila, radicanteOk, estadoRadicante]);
  const falloVisible =
    falloIdentidad ??
    (estadoRadicante === "rechazada" || estadoRadicante === "vencida"
      ? `Tu identidad no quedó validada (${estadoRadicante === "vencida" ? "venció" : "fue rechazada"}). Vuelve a intentarlo.`
      : null);
  const enCursoIdentidad = !radicanteOk && !falloVisible && !lanzando && (esperandoIdentidad || estadoRadicante === "pendiente" || estadoRadicante === "pendiente_envio");

  const validarInvitado = async (rol: ActorRol): Promise<{ estado: string; capturaUrl?: string | null }> => {
    if (fase.tipo !== "listo" || !rolRadicante) throw new Error("Sesión no lista");
    if (!identidadCumplida) {
      onAviso(`Identifica a ${rolRadicante.etiqueta} primero`, "Primero debe quedar identificado quien radica en el punto.", "alerta");
      throw new Error("invitaciones_bloqueadas");
    }
    const fila = filas.find((f) => f.rol === rol.rol && f.ordinal === 1);
    if (!fila) throw new Error(`Falta ${rol.etiqueta}`);
    const resultado = await cliente.punto.actoresInvitar({
      parte: rol.rol,
      ordinal: 1,
      tipoDocumento: fila.tipoDocumento,
      documento: fila.numeroDocumento,
      nombre: fila.nombre,
      correo: fila.correo,
      celular: fila.celular,
      direccion: fila.direccion,
      ciudad: fila.ciudad,
    });
    setCopiado(null);
    setFase((prev) =>
      prev.tipo === "listo" ? { ...prev, invitaciones: { ...prev.invitaciones, [rol.rol]: resultado } } : prev,
    );
    await refrescarEstado(fase.solicitudId);
    onAviso("Correo enviado", "Se envió el enlace de validación al correo indicado. También puedes copiarlo.", "exito");
    return resultado;
  };

  async function copiarEnlace(rol: string, url: string) {
    try {
      await navigator.clipboard.writeText(url);
      setCopiado(rol);
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
      {(fase.tipo === "cargando" || lanzando) && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm" id="punto-actores-carga">
          <MobilityLoader mensaje={MENSAJE_VALIDANDO} />
        </div>
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
          {omitirIdentidad && <AvisoModoPruebas id="punto-actores-modo-pruebas" />}
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
                  <span className="font-medium text-foreground">{documento}</span>).{" "}
                  {invitables.length > 0 ? "Completa o invita a las demás partes, o continúa." : "Continúa al siguiente paso."}
                </p>
              </div>
            </div>
          )}

          {rolRadicante && (
            <Card title={rolRadicante.etiqueta} id={`punto-actores-${rolRadicante.rol}`}>
              {filas
                .filter((f) => f.rol === rolRadicante.rol)
                .map((fila) => (
                  <PersonaActor
                    key={fila.clave}
                    fila={fila}
                    rol={rolRadicante}
                    esPropietario
                    documentoFijo={Boolean(documento)}
                    solicitudId={fase.solicitudId}
                    cliente={cliente}
                    mostrarErrores={intentoAvance > 0}
                    estado={estadoDe(fila)}
                    parte={parteDeFila(fila)}
                    versionEstado={versionEstado}
                    onCambio={(patch) => cambiarFila(fila.clave, patch)}
                    guardarRol={() => guardarRol(rolRadicante.rol)}
                    refrescarIdentidad={() => void refrescarEstado(fase.solicitudId)}
                    reportarError={(err) => onAviso("No pudimos continuar", mensajeDeFallo(err), "alerta")}
                    radicantePunto
                    onValidarIdentidad={validarRadicante}
                  />
                ))}
              {radicanteOk && (
                <div className="mt-4">
                  <Badge tone="success">Identificado</Badge>
                </div>
              )}
              {!radicanteOk && (
                <div className="mt-4 space-y-3" id="punto-actores-estado-identidad">
                  {enCursoIdentidad && (
                    <div className="flex flex-wrap items-center gap-3" role="status">
                      <Badge tone="warning">Validación: en curso</Badge>
                      <span className="text-sm text-muted-foreground">Completa la validación en la ventana que se abrió.</span>
                      {fase.capturaUrlVendedor && (
                        <a className={boton("flit", "min-h-11 px-6", "none")} href={fase.capturaUrlVendedor} target="_blank" rel="noreferrer">
                          Abrir validación
                        </a>
                      )}
                    </div>
                  )}
                  {falloVisible && (
                    <div role="alert" className="flex flex-col gap-3 rounded-[9px] p-4" style={{ backgroundColor: `${COLOR_ERROR}18` }} id="punto-actores-identidad-fallo">
                      <p className="flex gap-2 font-medium" style={{ color: COLOR_ERROR }}>
                        <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
                        <span>{falloVisible}</span>
                      </p>
                      <button type="button" className={boton("flit", "min-h-11 px-6", "none")} disabled={lanzando} onClick={() => void lanzarIdentidad()}>
                        Reintentar
                      </button>
                    </div>
                  )}
                </div>
              )}
            </Card>
          )}

          {identidadCumplida && invitables.map((rolInv) => {
            const invitacion = fase.invitaciones[rolInv.rol];
            return (
              <Card key={rolInv.rol} title={rolInv.etiqueta} id={`punto-actores-${rolInv.rol}`}>
                {filas
                  .filter((f) => f.rol === rolInv.rol)
                  .map((fila) => (
                    <PersonaActor
                      key={fila.clave}
                      fila={fila}
                      rol={rolInv}
                      solicitudId={fase.solicitudId}
                      cliente={cliente}
                      mostrarErrores={intentoAvance > 0}
                      estado={estadoDe(fila)}
                      parte={parteDeFila(fila)}
                      versionEstado={versionEstado}
                      onCambio={(patch) => cambiarFila(fila.clave, patch)}
                      guardarRol={() => guardarRol(rolInv.rol)}
                      refrescarIdentidad={() => void refrescarEstado(fase.solicitudId)}
                      reportarError={(err) => onAviso("No pudimos invitar", mensajeDeFallo(err), "alerta")}
                      onValidarIdentidad={() => validarInvitado(rolInv)}
                      textoBotonValidar="Enviar validación"
                      puedeValidarIdentidad={identidadCumplida}
                    />
                  ))}
                {invitacion?.capturaUrl && (
                  <div className="mt-4">
                    <button
                      type="button"
                      className={boton("flit", "min-h-11 px-6", "none")}
                      onClick={() => void copiarEnlace(rolInv.rol, invitacion.capturaUrl!)}
                    >
                      <Copy aria-hidden className="mr-2 inline h-4 w-4" />
                      {copiado === rolInv.rol ? "Copiado" : "Copiar enlace"}
                    </button>
                  </div>
                )}
              </Card>
            );
          })}

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
