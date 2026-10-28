"use client";

import { useMutation } from "@tanstack/react-query";
import { AlertTriangle } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Captcha } from "@/components/Captcha";
import { MobilityLoader } from "@/components/MobilityLoader";
import { Badge, Btn, Card, Row } from "@/components/tramite/piezas";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import type { Diagnostico, VehiculoMinimo } from "@/lib/api/cliente";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { formatearFechaHora } from "@/lib/formato";
import { consentimientoDeConsulta, datosTecnicos, entradaConsulta, filasDiagnostico, MENSAJE_ATENCION, MENSAJE_RUNT_NO_RESPONDE, MENSAJE_SIN_VEHICULO } from "@/lib/tramite/diagnostico";

interface Resultado {
  vehiculo: VehiculoMinimo;
  /** `null` si el vehículo no existe: no hay nada que diagnosticar. */
  diagnostico: Diagnostico | null;
}

const MOTIVO_SIN_CONSULTA = "Verifica tu identidad y consulta el vehículo para continuar.";
const MOTIVO_CON_PENDIENTES = "Resuelve los pendientes del diagnóstico para continuar.";

/** Mensaje de un error de la consulta: el RUNT caído tiene su texto propio; el resto, el del catálogo de errores. */
function mensajeDeConsulta(error: unknown): string {
  if (error instanceof ApiError && error.code === "proveedor_no_disponible") return MENSAJE_RUNT_NO_RESPONDE;
  return mensajeDe(error);
}

/**
 * Renderer de las secciones `vehicle_query`: la consulta REAL del vehículo (dato mínimo, con consentimiento y captcha) y el
 * preflight de requisitos del tipo, que el back guarda en el borrador. Cada fila del diagnóstico sale de lo que devuelve el
 * back; no hay indicadores escritos aquí. Sin datos de terceros: el propietario no viaja en la respuesta.
 */
