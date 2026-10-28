"use client";

import { useQuery } from "@tanstack/react-query";
import { Check, TriangleAlert } from "lucide-react";
import { useRouter } from "next/navigation";
import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { boton } from "@/components/landing/estilos";
import { ConsultarModal } from "@/components/modales/ConsultarModal";
import { RadicadoTracking } from "@/components/modales/RadicadoTracking";
import { MobilityLoader } from "@/components/MobilityLoader";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { Btn, Card } from "@/components/tramite/piezas";
import type { DefinicionTipo, EstadoSolicitud, TipoTramite } from "@/lib/api/cliente";
import { ApiError, mensajeDe, type FallasDeRadicacion } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { nuevaLlave } from "@/lib/idempotencia";
import { conservaLlave, esEstadoFinal, INTERVALO_SONDEO_MS, pasosDeSeguimiento } from "@/lib/tramite/envio";
import { limpiarContexto } from "@/lib/tramite/contexto";
import { ETIQUETA_VALIDACION } from "@/lib/tramite/actores";
import { esEditable, type GrupoPaso } from "@/lib/tramite/pasos";
import { destinoDePendiente, pasoTieneSecciones, textoPendiente } from "@/lib/tramite/resumen";

type Falla = { tipo: "mensaje"; texto: string } | { tipo: "pendientes"; fallas: FallasDeRadicacion } | { tipo: "identidad"; fallas: FallasDeRadicacion };

const NOMBRE_PASO: Readonly<Record<string, string>> = { actores: "Actores", requisitos: "Requisitos", diagnostico: "Diagnóstico", resumen: "Resumen" };

export const claveEstado = (id: string) => ["pn", "estado", id] as const;

/**
 * Paso 5 «Envío» (HU F6). Antes de enviar, muestra el botón «Enviar mi trámite»; una vez entregado (o si el trámite ya había
 * salido del portal, p. ej. al reanudar) muestra la confirmación con el seguimiento. Sin cobro: el trámite se entrega a FLIT 2
 * como borrador.
 */
export function EnvioTramite({ onEstado }: PropsSeccion) {
  const { solicitud } = useWizard();
  const [entregado, setEntregado] = useState(false);

  // Último paso: no hay «Continuar» que gobernar.
  useEffect(() => onEstado({ completo: true }), [onEstado]);

  if (entregado || !esEditable(solicitud.estado)) return <Confirmacion />;
  return <PanelEnvio onEntregado={() => setEntregado(true)} />;
}

// ── Antes de enviar ─────────────────────────────────────────────────────────────────────────────────────────────────

function PanelEnvio({ onEntregado }: { onEntregado: () => void }) {
  const { cliente, definicion, solicitud, recargar, reportarError, irAPaso } = useWizard();
  const bloqueo = useBloqueo();
  const idBloqueo = useId();
  const [enviando, setEnviando] = useState(false);
  const [falla, setFalla] = useState<Falla | null>(null);
  // Una llave por intento de envío: se reutiliza si el intento falló sin saber qué pasó (red, 5xx, 429).
  const llave = useRef<string | null>(null);
  // Guarda contra el doble clic antes de que React pinte el estado «enviando».
  const enCurso = useRef(false);

  const enviar = async () => {
    if (enCurso.current || bloqueo.bloqueado) return;
    enCurso.current = true;
    setEnviando(true);
    setFalla(null);
    llave.current ??= nuevaLlave();
    try {
      await cliente.solicitudes.radicar(solicitud.solicitudId, llave.current);
      onEntregado();
      void recargar();
    } catch (error) {
      reportarError(error);
      bloqueo.registrar(error);
      if (!conservaLlave(error)) llave.current = null;
      setFalla(fallaDe(error));
      // El back dice que ya salió o que algo cambió: se vuelve a leer para que la pantalla refleje lo vigente.
      if (error instanceof ApiError && (error.code === "estado_no_permite_cambio" || error.code === "requisitos_incompletos")) void recargar();
    } finally {
      enCurso.current = false;
      setEnviando(false);
    }
  };

  return (
    <>
      <Card title="Envío de tu trámite" id="envio-tramite">
        <p className="text-sm">
          Al enviar, tu trámite se entrega a FLIT 2 y queda como <b>borrador</b>, pendiente de continuar con el equipo. Si necesitas cambiar algo, vuelve al resumen antes de enviar.
        </p>

        <div aria-live="assertive">{falla && <FallaEnvio falla={falla} onIr={irAPaso} definicion={definicion} />}</div>
        {bloqueo.bloqueado && (
          <p id={idBloqueo} role="status" className="mt-3 text-sm text-muted-foreground">
            Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.
          </p>
        )}

        <div className="mt-6 flex flex-wrap justify-end gap-3">
          <Btn tone="secondary" onClick={() => irAPaso("resumen")} disabled={enviando}>
            Volver al resumen
          </Btn>
          <Btn tone="positive" onClick={() => void enviar()} disabled={enviando || bloqueo.bloqueado} describedBy={bloqueo.bloqueado ? idBloqueo : undefined}>
            Enviar mi trámite
          </Btn>
        </div>
      </Card>
      {enviando && <CargadorDeEnvio />}
    </>
  );
}

