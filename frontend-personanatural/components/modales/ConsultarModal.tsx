"use client";

import { useMutation } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { useId, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { Captcha } from "@/components/Captcha";
import { MobilityLoader } from "@/components/MobilityLoader";
import { portal, type ClientePortal, type EstadoRadicado, type TipoTramite } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { cedulaValida, normalizarPlaca, normalizarRadicado, soloDigitos } from "@/lib/entradas";
import { formatearFechaHora } from "@/lib/formato";
import { CLASE_MENSAJE, CLASE_TITULAR, esAprobado, titularDe } from "@/lib/radicados/presentacion";
import { ModalFlit } from "./ModalFlit";
import { RadicadoTracking } from "./RadicadoTracking";

interface Props {
  abierto: boolean;
  onCerrar: () => void;
  cliente?: ClientePortal;
  /** Catálogo de tipos, solo para mostrar el nombre del tipo en lugar de su código. */
  tipos?: readonly TipoTramite[];
  /** El ciudadano pulsó la alerta de subsanación (la costura al wizard de la HU F7). */
  onCorregir?: (radicado: EstadoRadicado, cedula: string) => void;
}

/** Modal «Consultar mi trámite»: radicado + cédula (+ placa opcional), tracking de 5 pasos y alerta de subsanación. */
export function ConsultarModal({ abierto, onCerrar, cliente = portal, tipos = [], onCorregir }: Props) {
  return (
    <ModalFlit abierto={abierto} onCerrar={onCerrar} titulo="Consultar mi trámite" icono={Search} descripcion="Ingresa el número de radicado y la cédula del solicitante. La placa es opcional.">
      <ContenidoConsulta cliente={cliente} tipos={tipos} onCerrar={onCerrar} onCorregir={onCorregir} />
    </ModalFlit>
  );
}

function ContenidoConsulta({ cliente, tipos, onCerrar, onCorregir }: { cliente: ClientePortal; tipos: readonly TipoTramite[]; onCerrar: () => void; onCorregir?: Props["onCorregir"] }) {
  const idAyuda = useId();
  const [placa, setPlaca] = useState("");
  const [radicado, setRadicado] = useState("");
  const [cedula, setCedula] = useState("");
  const [captcha, setCaptcha] = useState<string | null>(null);
  const [reinicio, setReinicio] = useState(0);
  const bloqueo = useBloqueo();

  const consulta = useMutation({
    mutationFn: ({ token }: { token: string }) => cliente.radicados.consultar({ placa: placa || undefined, radicado, cedula }, { captcha: token }),
    onError: (error) => {
      bloqueo.registrar(error);
    },
    onSettled: () => {
      // El token de un captcha sirve una sola vez.
      setCaptcha(null);
      setReinicio((n) => n + 1);
    },
  });

  const listo = radicado.trim().length > 0 && cedulaValida(cedula);
  const puedeEnviar = listo && captcha !== null && !consulta.isPending && !bloqueo.bloqueado;
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
          if (puedeEnviar && captcha) consulta.mutate({ token: captcha });
        }}
      >
        <label className="dashboard-label">
          Placa (opcional)
          <input className="field uppercase" autoComplete="off" placeholder="Ej. ABC123" maxLength={6} value={placa} onChange={(e) => cambio(setPlaca)(normalizarPlaca(e.target.value))} />
        </label>
        <label className="dashboard-label">
          Número de radicado
          <input required className="field uppercase" autoComplete="off" placeholder="Nº de radicado" value={radicado} onChange={(e) => cambio(setRadicado)(normalizarRadicado(e.target.value))} />
        </label>
        <label className="dashboard-label sm:col-span-2">
          Cédula del solicitante
          <input required className="field" inputMode="numeric" autoComplete="off" placeholder="Ej. 1020456789" value={cedula} onChange={(e) => cambio(setCedula)(soloDigitos(e.target.value))} />
        </label>
        <div className="sm:col-span-2">
          <Captcha onToken={setCaptcha} reinicio={reinicio} />
        </div>
        <div className="flex flex-col items-center gap-2 sm:col-span-2">
          <button type="submit" className={boton("flit", "h-11 px-10", "none")} disabled={!puedeEnviar} aria-describedby={idAyuda}>Consultar</button>
          <p id={idAyuda} role="status" className="min-h-4 text-center text-xs text-foreground/70">
            {bloqueo.bloqueado ? `Demasiados intentos. Podrás consultar de nuevo en ${bloqueo.segundosRestantes} s.` : ""}
          </p>
        </div>
      </form>

      <div aria-live="polite" aria-busy={consulta.isPending}>
        {consulta.isPending && <MobilityLoader />}
        {consulta.isError && (
          <p role="alert" className="mt-4 rounded-[9px] border border-alert bg-alert/10 p-3 text-center text-sm text-alert">
            {mensajeDe(consulta.error)}
          </p>
        )}
        {consulta.isSuccess && <ResultadoConsulta resultado={consulta.data} tipos={tipos} onCerrar={onCerrar} onCorregir={() => onCorregir?.(consulta.data, cedula)} />}
      </div>
    </>
  );
}

function ResultadoConsulta({ resultado, tipos, onCerrar, onCorregir }: { resultado: EstadoRadicado; tipos: readonly TipoTramite[]; onCerrar: () => void; onCorregir: () => void }) {
  const titular = titularDe(resultado);
  const nombreTipo = tipos.find((t) => t.code === resultado.tipoTramite)?.name ?? resultado.tipoTramite;
  const pasos = resultado.pasos.map((p) => ({ titulo: p.titulo, estado: p.estado, fecha: formatearFechaHora(p.fecha) }));
  const subsanacion = resultado.subsanacion;
  const puedeCorregir = Boolean(subsanacion?.accionCorregir) && !resultado.soloLectura;

  return (
    <section aria-label="Resultado de la consulta">
      <h3 className={`mt-6 text-center text-2xl font-bold ${CLASE_TITULAR[titular.tono]}`}>{titular.texto}</h3>
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
      <p className={`mt-4 rounded-lg p-4 text-sm ${CLASE_MENSAJE[titular.tono]}`}>{resultado.mensaje}</p>
      {resultado.soloLectura && <p className="mt-2 text-xs text-foreground/70">Consultas este trámite como parte: solo puedes verlo, no modificarlo.</p>}

      {subsanacion && (
        puedeCorregir ? (
          <button type="button" onClick={onCorregir} className="mt-4 w-full rounded-[9px] border border-alert bg-alert/10 p-4 text-left text-sm text-alert transition-transform hover:scale-[1.01]">
            <b>Causal de rechazo:</b> {subsanacion.motivo ?? "El Organismo de Tránsito devolvió la solicitud."} <b>Da clic aquí para subsanar tu trámite.</b>
          </button>
        ) : (
          <p role="note" className="mt-4 rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert">
            <b>Causal de rechazo:</b> {subsanacion.motivo ?? "El Organismo de Tránsito devolvió la solicitud."}
          </p>
        )
      )}

      {esAprobado(resultado) && (
        <div className="mt-4 flex flex-col items-center gap-1">
          <button type="button" disabled aria-describedby="comprobante-pendiente" className={boton("flit", "h-11 px-6", "none")}>Descargar comprobante</button>
          <p id="comprobante-pendiente" className="text-xs text-foreground/70">El comprobante estará disponible próximamente.</p>
        </div>
      )}

      {!puedeCorregir && (
        <button type="button" onClick={onCerrar} className={boton("flit", "mx-auto mt-4 flex h-11 px-10", "none")}>Entendido</button>
      )}
    </section>
  );
}
