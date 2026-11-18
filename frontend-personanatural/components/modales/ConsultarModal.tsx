"use client";

import { useMutation } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { useId, useState, type ReactNode } from "react";
import { boton } from "@/components/landing/estilos";
import { MobilityLoader } from "@/components/MobilityLoader";
import { portal, type CierrePuntoRespuesta, type ClientePortal, type EstadoRadicado, type TipoTramite } from "@/lib/api/cliente";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import {
  cedulaValida,
  clasificarRadicadoOCedula,
  normalizarPlaca,
  normalizarRadicadoOCedula,
  placaValida,
  soloDigitos,
} from "@/lib/entradas";
import { formatearFechaHora } from "@/lib/formato";
import { descargarPdf } from "@/lib/punto/comprobantePdf";
import { MENSAJE_EN_PROCESO, pasosDeCierre, titularDeCierre } from "@/lib/punto/seguimiento";
import { CLASE_MENSAJE, CLASE_TITULAR, esAprobado, titularDe } from "@/lib/radicados/presentacion";
import { ModalFlit } from "./ModalFlit";
import { RadicadoTracking } from "./RadicadoTracking";

interface Props {
  abierto: boolean;
  onCerrar: () => void;
  cliente?: ClientePortal;
  /** Catálogo de tipos, solo para mostrar el nombre del tipo en lugar de su código. */
  tipos?: readonly TipoTramite[];
  /** Valor inicial del segundo campo (p. ej. el radicado que acaba de entregarse en el wizard). */
  radicadoInicial?: string;
  /**
   * Consultar del canal OT (HU #14465): un código de trámite (público o radicado) se busca primero en el cierre con pago del organismo
   * y no pide placa; si no tiene pago (404) se consulta como siempre con placa + radicado.
   */
  consultaPunto?: boolean;
  /** Punto: la caja o la recarga dejaron el trámite sin pago; con la cédula de quien radicó se vuelve al paso Pago. */
  onRetomarPago?: (datos: RetomarPagoDatos) => void;
}

/** Lo necesario para abrir el asistente del punto directamente en el paso Pago de una solicitud reanudada. */
export interface RetomarPagoDatos {
  solicitudId: string;
  tipoCodigo: string;
  placa: string;
  documento: string;
}

/**
 * El trámite ya salió hacia el OT (pagado, en cola o radicado) cuando el paso «Enviado al OT» dejó de estar pendiente. El macro-estado
 * no sirve para esto: sin espejo de FLIT 2 un trámite pagado y radicado también figura como «pendiente_validacion».
 */
const yaEnviado = (r: EstadoRadicado): boolean => (r.pasos.find((p) => p.clave === "enviado_ot")?.estado ?? "pending") !== "pending";
/** Solo un trámite que aún no se pagó ni se envió puede retomar el pago. */
const puedeRetomarPago = (r: EstadoRadicado): boolean => r.macroEstado.codigo === "pendiente_validacion" && !yaEnviado(r);

const MENSAJE_FALTA_PLACA = "Para consultar este trámite necesitas ingresar la placa.";

/**
 * Un trámite ya registrado en FLIT 2.0 tiene seguimiento real (revisión, aprobación, rechazo): se pide con la placa y el radicado que
 * informa el cierre. Si esa consulta no responde, se muestra lo que sabe el cierre (en proceso).
 */
async function conSeguimientoReal(cliente: ClientePortal, cierre: CierrePuntoRespuesta): Promise<EstadoRadicado | CierrePuntoRespuesta> {
  if (cierre.estadoCierre !== "registrado" || !cierre.radicadoFlit2 || !cierre.placa) return cierre;
  try {
    const estado = await cliente.radicados.consultar({ placa: cierre.placa, radicado: cierre.radicadoFlit2 });
    // El catálogo de tipos no se conoce aquí: se muestra el nombre que ya informó el cierre.
    return cierre.tipoTramite ? { ...estado, tipoTramite: cierre.tipoTramite } : estado;
  } catch {
    return cierre;
  }
}

/** Modal «Consultar mi trámite» (diseño UI): placa + radicado o cédula, y tracking de 5 pasos. */
export function ConsultarModal({ abierto, onCerrar, cliente = portal, tipos = [], radicadoInicial = "", consultaPunto = false, onRetomarPago }: Props) {
  return (
    <ModalFlit
      abierto={abierto}
      onCerrar={onCerrar}
      titulo="Consultar mi trámite"
      icono={Search}
      descripcion="Ingresa la placa y el número de radicado o cédula."
    >
      <ContenidoConsulta cliente={cliente} tipos={tipos} onCerrar={onCerrar} radicadoInicial={radicadoInicial} consultaPunto={consultaPunto} onRetomarPago={onRetomarPago} />
    </ModalFlit>
  );
}

