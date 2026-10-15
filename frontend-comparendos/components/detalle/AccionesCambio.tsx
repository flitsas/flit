"use client";

import { useState } from "react";
import { CheckCircle2, CircleX, FileText, MailPlus, MessageSquareReply, Send } from "lucide-react";
import { InlineAlert, Modal } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS, TEXTAREA_CLS } from "@/components/ui/estilos";
import { apiAbrirPdf, apiFetch } from "@/lib/api";
import { armarRespuestaCambio, erroresRespuestaCambio, solicitudPendiente, type FormRespuestaCambio, type Viabilidad } from "@/lib/cambio";
import { diaColombia, formatoFecha } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { capacidades, tienePermiso } from "@/lib/sesion";
import type { DetalleComparendo } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const Z = "z-[120]";
type Solicitud = DetalleComparendo["solicitudesCambio"][number];

function ResponderCambio({ ctx, solicitud, onCerrar }: { ctx: ContextoDetalle; solicitud: Solicitud; onCerrar: () => void }) {
  const toast = useToast();
  const hoy = diaColombia();
  const diaSolicitud = diaColombia(solicitud.solicitadoEn);
  const [f, setF] = useState<FormRespuestaCambio>({ resultado: "", causal: "", fecha: hoy, soporte: null });
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const errores = erroresRespuestaCambio(f, diaSolicitud, hoy);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/comparendos/${ctx.detalle.id}/cambio-contraventor/${solicitud.id}/respuesta`, { method: "POST", body: armarRespuestaCambio(f) });
      toast.show(f.resultado === "Aceptado" ? "Cambio de contraventor aceptado por el organismo." : "Rechazo registrado: puedes volver a solicitar.", "success");
      ctx.recargar();
      onCerrar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo registrar la respuesta.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal
      open
      onClose={onCerrar}
      busy={ocupado}
      zClassName={Z}
      size="lg"
      title="Respuesta del organismo al cambio de contraventor"
      description={`Intento ${solicitud.numeroIntento}, solicitado el ${formatoFecha(diaSolicitud)}.`}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || errores.length > 0} onClick={guardar}>
            {ocupado ? "Guardando…" : "Registrar respuesta"}
          </button>
        </div>
      }
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-2">
        <fieldset className="flex flex-col gap-1">
          <legend className="text-[11px] font-semibold text-[#5B6B82] dark:text-slate-300">Resultado</legend>
          <div className="flex gap-4 text-xs">
            {(["Aceptado", "Rechazado"] as const).map((r) => (
              <label key={r} className="flex items-center gap-1.5">
                <input type="radio" name="resultado" checked={f.resultado === r} onChange={() => setF({ ...f, resultado: r })} />
                {r}
              </label>
            ))}
          </div>
        </fieldset>
        <label className={ETIQUETA_CLS}>
          Fecha de la respuesta
          <input className={INPUT_CLS} type="date" min={diaSolicitud} max={hoy} value={f.fecha} onChange={(e) => setF({ ...f, fecha: e.target.value })} />
        </label>
        {f.resultado === "Rechazado" && (
          <label className={`${ETIQUETA_CLS} md:col-span-2`}>
            Causal del rechazo
            <textarea className={TEXTAREA_CLS} rows={3} value={f.causal} onChange={(e) => setF({ ...f, causal: e.target.value })} />
          </label>
        )}
        <label className={`${ETIQUETA_CLS} md:col-span-2`}>
          Soporte de la respuesta (opcional)
          <input type="file" accept="application/pdf,image/jpeg,image/png" className="text-xs" onChange={(e) => setF({ ...f, soporte: e.target.files?.[0] ?? null })} />
        </label>
      </div>
      {errores.length > 0 && (
        <InlineAlert tone="warning" compact className="mt-3">
          {errores.join(" ")}
        </InlineAlert>
      )}
      {error && (
        <InlineAlert tone="error" compact className="mt-3">
          {error}
        </InlineAlert>
      )}
    </Modal>
  );
}

/** Condiciones de viabilidad, vista previa del formato y solicitud ante el organismo. */
function Solicitar({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const d = ctx.detalle;
  const viabilidad = useCarga(`cambio-viabilidad:${d.id}:${d.ultimoEventoEn}`, (signal) =>
    apiFetch<Viabilidad>(`/comparendos/${d.id}/cambio-contraventor/viabilidad`, { signal }),
  );
  const [confirmar, setConfirmar] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const v = viabilidad.datos;

  const verFormato = () =>
    apiAbrirPdf(`/comparendos/${d.id}/cambio-contraventor/vista-previa`).catch((e: unknown) =>
      toast.show(e instanceof Error ? e.message : "No se pudo abrir el formato.", "error"),
    );

  const verCarta = () =>
    apiAbrirPdf(`/comparendos/${d.id}/cambio-contraventor/vista-previa?documento=carta`).catch((e: unknown) =>
      toast.show(e instanceof Error ? e.message : "No se pudo abrir la carta.", "error"),
    );

  const solicitar = async () => {
    setEnviando(true);
    try {
      const r = await apiFetch<{ numeroIntento: number; correoOt: string }>(`/comparendos/${d.id}/cambio-contraventor`, { method: "POST" });
      const fallo = r.correoOt === "Fallido";
      toast.show(fallo ? `Solicitud ${r.numeroIntento} registrada, pero el correo al organismo falló.` : `Solicitud de cambio enviada al organismo (intento ${r.numeroIntento}).`, fallo ? "error" : "success");
      setConfirmar(false);
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo solicitar el cambio.", "error");
    } finally {
      setEnviando(false);
    }
  };

  if (viabilidad.estado === "loading") return <p className="text-xs opacity-60">Revisando si se puede solicitar el cambio…</p>;
  if (viabilidad.estado === "error") return <InlineAlert tone="error">{viabilidad.error}</InlineAlert>;
  if (!v) return null;

  return (
    <div className="flex flex-col gap-2">
      <ul className="grid grid-cols-1 gap-1 text-xs sm:grid-cols-2">
        {v.condiciones.map((c) => (
          <li key={c.codigo} className="flex items-start gap-1.5">
            {c.cumple ? <CheckCircle2 className="mt-px h-3.5 w-3.5 shrink-0 text-[#12B76A]" aria-label="Cumple" /> : <CircleX className="mt-px h-3.5 w-3.5 shrink-0 text-[#B42318]" aria-label="No cumple" />}
            <span className={c.cumple ? "" : "font-semibold"}>{c.descripcion}</span>
          </li>
        ))}
      </ul>
      <div className="flex flex-wrap gap-2">
        {v.condiciones.find((c) => c.codigo === "CONDUCTOR")?.cumple && (
          <button type="button" className={controlCls(false)} onClick={verFormato}>
            <FileText className="h-3.5 w-3.5" aria-hidden />
            Ver formato
          </button>
        )}
        {v.condiciones.find((c) => c.codigo === "CONDUCTOR")?.cumple && (
          <button type="button" className={controlCls(false)} onClick={verCarta}>
            <FileText className="h-3.5 w-3.5" aria-hidden />
            Ver carta al conductor
          </button>
        )}
        <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!v.viable} onClick={() => setConfirmar(true)}>
          <Send className="h-3.5 w-3.5" aria-hidden />
          Solicitar cambio
        </button>
      </div>
      <Modal
        open={confirmar}
        onClose={() => setConfirmar(false)}
        busy={enviando}
        zClassName={Z}
        title="¿Solicitar el cambio de contraventor?"
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setConfirmar(false)} disabled={enviando}>
              Cancelar
            </button>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={enviando} onClick={solicitar}>
              {enviando ? "Enviando…" : "Enviar al organismo"}
            </button>
          </div>
        }
      >
        <p className="text-sm">
          Se envía el formato firmado por {v.representante ?? "el representante legal"} con la cédula y la licencia del conductor a{" "}
          {v.correos?.join(", ") || v.organismo || "el organismo de tránsito"}, y el aviso 4 al conductor si tiene correo.
        </p>
      </Modal>
    </div>
  );
}

/**
 * Bloque de cambio de contraventor (HU #13481 sobre la API de la HU #13479, §7.4): viabilidad y solicitud, reenvío del
 * aviso 4 y registro de la respuesta del organismo.
 */
export function AccionesCambio({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const d = ctx.detalle;
  const [responder, setResponder] = useState<Solicitud | null>(null);
  const [reenviando, setReenviando] = useState(false);
  if (!capacidades(ctx.sesion).cambioContraventor) return null;

  const pendiente = solicitudPendiente(d.solicitudesCambio);
  const puedeSolicitar =
    d.activo && tienePermiso(ctx.sesion, P.cambioSolicitar) && (d.estadoCambioContraventor === "SinSolicitar" || d.estadoCambioContraventor === "RechazadoOt");
  const puedeResponder = !!pendiente && tienePermiso(ctx.sesion, P.cambioResponder);
  const puedeAviso4 = d.solicitudesCambio.length > 0 && !!d.contraventor?.correo && tienePermiso(ctx.sesion, P.avisosEnviar);
  if (!puedeSolicitar && !puedeResponder && !puedeAviso4) return null;

  const aviso4 = async () => {
    setReenviando(true);
    try {
      const r = await apiFetch<{ estado: string }>(`/comparendos/${d.id}/cambio-contraventor/aviso4`, { method: "POST" });
      toast.show(r.estado === "Fallido" ? "El aviso 4 no se pudo enviar." : "Aviso 4 reenviado al conductor.", r.estado === "Fallido" ? "error" : "success");
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo reenviar el aviso 4.", "error");
    } finally {
      setReenviando(false);
    }
  };

  return (
    <div className="flex flex-col gap-3 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
      {puedeSolicitar && <Solicitar ctx={ctx} />}
      {(puedeResponder || puedeAviso4) && (
        <div className="flex flex-wrap gap-2">
          {puedeResponder && (
            <button type="button" className={controlCls(false)} onClick={() => setResponder(pendiente)}>
              <MessageSquareReply className="h-3.5 w-3.5" aria-hidden />
              Registrar respuesta del organismo
            </button>
          )}
          {puedeAviso4 && (
            <button type="button" className={controlCls(false)} disabled={reenviando} onClick={aviso4}>
              <MailPlus className="h-3.5 w-3.5" aria-hidden />
              {reenviando ? "Reenviando…" : "Reenviar aviso 4"}
            </button>
          )}
        </div>
      )}
      {responder && <ResponderCambio ctx={ctx} solicitud={responder} onCerrar={() => setResponder(null)} />}
    </div>
  );
}