/** Cargador a pantalla completa. Va en el <body> porque el carrusel del wizard usa transform y «fixed» quedaría relativo a él. */
function CargadorDeEnvio() {
  if (typeof document === "undefined") return null;
  return createPortal(
    <div aria-busy="true" className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
      <div className="text-center">
        <p className="text-lg font-semibold text-primary">Enviando tu trámite</p>
        <MobilityLoader />
        <p className="text-sm text-muted-foreground">No cierres esta ventana.</p>
      </div>
    </div>,
    document.body,
  );
}

function fallaDe(error: unknown): Falla {
  if (error instanceof ApiError) {
    if (error.code === "requisitos_incompletos" && error.fallas?.pendientes?.length) return { tipo: "pendientes", fallas: error.fallas };
    if (error.code === "identity_pending") return { tipo: "identidad", fallas: error.fallas ?? {} };
  }
  return { tipo: "mensaje", texto: mensajeDe(error) };
}

function FallaEnvio({ falla, onIr, definicion }: { falla: Falla; onIr: (grupo: GrupoPaso) => void; definicion: DefinicionTipo }) {
  const marco = "mt-4 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert";
  const cabecera = (texto: string): ReactNode => (
    <p className="flex items-center gap-2 font-semibold">
      <TriangleAlert aria-hidden className="h-5 w-5 shrink-0" />
      {texto}
    </p>
  );

  if (falla.tipo === "mensaje") {
    return (
      <div role="alert" className={marco}>
        {cabecera(falla.texto)}
      </div>
    );
  }

  if (falla.tipo === "pendientes") {
    return (
      <div role="alert" className={marco}>
        {cabecera("Aún falta información para enviar tu trámite")}
        <ul className="mt-2 space-y-1">
          {(falla.fallas.pendientes ?? []).map((p, i) => {
            const destino = destinoDePendiente(p, definicion);
            return (
              <li key={`${p.codigo}-${p.referencia ?? ""}-${i}`} className="flex flex-wrap items-center gap-2">
                <span>{textoPendiente(p, definicion)}</span>
                {destino && (
                  <button type="button" className={boton("link", "h-auto p-0 text-alert", "none")} onClick={() => onIr(destino)}>
                    Ir a {NOMBRE_PASO[destino]}
                  </button>
                )}
              </li>
            );
          })}
        </ul>
      </div>
    );
  }

  const partes = falla.fallas.partes ?? [];
  const rechazada = falla.fallas.motivo === "identidad_rechazada";
  return (
    <div role="alert" className={marco}>
      {cabecera("Falta validar la identidad")}
      <p className="mt-1">
        {rechazada ? "Alguna validación fue rechazada: reenvía el enlace desde Actores para que la persona vuelva a validar." : "Todas las personas del trámite deben validar su identidad antes de enviarlo."}
      </p>
      {partes.length > 0 && (
        <ul className="mt-2 list-disc space-y-1 pl-5">
          {partes.map((p) => {
            const rol = definicion.actores.find((r) => r.rol === p.parte);
            const nombre = `${rol?.etiqueta || p.parte}${rol && rol.max > 1 ? ` ${p.ordinal}` : ""}`;
            return (
              <li key={`${p.parte}-${p.ordinal}`}>
                {nombre}: validación {ETIQUETA_VALIDACION[p.estado] ?? p.estado}
              </li>
            );
          })}
        </ul>
      )}
      {pasoTieneSecciones("actores", definicion) && (
        <button type="button" className={boton("link", "mt-2 h-auto p-0 text-alert", "none")} onClick={() => onIr("actores")}>
          Ir a Actores
        </button>
      )}
    </div>
  );
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
  const rechazada = codigo === "rechazada_creacion";
  const cancelada = codigo === "cancelada";
  const creada = codigo === "creada";
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
    <>
      <Card>
        <div className="flex flex-col items-center text-center">
          {rechazada || cancelada ? (
            <span aria-hidden className="grid h-16 w-16 place-items-center rounded-full bg-alert text-primary-foreground">
              <TriangleAlert className="h-9 w-9" />
            </span>
          ) : (
            <span aria-hidden className="grid h-16 w-16 place-items-center rounded-full bg-success text-primary-foreground">
              <Check className="h-9 w-9" />
            </span>
          )}
          <h2 ref={titulo} tabIndex={-1} className={`mt-4 text-[32px] font-bold leading-tight ${rechazada || cancelada ? "text-alert" : "text-success"}`}>
            {rechazada ? "No pudimos crear tu trámite en FLIT 2" : cancelada ? "Este trámite fue cancelado" : "Tu trámite fue entregado"}
          </h2>

          <div className="mt-6 w-full overflow-x-auto rounded-[9px] border border-border">
            <table className="w-full min-w-[600px] text-center text-sm">
              <thead className="bg-primary text-primary-foreground">
                <tr>
                  {["Radicado del portal", "Tipo", "Organismo", "Referencia en FLIT 2"].map((t) => (
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
                  <td className="border-t border-border p-4 text-center align-middle">{solicitud.organismoNombre}</td>
                  <td className="border-t border-border p-4 text-center align-middle">{referencia ?? (creada ? "Creado" : "Pendiente")}</td>
                </tr>
              </tbody>
            </table>
          </div>

          {!cancelada && <RadicadoTracking pasos={seguimiento} />}

          <div aria-live="polite" className="mt-2 max-w-2xl space-y-2">
            {rechazada ? (
              <p role="alert" className="text-alert">
                FLIT 2 no pudo crear tu trámite. Guarda tu radicado y comunícate con el organismo de tránsito para continuar.
              </p>
            ) : cancelada ? (
              <p>Este trámite fue cancelado y ya no se puede enviar.</p>
            ) : creada ? (
              <p>
                Tu trámite ya fue creado en FLIT 2{referencia ? <> con el número de referencia <b>{referencia}</b></> : null}. Quedó como borrador, pendiente de continuar con el equipo.
              </p>
            ) : (
              <p>Tu trámite fue entregado a FLIT 2 y está siendo creado. Quedará como borrador, pendiente de continuar con el equipo.</p>
            )}
            {!rechazada && !cancelada && (
              <p className="text-sm text-muted-foreground">
                Guarda tu radicado <b>{solicitud.codigoPublico}</b>: junto con tu cédula te sirve para consultar el estado más adelante en «Consultar mi trámite».
              </p>
            )}
            {estado.isError && !esEstadoFinal(codigo) && (
              <p role="status" className="text-sm text-muted-foreground">
                No pudimos actualizar el estado ahora. Lo intentaremos de nuevo.
              </p>
            )}
          </div>
        </div>
      </Card>

      <div className="flex flex-wrap justify-end gap-3">
        <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={() => setConsultando(true)}>
          Consultar mi trámite
        </button>
        <button type="button" className={boton("flit", "min-h-11 px-6", "none")} onClick={salir}>
          Salir
        </button>
      </div>

      <ConsultarModal abierto={consultando} onCerrar={() => setConsultando(false)} cliente={cliente} tipos={tipos} radicadoInicial={solicitud.codigoPublico} />
    </>
  );
}
