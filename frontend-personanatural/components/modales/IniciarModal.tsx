"use client";

import { useMutation } from "@tanstack/react-query";
import { ClipboardList, FileText } from "lucide-react";
import { useId, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { SelectorOrganismo } from "@/components/landing/SelectorOrganismo";
import { useDefinicion, useOrganismos } from "@/components/landing/useCatalogos";
import { Captcha } from "@/components/Captcha";
import { MobilityLoader } from "@/components/MobilityLoader";
import { portal, type ClientePortal, type Organismo, type SesionTramite } from "@/lib/api/cliente";
import { mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { CONSENTIMIENTO_VERSION } from "@/lib/consentimiento";
import { TEXTO_POLITICAS } from "@/lib/landing/contenido";
import { ModalFlit } from "./ModalFlit";

/** Lo que el ciudadano escribió en la tarjeta «Iniciar mi trámite». Solo vive en memoria. */
export interface DatosInicio {
  tipoCodigo: string;
  cedula: string;
  placa?: string;
  vin?: string;
}

interface Props {
  abierto: boolean;
  onCerrar: () => void;
  cliente?: ClientePortal;
  datos: DatosInicio | null;
  /** El aviso del pie abre el diálogo «El documento oficial aún no está disponible.». */
  onAbrirPoliticas: () => void;
  /** La sesión ya está abierta: quien llama guarda el contexto y entra al wizard. */
  onSesion: (sesion: SesionTramite, datos: DatosInicio, organismoId: string) => void;
}

/**
 * Modal «Requisitos para tu trámite»: documentos obligatorios del tipo elegido (por organismo), consentimiento de datos,
 * captcha y «Iniciar ahora» (abre la sesión efímera) o «Iniciar más tarde» (cierra sin crear nada).
 */
export function IniciarModal({ abierto, onCerrar, cliente = portal, datos, onAbrirPoliticas, onSesion }: Props) {
  // Mientras se abre la sesión no se puede cerrar: así no se crean borradores duplicados.
  const [ocupado, setOcupado] = useState(false);
  return (
    <ModalFlit
      abierto={abierto && datos !== null}
      onCerrar={onCerrar}
      titulo="Requisitos para tu trámite"
      icono={ClipboardList}
      descripcion="Ten a la mano estos documentos para realizar tu trámite digital sin contratiempos."
      descripcionGrande
      ocupado={ocupado}
    >
      {datos && <ContenidoIniciar cliente={cliente} datos={datos} onCerrar={onCerrar} onAbrirPoliticas={onAbrirPoliticas} onSesion={onSesion} onOcupado={setOcupado} />}
    </ModalFlit>
  );
}

interface ContenidoProps {
  cliente: ClientePortal;
  datos: DatosInicio;
  onCerrar: () => void;
  onAbrirPoliticas: () => void;
  onSesion: Props["onSesion"];
  onOcupado: (ocupado: boolean) => void;
}

function ContenidoIniciar({ cliente, datos, onCerrar, onAbrirPoliticas, onSesion, onOcupado }: ContenidoProps) {
  const idConsentimiento = useId();
  const organismos = useOrganismos(cliente);
  const [organismo, setOrganismo] = useState<Organismo | null>(null);
  const [consentimiento, setConsentimiento] = useState(false);
  const [captcha, setCaptcha] = useState<string | null>(null);
  const [reinicio, setReinicio] = useState(0);
  const bloqueo = useBloqueo();
  const definicion = useDefinicion(cliente, datos.tipoCodigo, organismo?.id);

  const sesion = useMutation({
    mutationFn: ({ token, organismoId }: { token: string; organismoId: string }) =>
      cliente.sesiones.iniciar(
        { tipoCodigo: datos.tipoCodigo, organismoId, cedula: datos.cedula, placa: datos.placa, vin: datos.vin, consentimiento: true, consentimientoVersion: CONSENTIMIENTO_VERSION },
        { captcha: token },
      ),
    onMutate: () => onOcupado(true),
    onSuccess: (abierta, { organismoId }) => onSesion(abierta, datos, organismoId),
    onError: (error) => {
      onOcupado(false);
      bloqueo.registrar(error);
    },
    onSettled: () => {
      // El token de un captcha sirve una sola vez.
      setCaptcha(null);
      setReinicio((n) => n + 1);
    },
  });

  const documentos = (definicion.data?.documentos ?? []).filter((d) => d.obligatorio).sort((a, b) => a.orden - b.orden);
  const enCurso = sesion.isPending || sesion.isSuccess;
  const puedeIniciar = organismo !== null && consentimiento && captcha !== null && !enCurso && !bloqueo.bloqueado;

  return (
    <>
      <div className="mx-auto mt-4 max-w-md">
        <div className="dashboard-label">
          Organismo de tránsito
          {organismos.isPending && <MobilityLoader />}
          {organismos.isError && (
            <div role="alert" className="flex flex-col items-start gap-2 text-sm">
              <p>{mensajeDe(organismos.error)}</p>
              <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => void organismos.refetch()} disabled={organismos.isFetching}>Reintentar</button>
            </div>
          )}
          {organismos.isSuccess && <SelectorOrganismo organismos={organismos.data ?? []} valor={organismo} onChange={setOrganismo} disabled={enCurso} />}
        </div>
      </div>

      <section aria-label="Documentos requeridos" aria-busy={definicion.isFetching} className="mt-4">
        {definicion.isPending && <MobilityLoader />}
        {definicion.isError && (
          <div role="alert" className="flex flex-col items-center gap-2 text-center text-sm">
            <p>{mensajeDe(definicion.error)}</p>
            <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => void definicion.refetch()} disabled={definicion.isFetching}>Reintentar</button>
          </div>
        )}
        {definicion.isSuccess && documentos.length === 0 && <p role="status" className="text-center text-sm">Este trámite no exige documentos obligatorios por ahora.</p>}
        {definicion.isSuccess && documentos.length > 0 && (
          <ul className="grid grid-cols-2 gap-3 sm:grid-cols-3 lg:grid-cols-6">
            {documentos.map((d) => (
              <li key={d.docCode} className="flex flex-col items-center gap-2 rounded-[9px] border border-border p-3 text-center text-xs">
                <FileText aria-hidden className="h-7 w-7 text-primary" />
                {d.nombre}
              </li>
            ))}
          </ul>
        )}
      </section>

      <form
        className="mt-5"
        onSubmit={(e) => {
          e.preventDefault();
          if (puedeIniciar && organismo && captcha) sesion.mutate({ token: captcha, organismoId: organismo.id });
        }}
      >
        <div className="mx-auto flex max-w-xl flex-col items-center gap-1 text-center">
          <label htmlFor={idConsentimiento} className="flex min-h-11 items-center gap-2 text-sm">
            <input id={idConsentimiento} type="checkbox" required className="h-5 w-5 shrink-0 accent-primary" checked={consentimiento} disabled={enCurso} onChange={(e) => setConsentimiento(e.target.checked)} />
            Acepto el tratamiento de mis datos personales.
          </label>
          <button type="button" onClick={onAbrirPoliticas} className={boton("link", "h-auto p-0 text-xs font-light text-foreground/70", "none")}>{TEXTO_POLITICAS}</button>
        </div>

        <div className="mt-3">
          <Captcha onToken={setCaptcha} reinicio={reinicio} />
        </div>

        <div aria-live="polite" className="mt-3 text-center text-sm">
          {sesion.isError && <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-alert">{mensajeDe(sesion.error)}</p>}
          {bloqueo.bloqueado && <p role="status" className="mt-2 text-xs text-foreground/70">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
        </div>

        <div className="mt-5 flex flex-wrap justify-center gap-3">
          <button type="button" className={boton("back", "h-11 px-6", "none")} disabled={enCurso} onClick={onCerrar}>Iniciar más tarde</button>
          <button type="submit" className={boton("flit", "h-11 px-6", "none")} disabled={!puedeIniciar}>Iniciar ahora</button>
        </div>
      </form>

      {enCurso && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
          <MobilityLoader />
        </div>
      )}
    </>
  );
}