export function ConsultaVehiculo({ activa, intentoAvance, onEstado }: PropsSeccion) {
  const { cliente, solicitud, contexto, recargar, reportarError } = useWizard();
  const bloqueo = useBloqueo();
  const [captcha, setCaptcha] = useState<string | null>(null);
  const [reinicio, setReinicio] = useState(0);
  const idAlerta = `diag-alerta-${solicitud.solicitudId}`;

  const entrada = entradaConsulta(solicitud, contexto?.cedula);

  const consulta = useMutation<Resultado, unknown, string>({
    mutationFn: async (token) => {
      if (!entrada) throw new ApiError("sin_identificador", "Falta el identificador del vehículo", 400);
      const vehiculo = await cliente.consultas.vehiculo({ ...entrada, ...consentimientoDeConsulta }, { captcha: token });
      if (!vehiculo.existe) return { vehiculo, diagnostico: null };
      const propietario = entrada.propietario ? { tipoDocumento: entrada.propietario.tipo, numeroDocumento: entrada.propietario.numero } : undefined;
      const diagnostico = await cliente.solicitudes.diagnosticar(solicitud.solicitudId, propietario ? { propietario } : undefined);
      return { vehiculo, diagnostico };
    },
    // El preflight cambió la `version` del borrador: se vuelve a leer para que lo que se guarde después use la vigente.
    onSuccess: (r) => {
      if (r.diagnostico) void recargar();
    },
    onError: (error) => {
      bloqueo.registrar(error);
      reportarError(error);
    },
    onSettled: () => {
      // El token del captcha sirve una sola vez.
      setCaptcha(null);
      setReinicio((n) => n + 1);
    },
  });

  const guardado = solicitud.datos.diagnostico ?? null;
  const diagnostico: (Diagnostico & { fecha?: string }) | null = consulta.data?.diagnostico ?? guardado;
  const vehiculo = consulta.data?.vehiculo ?? null;
  const noExiste = consulta.data !== undefined && !consulta.data.vehiculo.existe;
  const consultando = consulta.isPending;
  const enEspera = bloqueo.bloqueado;

  // Sin resultado todavía: con el captcha resuelto se consulta sola, sin otro clic. Nunca mientras se espera un 429 o un 503.
  const mutar = consulta.mutate;
  const tokenUsado = useRef<string | null>(null);
  useEffect(() => {
    if (!captcha) {
      tokenUsado.current = null;
      return;
    }
    if (!activa || diagnostico || noExiste || consultando || enEspera) return;
    if (tokenUsado.current === captcha) return; // un token, una consulta (también bajo StrictMode)
    tokenUsado.current = captcha;
    mutar(captcha);
  }, [activa, captcha, diagnostico, noExiste, consultando, enEspera, mutar]);

  const conBloqueos = diagnostico !== null && !diagnostico.puedeContinuar;
  const completo = diagnostico?.puedeContinuar === true;
  const motivo = completo ? undefined : conBloqueos ? MOTIVO_CON_PENDIENTES : MOTIVO_SIN_CONSULTA;
  useEffect(() => {
    onEstado({ completo, bloqueo: motivo });
  }, [completo, motivo, onEstado]);

  const reintentar = () => {
    consulta.reset();
    setCaptcha(null);
    setReinicio((n) => n + 1);
  };

  const filas = diagnostico ? filasDiagnostico(diagnostico.requisitos) : [];
  // La fecha es la que el back guardó con el diagnóstico (llega al recargar el borrador, justo después de consultar).
  const fecha = formatearFechaHora(guardado?.fecha);
  const tecnicos = datosTecnicos(vehiculo, solicitud);
  const error = consulta.isError ? consulta.error : null;
  const sinIdentificador = error instanceof ApiError && error.code === "sin_identificador";

  return (
    <>
      <Card title="Diagnóstico de requisitos previos" id="diagnostico-requisitos">
        <div className="relative" aria-busy={consultando}>
          {fecha && <p className="text-xs font-light text-muted-foreground">Consultado en RUNT el {fecha}</p>}

          {diagnostico && filas.length > 0 && (
            <ul className="wizard-diagnosis mt-3 grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6" aria-label="Indicadores del vehículo">
              {filas.map((f) => (
                <Row key={f.clave} k={f.nombre} v={<Badge tone={f.tono}>{f.estado}</Badge>} />
              ))}
            </ul>
          )}

          {conBloqueos && (
            <div id={idAlerta} role="alert" className="mt-4 flex gap-3 rounded-[9px] bg-warning/10 p-4">
              <AlertTriangle aria-hidden className="h-6 w-6 shrink-0 text-warning" />
              <p>
                <b>Atención:</b> {MENSAJE_ATENCION}
              </p>
            </div>
          )}

          {noExiste && (
            <p role="alert" className="mt-3 flex gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
              <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
              <span>
                {MENSAJE_SIN_VEHICULO}. Revisa la placa o el VIN: para cambiarlos, vuelve al inicio y abre el trámite de nuevo.
              </span>
            </p>
          )}

          {error !== null && (
            <div role="alert" className="mt-3 flex flex-col items-start gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
              <p className="flex gap-2">
                <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
                <span>{sinIdentificador ? "Necesitamos tu cédula para consultar el vehículo. Reanuda tu trámite con tu radicado y tu cédula." : mensajeDeConsulta(error)}</span>
              </p>
              {!sinIdentificador && <Btn tone="secondary" onClick={reintentar} disabled={enEspera || consultando}>Reintentar</Btn>}
              {enEspera && <p role="status" className="text-xs">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
            </div>
          )}

          {!diagnostico && !noExiste && error === null && activa && entrada && (
            <div className="mt-2">
              <Captcha onToken={setCaptcha} reinicio={reinicio} />
            </div>
          )}

          {intentoAvance > 0 && !completo && !conBloqueos && !consultando && (
            <p role="alert" className="mt-3 flex items-center gap-1 text-xs text-alert">
              <AlertTriangle aria-hidden className="h-3.5 w-3.5" />
              {MOTIVO_SIN_CONSULTA}
            </p>
          )}

          {consultando && (
            <div className="absolute inset-0 z-10 grid place-items-center rounded-[9px] bg-background/80 backdrop-blur-sm">
              <MobilityLoader />
            </div>
          )}
        </div>
      </Card>

      <Card title="Datos técnicos del vehículo" id="datos-tecnicos">
        <dl className="grid grid-cols-2 gap-4 sm:grid-cols-3 lg:grid-cols-6">
          {/* El propietario es dato de un tercero: el BFF no lo entrega sin identidad validada, así que se muestra protegido. */}
          <div>
            <dt className="text-xs font-light text-muted-foreground">Propietario Actual</dt>
            <dd className="break-words">
              <span aria-hidden>••••••••</span>
              <span className="sr-only">Dato protegido hasta validar la identidad</span>
            </dd>
          </div>
          {tecnicos.map(([k, v]) => (
            <div key={k}>
              <dt className="text-xs font-light text-muted-foreground">{k}</dt>
              <dd className="break-words">{v}</dd>
            </div>
          ))}
        </dl>
      </Card>
    </>
  );
}
