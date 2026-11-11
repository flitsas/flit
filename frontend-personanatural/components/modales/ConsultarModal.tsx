"use client";

import { useMutation } from "@tanstack/react-query";
import { Search } from "lucide-react";
import { useId, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { MobilityLoader } from "@/components/MobilityLoader";
import { portal, type ClientePortal, type EstadoRadicado, type TipoTramite } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import {
  clasificarRadicadoOCedula,
  normalizarPlaca,
  normalizarRadicadoOCedula,
  placaValida,
} from "@/lib/entradas";
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
  /** Valor inicial del segundo campo (p. ej. el radicado que acaba de entregarse en el wizard). */
  radicadoInicial?: string;
}

/** Modal «Consultar mi trámite» (diseño UI): placa + radicado o cédula, y tracking de 5 pasos. */
export function ConsultarModal({ abierto, onCerrar, cliente = portal, tipos = [], radicadoInicial = "" }: Props) {
  return (
    <ModalFlit
      abierto={abierto}
      onCerrar={onCerrar}
      titulo="Consultar mi trámite"
      icono={Search}
      descripcion="Ingresa la placa y el número de radicado o cédula."
    >
      <ContenidoConsulta cliente={cliente} tipos={tipos} onCerrar={onCerrar} radicadoInicial={radicadoInicial} />
    </ModalFlit>
  );
}

function ContenidoConsulta({
  cliente,
  tipos,
  onCerrar,
  radicadoInicial,
}: {
  cliente: ClientePortal;
  tipos: readonly TipoTramite[];
  onCerrar: () => void;
  radicadoInicial: string;
}) {
  const idAyuda = useId();
  const [placa, setPlaca] = useState("");
  const [clave, setClave] = useState(radicadoInicial);
  const bloqueo = useBloqueo();

  const consulta = useMutation({
    mutationFn: () => {
      const clasificado = clasificarRadicadoOCedula(clave);
      if (!clasificado || !placaValida(placa)) throw new Error("Datos incompletos");
      return cliente.radicados.consultar({ placa, ...clasificado });
    },
    onError: (error) => {
      bloqueo.registrar(error);
    },
  });

  const listo = placaValida(placa) && clasificarRadicadoOCedula(clave) !== null;
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
            required
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
            {mensajeDe(consulta.error)}
          </p>
        )}
        {consulta.isSuccess && <ResultadoConsulta resultado={consulta.data} tipos={tipos} onCerrar={onCerrar} />}
      </div>
    </>
  );
}

function ResultadoConsulta({ resultado, tipos, onCerrar }: { resultado: EstadoRadicado; tipos: readonly TipoTramite[]; onCerrar: () => void }) {
  const titular = titularDe(resultado);
  const nombreTipo = tipos.find((t) => t.code === resultado.tipoTramite)?.name ?? resultado.tipoTramite;
  const pasos = resultado.pasos.map((p) => ({ titulo: p.titulo, estado: p.estado, fecha: formatearFechaHora(p.fecha) }));
  const motivoRechazo = resultado.subsanacion?.motivo;

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

      {motivoRechazo && (
        <p role="note" className="mt-4 rounded-[9px] border border-alert bg-alert/10 p-4 text-sm text-alert">
          <b>Causal de rechazo:</b> {motivoRechazo}
        </p>
      )}

      {esAprobado(resultado) && (
        <div className="mt-4 flex flex-col items-center gap-1">
          <button type="button" disabled aria-describedby="comprobante-pendiente" className={boton("flit", "h-11 px-6", "none")}>
            Descargar comprobante
          </button>
          <p id="comprobante-pendiente" className="text-xs text-foreground/70">
            El comprobante estará disponible próximamente.
          </p>
        </div>
      )}

      <button type="button" onClick={onCerrar} className={boton("flit", "mx-auto mt-4 flex h-11 px-10", "none")}>
        Entendido
      </button>
    </section>
  );
}