function ContenidoConsulta({
  cliente,
  tipos,
  onCerrar,
  radicadoInicial,
  consultaPunto,
  onRetomarPago,
}: {
  cliente: ClientePortal;
  tipos: readonly TipoTramite[];
  onCerrar: () => void;
  radicadoInicial: string;
  consultaPunto: boolean;
  onRetomarPago: ((datos: RetomarPagoDatos) => void) | undefined;
}) {
  const idAyuda = useId();
  const [placa, setPlaca] = useState("");
  const [clave, setClave] = useState(radicadoInicial);
  const bloqueo = useBloqueo();

  // En el punto, un código (con letra o guion) puede ser un trámite con pago: se consulta sin placa.
  const clasificadoClave = clasificarRadicadoOCedula(clave);
  const esCodigo = consultaPunto && clasificadoClave !== null && "radicado" in clasificadoClave;

  const consulta = useMutation<EstadoRadicado | CierrePuntoRespuesta>({
    mutationFn: async () => {
      const clasificado = clasificarRadicadoOCedula(clave);
      if (!clasificado) throw new Error("Datos incompletos");
      if (esCodigo && "radicado" in clasificado) {
        try {
          const cierre = await cliente.punto.pago.cierrePorCodigo(clasificado.radicado);
          return await conSeguimientoReal(cliente, cierre);
        } catch (error) {
          // Sin pago (404 uniforme; 403 y 409 por compatibilidad): se cae a la consulta de siempre, que sí pide la placa.
          if (!(error instanceof ApiError) || (error.status !== 404 && error.status !== 403 && error.status !== 409)) throw error;
          if (!placaValida(placa)) throw new Error(MENSAJE_FALTA_PLACA);
        }
      }
      if (!placaValida(placa)) throw new Error("Datos incompletos");
      return cliente.radicados.consultar({ placa, ...clasificado });
    },
    onError: (error) => {
      bloqueo.registrar(error);
    },
  });

  const listo = (placaValida(placa) || esCodigo) && clasificadoClave !== null;
  const puedeEnviar = listo && !consulta.isPending && !bloqueo.bloqueado;
  const cambio = (fijar: (v: string) => void) => (valor: string) => {
    fijar(valor);
    consulta.reset();
  };

  return (
    <>
      <form
        className="mt-4 grid gap-3 sm:grid-cols-2"
        onSubmit={(e) => {
          e.preventDefault();
          if (puedeEnviar) consulta.mutate();
        }}
      >
        <label className="dashboard-label">
          Placa
          <input
            required={!consultaPunto}
            className="field uppercase"
            autoComplete="off"
            placeholder="EJ. ABC123"
            maxLength={6}
            value={placa}
            onChange={(e) => cambio(setPlaca)(normalizarPlaca(e.target.value))}
          />
        </label>
        <label className="dashboard-label">
          Número de Radicado o Cédula
          <input
            required
            className="field uppercase"
            autoComplete="off"
            placeholder="Nº radicado / Cédula"
            value={clave}
            onChange={(e) => cambio(setClave)(normalizarRadicadoOCedula(e.target.value))}
          />
        </label>
        <div className="flex flex-col items-center gap-2 sm:col-span-2">
          <button type="submit" className={boton("flit", "h-11 px-10", "none")} disabled={!puedeEnviar} aria-describedby={idAyuda}>
            Consultar
          </button>
          <p id={idAyuda} role="status" className="min-h-4 text-center text-xs text-foreground/70">
            {bloqueo.bloqueado ? `Demasiados intentos. Podrás consultar de nuevo en ${bloqueo.segundosRestantes} s.` : ""}
          </p>
        </div>
      </form>

      <div aria-live="polite" aria-busy={consulta.isPending}>
        {consulta.isPending && <MobilityLoader />}
        {consulta.isError && (
          <p role="alert" className="mt-4 rounded-[9px] border border-alert bg-alert/10 p-3 text-center text-sm text-alert">
            {consulta.error instanceof Error && consulta.error.message === MENSAJE_FALTA_PLACA ? MENSAJE_FALTA_PLACA : mensajeDe(consulta.error)}
          </p>
        )}
        {consulta.isSuccess &&
          ("estadoCierre" in consulta.data ? (
            <ResultadoCierreConsulta cierre={consulta.data} onCerrar={onCerrar} />
          ) : (
            <ResultadoConsulta
              resultado={consulta.data}
              tipos={tipos}
              onCerrar={onCerrar}
              retomar={
                consultaPunto && onRetomarPago && puedeRetomarPago(consulta.data) ? (
                  <RetomarPago cliente={cliente} resultado={consulta.data} placaEscrita={placa} onRetomarPago={onRetomarPago} onCerrar={onCerrar} />
                ) : null
              }
            />
          ))}
      </div>
    </>
  );
}

