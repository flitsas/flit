"use client";

import { useMutation } from "@tanstack/react-query";
import { ClipboardList, FileText } from "lucide-react";
import { useId, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { useDefinicion } from "@/components/landing/useCatalogos";
import { MobilityLoader } from "@/components/MobilityLoader";
import { portal, type ClientePortal, type Organismo, type SesionTramite } from "@/lib/api/cliente";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { CONSENTIMIENTO_VERSION } from "@/lib/consentimiento";
import { mensajeDeOrganismo, organismoHabilitadoDelRunt, MENSAJE_PLACA_NO_ENCONTRADA, MENSAJE_RUNT_PUERTA } from "@/lib/landing/puerta-iniciar";
import { comoLista } from "@/lib/landing/catalogos";
import { TEXTO_POLITICA_DATOS, URL_POLITICA_FLIT } from "@/lib/legal";
import { MENSAJE_CONSULTANDO_VEHICULO } from "@/lib/tramite/diagnostico";
import { ModalFlit } from "./ModalFlit";

/** Lo que el ciudadano escribió en la tarjeta «Iniciar mi trámite». Solo vive en memoria. */
export interface DatosInicio {
  tipoCodigo: string;
  /** Número de documento del propietario. La consulta por placa lo exige; la sesión lo usa como identidad del borrador. */
  cedula: string;
  tipoDocumento: string;
  placa?: string;
  vin?: string;
}

interface Props {
  abierto: boolean;
  onCerrar: () => void;
  cliente?: ClientePortal;
  datos: DatosInicio | null;
  /** Política que el ciudadano puede leer antes de aceptar. Por defecto, la de FLIT 2. */
  urlPolitica?: string;
  /** La sesión ya está abierta: quien llama guarda el contexto y entra al wizard. El organismo es el que trajo el RUNT. */
  onSesion: (sesion: SesionTramite, datos: DatosInicio) => void;
}

/**
 * Modal «Requisitos para tu trámite»: documentos que siempre pide el tipo, consentimiento e «Iniciar ahora».
 * Al confirmar se consulta el RUNT (con el documento del propietario si es por placa) y solo entonces se abre el borrador.
 */
export function IniciarModal({ abierto, onCerrar, cliente = portal, datos, urlPolitica = URL_POLITICA_FLIT, onSesion }: Props) {
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
      {datos && <ContenidoIniciar cliente={cliente} datos={datos} urlPolitica={urlPolitica} onCerrar={onCerrar} onSesion={onSesion} onOcupado={setOcupado} />}
    </ModalFlit>
  );
}

interface ContenidoProps {
  cliente: ClientePortal;
  datos: DatosInicio;
  onCerrar: () => void;
  urlPolitica: string;
  onSesion: Props["onSesion"];
  onOcupado: (ocupado: boolean) => void;
}

function mensajeDeArranque(error: unknown): string {
  if (error instanceof ApiError && (error.code === "proveedor_no_disponible" || error.status === 503)) return MENSAJE_RUNT_PUERTA;
  return mensajeDe(error);
}

function ContenidoIniciar({ cliente, datos, urlPolitica, onCerrar, onSesion, onOcupado }: ContenidoProps) {
  const idConsentimiento = useId();
  const [consentimiento, setConsentimiento] = useState(false);
  const [aviso, setAviso] = useState<string | null>(null);
  const bloqueo = useBloqueo();
  const definicion = useDefinicion(cliente, datos.tipoCodigo);
  const porPlaca = Boolean(datos.placa);

  const arranque = useMutation({
    mutationFn: async () => {
      const vehiculo = await cliente.consultas.vehiculo({
        placa: datos.placa,
        vin: datos.vin,
        propietario: porPlaca ? { tipo: datos.tipoDocumento, numero: datos.cedula } : undefined,
        consentimiento: true,
        consentimientoVersion: CONSENTIMIENTO_VERSION,
      });
      if (!vehiculo.existe) return { ok: false as const, mensaje: porPlaca ? MENSAJE_PLACA_NO_ENCONTRADA : "No encontramos el vehículo." };
      const organismos = comoLista<Organismo>(await cliente.catalogos.organismos());
      const avisoOrganismo = mensajeDeOrganismo(vehiculo.organismo, organismos);
      if (avisoOrganismo) return { ok: false as const, mensaje: avisoOrganismo };
      const organismo = organismoHabilitadoDelRunt(vehiculo.organismo, organismos);
      const sesion = await cliente.sesiones.iniciar({
        tipoCodigo: datos.tipoCodigo,
        organismoId: organismo?.id,
        cedula: datos.cedula,
        placa: datos.placa,
        vin: datos.vin,
        consentimiento: true,
        consentimientoVersion: CONSENTIMIENTO_VERSION,
      });
      return { ok: true as const, sesion };
    },
    onMutate: () => {
      setAviso(null);
      onOcupado(true);
    },
    onSuccess: (resultado) => {
      if (!resultado.ok) {
        onOcupado(false);
        setAviso(resultado.mensaje);
        return;
      }
      onSesion(resultado.sesion, datos);
    },
    onError: (error) => {
      onOcupado(false);
      bloqueo.registrar(error);
      setAviso(mensajeDeArranque(error));
    },
  });

  const documentos = (definicion.data?.documentos ?? []).filter((d) => d.obligatorio && (d.condicion ?? "").trim() === "").sort((a, b) => a.orden - b.orden);
  const enCurso = arranque.isPending;
  const puedeIniciar = consentimiento && !enCurso && !bloqueo.bloqueado;

  return (
    <>
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
          if (puedeIniciar) arranque.mutate();
        }}
      >
        <div className="mx-auto flex max-w-xl flex-col items-center gap-1 text-center">
          <label htmlFor={idConsentimiento} className="flex min-h-11 items-center gap-2 text-sm">
            <input id={idConsentimiento} type="checkbox" required className="h-5 w-5 shrink-0 accent-primary" checked={consentimiento} disabled={enCurso} onChange={(e) => setConsentimiento(e.target.checked)} />
            Acepto el tratamiento de mis datos personales.
          </label>
          <a href={urlPolitica} target="_blank" rel="noopener noreferrer" className={boton("link", "h-auto p-0 text-xs font-light text-foreground/70", "none")}>{TEXTO_POLITICA_DATOS}<span className="sr-only"> (se abre en una pestaña nueva)</span></a>
        </div>

        <div aria-live="polite" className="mt-3 text-center text-sm">
          {aviso && <p role="alert" className="rounded-[9px] border border-alert bg-alert/10 p-3 text-alert">{aviso}</p>}
          {bloqueo.bloqueado && <p role="status" className="mt-2 text-xs text-foreground/70">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
        </div>

        <div className="mt-5 flex flex-wrap justify-center gap-3">
          <button type="button" className={boton("back", "h-11 px-6", "none")} disabled={enCurso} onClick={onCerrar}>Iniciar más tarde</button>
          <button type="submit" className={boton("flit", "h-11 px-6", "none")} disabled={!puedeIniciar}>Iniciar ahora</button>
        </div>
      </form>

      {enCurso && (
        <div className="fixed inset-0 z-50 grid place-items-center bg-background/80 backdrop-blur-sm">
          <MobilityLoader mensaje={MENSAJE_CONSULTANDO_VEHICULO} />
        </div>
      )}
    </>
  );
}
