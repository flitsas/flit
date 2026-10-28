"use client";

import { useMutation } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { AddressField } from "@/components/tramite/AddressField";
import { Badge, Btn, Field } from "@/components/tramite/piezas";
import type { ActorRol, ClientePortal, EstadoValidacion, ParteIdentidad, PersonaDetalle } from "@/lib/api/cliente";
import { ApiError, esperaDe, mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import {
  enCurso,
  enmascararCorreo,
  enmascararNombre,
  ETIQUETA_VALIDACION,
  MENSAJE_CONSULTAR,
  soloDigitos,
  TIPOS_DOCUMENTO,
  TONO_VALIDACION,
  validarFila,
  type CampoActor,
  type ErroresActor,
  type FilaActor,
  type TipoDocumento,
} from "@/lib/tramite/actores";
import { consentimientoDeConsulta } from "@/lib/tramite/diagnostico";
import { formatearFecha } from "@/lib/formato";

interface Props {
  fila: FilaActor;
  rol: ActorRol;
  /** Título de la persona dentro del rol cuando el rol admite varias («Copropietario 2»); `undefined` con una sola. */
  titulo?: string;
  solicitudId: string;
  cliente: ClientePortal;
  /** Cédula con la que se abrió el trámite: esa persona ya se identificó y no necesita consulta. */
  cedulaSolicitante?: string;
  /** El ciudadano intentó avanzar con el paso incompleto: se muestran todos los errores. */
  mostrarErrores: boolean;
  erroresServidor?: ErroresActor | undefined;
  estado: EstadoValidacion;
  parte?: ParteIdentidad | undefined;
  /** Cambia cada vez que llega un estado nuevo de la identidad (para reiniciar la espera que informa el back). */
  versionEstado: number;
  /** Muestra el ícono de ayuda de la dirección de entrega (cuando la definición lo marque). */
  direccionEntrega?: boolean;
  onCambio: (patch: Partial<FilaActor>) => void;
  onQuitar?: (() => void) | undefined;
  /** Guarda a todas las personas del rol en el back (el actor debe existir allá antes de pedir su validación). */
  guardarRol: () => Promise<void>;
  /** Vuelve a leer el estado de la identidad. */
  refrescarIdentidad: () => void;
  reportarError: (error: unknown) => void;
}

const SEGUNDOS_ENTRE_REENVIOS = 60;
const AVISO_ENLACE_MS = 8000;

/** Una persona (actor) dentro de la tarjeta de su rol: documento y consulta, contacto, dirección y validación de identidad. */
export function PersonaActor({
  fila,
  rol,
  titulo,
  solicitudId,
  cliente,
  cedulaSolicitante,
  mostrarErrores,
  erroresServidor,
  estado,
  parte,
  versionEstado,
  direccionEntrega = false,
  onCambio,
  onQuitar,
  guardarRol,
  refrescarIdentidad,
  reportarError,
}: Props) {
  const [tocados, setTocados] = useState<Partial<Record<CampoActor, boolean>>>({});
  const [errorConsulta, setErrorConsulta] = useState<string | null>(null);
  /** Aviso bajo los campos cuando el nombre se escribe a mano porque la consulta no lo trajo (no es un error). */
  const [avisoManual, setAvisoManual] = useState<string | null>(null);
  const [aviso, setAviso] = useState<{ tipo: "ok" | "error"; texto: string } | null>(null);
  const [ocupado, setOcupado] = useState(false);
  const bloqueoConsulta = useBloqueo();
  const esperaReenvio = useBloqueo();
  const idCampo = (n: string) => `${fila.clave}-${n}`;

  const errores = validarFila(fila, rol);
  const juridica = !rol.personaNaturalPermitida || fila.tipoDocumento === "NIT";
  const error = (campo: CampoActor): string | undefined => {
    const delServidor = erroresServidor?.[campo];
    if (delServidor) return delServidor;
    const e = errores[campo];
    if (!e) return undefined;
    if (campo === "numeroDocumento") {
      if (juridica) return e; // el aviso de persona jurídica sale de inmediato
      if (e === MENSAJE_CONSULTAR) return mostrarErrores ? e : undefined;
    }
    return mostrarErrores || tocados[campo] ? e : undefined;
  };
  const tocar = (campo: CampoActor) => setTocados((t) => ({ ...t, [campo]: true }));

  // ── Consulta del documento ──────────────────────────────────────────────────────────────────────────────────────────

  // La consulta lleva el rol de la tarjeta y la sesión de la solicitud (sin captcha); devuelve los nombres para rellenar al actor.
  const consulta = useMutation<PersonaDetalle, unknown, void>({
    mutationFn: () =>
      cliente.consultas.personaDetalle(solicitudId, { tipoDocumento: fila.tipoDocumento, documento: fila.numeroDocumento, rol: fila.rol, ...consentimientoDeConsulta }),
    onSuccess: (r) => {
      setErrorConsulta(null);
      const nombre = (r.existe ? (r.nombreCompleto ?? [r.nombres, r.apellidos].filter(Boolean).join(" ")) : "").trim();
      if (nombre) {
        setAvisoManual(null);
        onCambio({ consultado: true, nombreConsultado: true, nombre, licencia: null });
      } else {
        setAvisoManual("No encontramos los datos de esta persona; escribe su nombre manualmente.");
        onCambio({ consultado: true, nombreConsultado: false, nombre: "", licencia: null });
      }
    },
    onError: (e) => {
      reportarError(e);
      if (e instanceof ApiError && e.code === "detalle_agotado") {
        // Sin consultas disponibles: se sigue a mano, como cuando no hay coincidencia.
        setAvisoManual(mensajeDe(e));
        onCambio({ consultado: true, nombreConsultado: false, nombre: "", licencia: null });
        return;
      }
      bloqueoConsulta.registrar(e);
      setErrorConsulta(mensajeDe(e));
    },
  });

  const alConsultar = () => {
    tocar("numeroDocumento");
    setErrorConsulta(null);
    // Persona jurídica o un número mal formado: el error ya está a la vista y no hay nada que consultar.
    if (errores.numeroDocumento && errores.numeroDocumento !== MENSAJE_CONSULTAR) return;
    // La persona que abrió el trámite ya se identificó (captcha y consentimiento al iniciar): no se vuelve a consultar.
    if (cedulaSolicitante && fila.tipoDocumento === "CC" && fila.numeroDocumento === cedulaSolicitante) {
      onCambio({ consultado: true });
      return;
    }
    consulta.mutate();
  };

  // ── Validación de identidad ─────────────────────────────────────────────────────────────────────────────────────────

  // La espera entre reenvíos que informa el back se toma una sola vez por cada estado nuevo que llega.
  const versionAtendida = useRef(-1);
  const iniciarEspera = esperaReenvio.iniciar;
  useEffect(() => {
    if (versionAtendida.current === versionEstado) return;
    versionAtendida.current = versionEstado;
    if (parte && parte.esperaSegundos > 0 && enCurso(parte.estado)) iniciarEspera(parte.esperaSegundos);
  }, [versionEstado, parte, iniciarEspera]);

  const temporizadorAviso = useRef<ReturnType<typeof setTimeout> | null>(null);
  useEffect(
    () => () => {
      if (temporizadorAviso.current) clearTimeout(temporizadorAviso.current);
    },
    [],
  );

  const avisar = (a: { tipo: "ok" | "error"; texto: string }) => {
    setAviso(a);
    if (temporizadorAviso.current) clearTimeout(temporizadorAviso.current);
    if (a.tipo === "ok") temporizadorAviso.current = setTimeout(() => setAviso(null), AVISO_ENLACE_MS);
  };

  const pedirIdentidad = async (accion: "solicitar" | "reenviar") => {
    const e = validarFila(fila, rol);
    if (Object.keys(e).length > 0) {
      setTocados({ numeroDocumento: true, nombre: true, correo: true, celular: true, direccion: true });
      avisar({ tipo: "error", texto: "Completa y corrige los datos de esta persona antes de validar su identidad." });
      return;
    }
    setOcupado(true);
    setAviso(null);
    try {
      await guardarRol();
      const resultado = await cliente.identidad[accion](solicitudId, fila.rol, fila.ordinal);
      refrescarIdentidad();
      if (accion === "reenviar") esperaReenvio.iniciar(SEGUNDOS_ENTRE_REENVIOS);
      avisar(
        resultado.estado === "pendiente_envio"
          ? { tipo: "ok", texto: "Aún no pudimos enviar el enlace: lo intentaremos de nuevo automáticamente." }
          : { tipo: "ok", texto: `Enlace enviado a ${enmascararCorreo(fila.correo)}` },
      );
    } catch (err) {
      reportarError(err);
      refrescarIdentidad();
      if (esperaDe(err)) esperaReenvio.registrar(err);
      avisar({ tipo: "error", texto: textoDeError(err) });
    } finally {
      setOcupado(false);
    }
  };

  // ── Presentación ────────────────────────────────────────────────────────────────────────────────────────────────────

  const identidadAbierta = estado === "aprobada" || enCurso(estado);
  const documentoBloqueado = fila.consultado && identidadAbierta;
  const nombreMostrado = !fila.consultado ? "" : estado === "aprobada" || !documentoBloqueado ? fila.nombre : enmascararNombre(fila.nombre);
  const pideIdentidad = rol.biometria;
  const vence = formatearFecha(fila.licencia?.vigencia);
  const etiquetaLicencia = fila.licencia?.estado ? `Licencia ${fila.licencia.estado.toLowerCase()}${fila.licencia.vigencia ? ` · ${vence ?? fila.licencia.vigencia}` : ""}` : null;
  const reenviosAgotados = parte !== undefined && enCurso(estado) && parte.reenviosRestantes === 0;
  const puedeCambiarDocumento = fila.consultado && !identidadAbierta;

  return (
    <div role={titulo ? "group" : undefined} aria-label={titulo} className="space-y-4">
      {titulo && (
        <div className="flex items-center justify-between gap-3">
          <p className="font-semibold">{titulo}</p>
          {onQuitar && (
            <Btn tone="secondary" onClick={onQuitar} disabled={identidadAbierta}>
              Quitar {titulo}
            </Btn>
          )}
        </div>
      )}
      <div className="grid gap-4 md:grid-cols-3">
        <Field label="Tipo de documento">
          <select
            className="field"
            disabled={fila.consultado}
            value={fila.tipoDocumento}
            onChange={(e) => {
              onCambio({ tipoDocumento: e.target.value as TipoDocumento });
              setErrorConsulta(null);
            }}
          >
            {TIPOS_DOCUMENTO.map((t) => (
              <option key={t.valor} value={t.valor}>
                {t.etiqueta}
              </option>
            ))}
          </select>
        </Field>
        <Field label="Número de documento *" error={error("numeroDocumento") ?? errorConsulta ?? undefined}>
          <div className="relative">
            <Search aria-hidden className="absolute left-3 top-1/2 h-5 w-5 -translate-y-1/2 text-primary" />
            <input
              className="field pl-10"
              inputMode="numeric"
              autoComplete="off"
              placeholder="Consulta el documento"
              disabled={fila.consultado}
              aria-invalid={error("numeroDocumento") || errorConsulta ? true : undefined}
              value={fila.numeroDocumento}
              onChange={(e) => {
                onCambio({ numeroDocumento: soloDigitos(e.target.value) });
                setErrorConsulta(null);
              }}
              onBlur={() => tocar("numeroDocumento")}
            />
          </div>
        </Field>
        <div className="flex flex-wrap items-end gap-2">
          {!fila.consultado && (
            <Btn onClick={alConsultar} disabled={consulta.isPending || bloqueoConsulta.bloqueado}>
              {consulta.isPending ? "Consultando..." : "Consultar"}
            </Btn>
          )}
          {puedeCambiarDocumento && (
            <Btn tone="secondary" onClick={() => {
                setAvisoManual(null);
                onCambio({ consultado: false, licencia: null, nombreConsultado: false, ...(fila.nombreConsultado ? { nombre: "" } : {}) });
              }}>
              Cambiar documento
            </Btn>
          )}
        </div>
        <Field label="Nombre completo *" error={error("nombre")}>
          <input
            className="field"
            autoComplete="off"
            disabled={!fila.consultado}
            readOnly={documentoBloqueado || fila.nombreConsultado}
            aria-invalid={error("nombre") ? true : undefined}
            value={nombreMostrado}
            onChange={(e) => onCambio({ nombre: e.target.value })}
            onBlur={() => tocar("nombre")}
          />
        </Field>
        <Field label="Teléfono *" error={error("celular")}>
          <input
            className="field"
            inputMode="numeric"
            autoComplete="off"
            disabled={!fila.consultado}
            aria-invalid={error("celular") ? true : undefined}
            value={fila.celular}
            onChange={(e) => onCambio({ celular: soloDigitos(e.target.value) })}
            onBlur={() => tocar("celular")}
          />
        </Field>
        <Field label="Correo electrónico *" error={error("correo")}>
          <input
            className="field"
            type="email"
            autoComplete="off"
            disabled={!fila.consultado}
            aria-invalid={error("correo") ? true : undefined}
            value={fila.correo}
            onChange={(e) => onCambio({ correo: e.target.value })}
            onBlur={() => tocar("correo")}
          />
        </Field>
        <div className="md:col-span-3">
          <AddressField
            id={idCampo("direccion")}
            label="Dirección"
            entrega={direccionEntrega}
            disabled={!fila.consultado}
            value={fila.direccion}
            error={error("direccion")}
            onChange={(v) => onCambio({ direccion: v })}
          />
        </div>
      </div>

      {fila.consultado && avisoManual && (
        <p role="status" className="text-xs">
          {avisoManual}
        </p>
      )}
      {bloqueoConsulta.bloqueado && (
        <p role="status" className="text-xs">
          Podrás intentarlo de nuevo en {bloqueoConsulta.segundosRestantes} s.
        </p>
      )}

      {fila.consultado && (etiquetaLicencia || pideIdentidad) && (
        <div className="flex flex-wrap items-center gap-3">
          {etiquetaLicencia && <Badge tone="info">{etiquetaLicencia}</Badge>}
          {pideIdentidad && <Badge tone={TONO_VALIDACION(estado)}>Validación: {ETIQUETA_VALIDACION[estado] ?? estado}</Badge>}
          {pideIdentidad && estado !== "aprobada" && (
            <span className="ml-auto flex flex-wrap items-center gap-3">
              {enCurso(estado) && parte && !reenviosAgotados && <span className="text-xs font-light text-muted-foreground">Te quedan {parte.reenviosRestantes} reenvíos</span>}
              {enCurso(estado) && esperaReenvio.bloqueado && <span className="text-xs font-light text-muted-foreground">Podrás reenviar en {esperaReenvio.segundosRestantes} s</span>}
              {enCurso(estado) ? (
                <Btn onClick={() => void pedirIdentidad("reenviar")} disabled={ocupado || esperaReenvio.bloqueado || reenviosAgotados}>
                  {ocupado ? "Enviando..." : "Reenviar Validación"}
                </Btn>
              ) : (
                <Btn onClick={() => void pedirIdentidad("solicitar")} disabled={ocupado}>
                  {ocupado ? "Enviando..." : "Validar identidad"}
                </Btn>
              )}
            </span>
          )}
        </div>
      )}
      <div aria-live="polite">
        {aviso && (
          <p role={aviso.tipo === "error" ? "alert" : "status"} className={`text-sm ${aviso.tipo === "error" ? "text-alert" : "text-success"}`}>
            {aviso.texto}
          </p>
        )}
      </div>
      {estado === "rechazada" && pideIdentidad && fila.consultado && (
        <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
          La validación de identidad de esta persona fue rechazada. Puedes enviarle un enlace nuevo con «Validar identidad».
        </p>
      )}
    </div>
  );
}

/** Mensaje de un error de identidad; con el código del 429 muestra la espera exacta. */
function textoDeError(error: unknown): string {
  const espera = esperaDe(error);
  if (error instanceof ApiError && error.code === "reenvio_muy_pronto" && espera) return `Espera ${espera} s antes de reenviar el enlace de nuevo.`;
  if (error instanceof ApiError && error.code === "servicio_no_disponible") return "La validación de identidad no está disponible por ahora. Intenta de nuevo en unos minutos.";
  return mensajeDe(error);
}