/**
 * Fila de acciones al final del resultado: a la derecha en una sola línea (apiladas a ancho completo en móvil, con la principal arriba).
 * «Entendido» es la principal cuando está sola; si hay otra acción, «Entendido» pasa a secundaria (variant back) a su izquierda.
 */
function FilaAcciones({ onCerrar, principal }: { onCerrar: () => void; principal?: ReactNode }) {
  return (
    <div data-testid="acciones-consulta" className="mt-4 flex flex-col-reverse gap-3 sm:flex-row sm:justify-end">
      <button type="button" onClick={onCerrar} className={boton(principal ? "back" : "flit", "h-11 w-full px-10 sm:w-auto", "none")}>
        Entendido
      </button>
      {principal}
    </div>
  );
}

/** Consulta por código de un trámite con pago en el punto (mismo diseño que el resultado de Lovable). */
function ResultadoCierreConsulta({ cierre, onCerrar }: { cierre: CierrePuntoRespuesta; onCerrar: () => void }) {
  const titular = titularDeCierre(cierre.estadoCierre);
  const rechazado = cierre.estadoCierre === "rechazado";
  return (
    <section aria-label="Resultado de la consulta" className="mt-6">
      <h2 className={`text-center text-2xl font-bold ${CLASE_TITULAR[titular.tono]}`}>{titular.texto}</h2>
      <RadicadoTracking pasos={pasosDeCierre(cierre.estadoCierre)} />
      <dl className="space-y-2 text-sm">
        {[
          ["ID del trámite", cierre.codigoPublico],
          ["Placa asociada", cierre.placa ?? "—"],
          ["Tipo de trámite", cierre.tipoTramite ?? "—"],
        ].map(([k, v]) => (
          <div key={k} className="flex justify-between gap-4">
            <dt className="text-foreground/70">{k}</dt>
            <dd className="text-right font-medium">{v}</dd>
          </div>
        ))}
      </dl>
      {rechazado ? (
        <p role="note" className="mt-4 rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert">
          <b>Causal de rechazo:</b> {cierre.motivo ?? "El organismo no pudo registrar el trámite."} Acércate al organismo con este código: {cierre.codigoPublico}
        </p>
      ) : (
        <p className={`mt-4 rounded-lg p-4 text-sm ${CLASE_MENSAJE[titular.tono]}`}>{MENSAJE_EN_PROCESO}</p>
      )}
      <FilaAcciones onCerrar={onCerrar} />
    </section>
  );
}

/** Ya enviado al OT y sin respuesta: el texto de «en proceso» del diseño; «Estamos validando…» queda para borrador, identidad y pago pendiente. */
const mensajeDeCaja = (r: EstadoRadicado): string => (r.macroEstado.codigo === "pendiente_validacion" && yaEnviado(r) ? MENSAJE_EN_PROCESO : r.mensaje);

function ResultadoConsulta({
  resultado,
  tipos,
  onCerrar,
  retomar,
}: {
  resultado: EstadoRadicado;
  tipos: readonly TipoTramite[];
  onCerrar: () => void;
  /** Reemplaza la fila de acciones cuando se puede retomar el pago (trae su propia fila con «Entendido»). */
  retomar: ReactNode;
}) {
  const titular = titularDe(resultado);
  const nombreTipo = tipos.find((t) => t.code === resultado.tipoTramite)?.name ?? resultado.tipoTramite;
  const pasos = resultado.pasos.map((p) => ({ titulo: p.titulo, estado: p.estado, fecha: formatearFechaHora(p.fecha) }));
  const motivoRechazo = resultado.subsanacion?.motivo;

  return (
    <section aria-label="Resultado de la consulta">
      <h2 className={`mt-6 text-center text-2xl font-bold ${CLASE_TITULAR[titular.tono]}`}>{titular.texto}</h2>
      <RadicadoTracking pasos={pasos} />
      <dl className="space-y-2 text-sm">
        {[
          ["ID del trámite", resultado.radicado],
          ["Placa asociada", resultado.placa ?? "—"],
          ["Tipo de trámite", nombreTipo],
        ].map(([k, v]) => (
          <div key={k} className="flex justify-between gap-4">
            <dt className="text-foreground/70">{k}</dt>
            <dd className="text-right font-medium">{v}</dd>
          </div>
        ))}
      </dl>
      <p className={`mt-4 rounded-lg p-4 text-sm ${CLASE_MENSAJE[titular.tono]}`}>{mensajeDeCaja(resultado)}</p>

      {motivoRechazo && (
        <p role="note" className="mt-4 rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert">
          <b>Causal de rechazo:</b> {motivoRechazo}
        </p>
      )}

      {resultado.soloLectura && <p className="mt-4 text-xs text-foreground/70">Consultas este trámite como parte: solo puedes verlo, no modificarlo.</p>}

      {retomar ?? <FilaAcciones onCerrar={onCerrar} principal={esAprobado(resultado) ? (
        <button
            type="button"
            className={boton("positive", "h-11 w-full px-10 sm:w-auto", "none")}
            onClick={() =>
              descargarPdf(`comprobante-${resultado.radicado}.pdf`, [
                "FLIT - Comprobante del trámite",
                "",
                `ID del trámite: ${resultado.radicado}`,
                `Placa: ${resultado.placa ?? "—"}`,
                `Tipo de trámite: ${nombreTipo}`,
                `Organismo: ${resultado.organismo}`,
                `Estado: ${titular.texto}`,
                ...pasos.map((p) => `${p.titulo}: ${p.fecha ?? "Sin fecha"}`),
              ])
            }
          >
            Descargar comprobante
          </button>
      ) : undefined} />}
    </section>
  );
}

