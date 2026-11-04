"use client";

import { skipToken, useQuery, useQueryClient } from "@tanstack/react-query";
import { TriangleAlert } from "lucide-react";
import { useEffect, useId, useRef, useState, type ReactNode } from "react";
import { createPortal } from "react-dom";
import { boton } from "@/components/landing/estilos";
import { useWizard, type PropsSeccion } from "@/components/tramite/ContextoWizard";
import { MobilityLoader } from "@/components/MobilityLoader";
import { Btn, Card } from "@/components/tramite/piezas";
import { claveSolicitud } from "@/components/tramite/useSolicitud";
import type { Cotizacion, DefinicionTipo, Solicitud } from "@/lib/api/cliente";
import { ApiError, mensajeDe, type FallasDeRadicacion } from "@/lib/api/errores";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { EXIGIR_IDENTIDAD_AL_ENVIAR, ETIQUETA_VALIDACION } from "@/lib/tramite/actores";
import { claveCotizacion } from "@/lib/tramite/cotizacion";
import { conservaLlave } from "@/lib/tramite/envio";
import { formatearPesos } from "@/lib/formato";
import { nuevaLlave } from "@/lib/idempotencia";
import type { GrupoPaso } from "@/lib/tramite/pasos";
import { destinoDePendiente, pasoTieneSecciones, textoPendiente } from "@/lib/tramite/resumen";
import { recordarPresentacion } from "@/lib/tramite/presentacion";

const NOMBRE_PASO: Readonly<Record<string, string>> = {
  actores: "Actores",
  requisitos: "Requisitos",
  diagnostico: "Diagnóstico",
  resumen: "Resumen",
};

type Falla = { tipo: "mensaje"; texto: string } | { tipo: "pendientes"; fallas: FallasDeRadicacion } | { tipo: "identidad"; fallas: FallasDeRadicacion };

/**
 * Paso anterior al envío: resume el trámite y radica con «Enviar trámite».
 * Si el BFF responde identidad pendiente, se muestra qué parte falta.
 */
export function ResumenTramite({ onEstado }: PropsSeccion) {
  const { cliente, definicion, solicitud, recargar, reportarError, irAPaso } = useWizard();
  const queryClient = useQueryClient();
  const bloqueo = useBloqueo();
  const idBloqueo = useId();
  const [enviando, setEnviando] = useState(false);
  const [falla, setFalla] = useState<Falla | null>(null);
  const llave = useRef<string | null>(null);
  const enCurso = useRef(false);

  const transformaciones = solicitud.datos.requisitos?.transformaciones ?? [];
  const cotizacion = useQuery<Cotizacion>({
    queryKey: claveCotizacion(solicitud.organismoId, definicion.code, transformaciones),
    queryFn: skipToken,
    staleTime: Infinity,
  });

  const placa = solicitud.placa ?? solicitud.vin ?? "—";

  useEffect(() => {
    onEstado({ completo: true });
  }, [onEstado]);

  const marcarEnviada = (estado: string) => {
    queryClient.setQueryData<Solicitud>(claveSolicitud(solicitud.solicitudId), (prev) => (prev ? { ...prev, estado } : prev));
  };

  const enviar = async () => {
    if (enCurso.current || bloqueo.bloqueado) return;
    enCurso.current = true;
    setEnviando(true);
    setFalla(null);
    llave.current ??= nuevaLlave();
    recordarPresentacion(solicitud);
    try {
      const resultado = await cliente.solicitudes.radicar(solicitud.solicitudId, llave.current);
      marcarEnviada(resultado.estado);
      irAPaso("envio");
      void recargar();
    } catch (error) {
      reportarError(error);
      bloqueo.registrar(error);
      if (!conservaLlave(error)) llave.current = null;
      if (error instanceof ApiError && error.code === "estado_no_permite_cambio") {
        marcarEnviada("en_cola");
        irAPaso("envio");
        void recargar();
      } else {
        if (error instanceof ApiError && error.code === "requisitos_incompletos") void recargar();
        setFalla(fallaDe(error));
      }
    } finally {
      enCurso.current = false;
      setEnviando(false);
    }
  };

  return (
    <div className="space-y-6" role="region" aria-label="Resumen del trámite">
      <Card title="Datos del trámite" id="resumen-tramite">
        <ul className="space-y-3">
          <li className="flex justify-between gap-4">
            <span>Placa</span>
            <b>{placa}</b>
          </li>
          <li className="flex justify-between gap-4">
            <span>Tipo de Trámite</span>
            <b>{definicion.name}</b>
          </li>
          {(cotizacion.data?.lineas ?? []).map((linea) => (
            <li key={linea.codigo} className="flex justify-between gap-4">
              <span>{linea.nombre}</span>
              <b>{formatearPesos(linea.valorCop)}</b>
            </li>
          ))}
          <li className="flex justify-between gap-4 border-t border-border pt-3 text-lg">
            <span className="font-bold">Valor del trámite</span>
            <b className="text-primary">{cotizacion.data ? formatearPesos(cotizacion.data.totalCop) : "—"}</b>
          </li>
        </ul>
      </Card>

      <div aria-live="assertive">{falla && <FallaEnvio falla={falla} onIr={irAPaso} definicion={definicion} />}</div>
      {bloqueo.bloqueado && (
        <p id={idBloqueo} role="status" className="text-sm text-muted-foreground">
          Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.
        </p>
      )}

      <div className="flex flex-wrap items-center justify-end gap-3">
        <Btn tone="secondary" onClick={() => irAPaso("requisitos")} disabled={enviando}>
          Atrás
        </Btn>
        <Btn tone="positive" onClick={() => void enviar()} disabled={enviando || bloqueo.bloqueado} describedBy={bloqueo.bloqueado ? idBloqueo : undefined}>
          Enviar trámite
        </Btn>
      </div>
      {enviando && <CargadorDeEnvio />}
    </div>
  );
}

function fallaDe(error: unknown): Falla {
  if (error instanceof ApiError) {
    if (error.code === "requisitos_incompletos" && error.fallas?.pendientes?.length) return { tipo: "pendientes", fallas: error.fallas };
    if (error.code === "identity_pending") {
      if (!EXIGIR_IDENTIDAD_AL_ENVIAR) return { tipo: "mensaje", texto: "No pudimos enviar el trámite. Intenta de nuevo." };
      return { tipo: "identidad", fallas: error.fallas ?? {} };
    }
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
        {rechazada
          ? "Alguna validación fue rechazada: reenvía el enlace desde Actores para que la persona vuelva a validar."
          : "Todas las personas del trámite deben validar su identidad antes de enviarlo."}
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