/** Punto: «Retomar el pago» pide la cédula de quien radicó y reanuda la sesión; con el pago pendiente abre el paso Pago. */
function RetomarPago({
  cliente,
  resultado,
  placaEscrita,
  onRetomarPago,
  onCerrar,
}: {
  cliente: ClientePortal;
  resultado: EstadoRadicado;
  placaEscrita: string;
  onRetomarPago: (datos: RetomarPagoDatos) => void;
  onCerrar: () => void;
}) {
  const [abierto, setAbierto] = useState(false);
  const [cedula, setCedula] = useState("");
  const [sigueSuCurso, setSigueSuCurso] = useState(false);
  const bloqueo = useBloqueo();
  const idCedula = useId();
  const idForm = useId();

  const reanudar = useMutation({
    mutationFn: () => cliente.sesiones.reanudar({ radicado: resultado.radicado, cedula }),
    onSuccess: (sesion) => {
      if (sesion.estado !== "pendiente_pago") {
        setSigueSuCurso(true);
        return;
      }
      onRetomarPago({
        solicitudId: sesion.solicitudId,
        tipoCodigo: sesion.tipoTramiteCodigo,
        placa: resultado.placa ?? placaEscrita,
        documento: cedula,
      });
    },
    onError: (error) => {
      bloqueo.registrar(error);
    },
  });

  const puedeEnviar = cedulaValida(cedula) && !reanudar.isPending && !bloqueo.bloqueado;
  const mensajeError = (error: unknown) =>
    error instanceof ApiError && error.status === 404 ? "No encontramos un trámite con esos datos." : mensajeDe(error);

  if (sigueSuCurso) {
    return (
      <>
        <p role="status" className="mt-4 rounded-[9px] bg-primary/10 p-3 text-center text-sm text-primary">
          Tu trámite sigue su curso. Ya no necesitas retomar el pago.
        </p>
        <FilaAcciones onCerrar={onCerrar} />
      </>
    );
  }

  if (!abierto) {
    return (
      <FilaAcciones
        onCerrar={onCerrar}
        principal={
          <button type="button" onClick={() => setAbierto(true)} className={boton("flit", "h-11 w-full px-10 sm:w-auto", "none")}>
            Retomar el pago
          </button>
        }
      />
    );
  }

  return (
    <>
    <form
      id={idForm}
      aria-label="Retomar el pago"
      className="mt-4 grid gap-3 rounded-[9px] border border-border p-4"
      onSubmit={(e) => {
        e.preventDefault();
        if (puedeEnviar) reanudar.mutate();
      }}
    >
      <label className="dashboard-label" htmlFor={idCedula}>
        Cédula de quien radicó
      </label>
      <input
        id={idCedula}
        required
        className="field"
        inputMode="numeric"
        autoComplete="off"
        placeholder="Ej. 1020456789"
        value={cedula}
        onChange={(e) => {
          setCedula(soloDigitos(e.target.value));
          reanudar.reset();
        }}
      />
      <div aria-live="polite" aria-busy={reanudar.isPending}>
        {reanudar.isPending && <MobilityLoader />}
        {bloqueo.bloqueado && (
          <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-center text-sm text-alert">
            Demasiados intentos. Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.
          </p>
        )}
        {reanudar.isError && !bloqueo.bloqueado && (
          <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-center text-sm text-alert">
            {mensajeError(reanudar.error)}
          </p>
        )}
      </div>
    </form>
    <FilaAcciones
      onCerrar={onCerrar}
      principal={
        <button type="submit" form={idForm} className={boton("flit", "h-11 w-full px-10 sm:w-auto", "none")} disabled={!puedeEnviar}>
          Continuar
        </button>
      }
    />
    </>
  );
}
