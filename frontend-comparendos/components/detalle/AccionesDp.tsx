"use client";

import { useState } from "react";
import { FileText, MessageSquareReply, RotateCcw, Send } from "lucide-react";
import { InlineAlert, Modal } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { Datos } from "@/components/ui/Acordeon";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiAbrirPdf, apiFetch } from "@/lib/api";
import { armarRespuestaDp, erroresRespuestaDp, ordenarTipologias, type FormRespuestaDp, type TipologiaComparendo, type VistaPreviaDp } from "@/lib/dp";
import { estadoLegal } from "@/lib/etiquetas";
import { diaColombia } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { capacidades, tienePermiso } from "@/lib/sesion";
import { useCarga } from "@/lib/useCarga";

const Z = "z-[120]";

/** Elegir tipología, ver la vista previa con sus bloqueos y generar y enviar el DP (§7.3, HU #13488). */
function GenerarDp({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const d = ctx.detalle;
  const puedeEnviar = tienePermiso(ctx.sesion, P.dpEnviar);
  const tipologias = useCarga(`dp-tipologias:${d.id}`, (signal) => apiFetch<TipologiaComparendo[]>(`/comparendos/${d.id}/dp/tipologias`, { signal }));
  const [elegida, setElegida] = useState<string>("");
  const previa = useCarga(elegida ? `dp-previa:${d.id}:${elegida}` : null, (signal) =>
    apiFetch<VistaPreviaDp>(`/comparendos/${d.id}/dp/vista-previa`, { method: "POST", body: { tipologiaId: elegida }, signal }),
  );
  const [confirmar, setConfirmar] = useState(false);
  const [enviando, setEnviando] = useState(false);
  const lista = ordenarTipologias(tipologias.datos ?? []);
  const tipologia = lista.find((t) => t.id === elegida);
  const v = previa.datos;

  const verPdf = () =>
    apiAbrirPdf(`/comparendos/${d.id}/dp/vista-previa.pdf`, { tipologiaId: elegida }).catch((e: unknown) =>
      toast.show(e instanceof Error ? e.message : "No se pudo abrir la vista previa.", "error"),
    );

  const enviar = async () => {
    setEnviando(true);
    try {
      const r = await apiFetch<{ radicado: string; estado: string }>(`/comparendos/${d.id}/dp`, { method: "POST", body: { tipologiaId: elegida } });
      toast.show(
        r.estado === "EnvioFallido" ? `Se radicó ${r.radicado}, pero el correo al organismo falló: puedes reintentar.` : `Derecho de petición ${r.radicado} enviado al organismo.`,
        r.estado === "EnvioFallido" ? "error" : "success",
      );
      setConfirmar(false);
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo enviar el derecho de petición.", "error");
    } finally {
      setEnviando(false);
    }
  };

  return (
    <div className="flex flex-col gap-3 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
      <h4 className="text-xs font-semibold">Generar derecho de petición</h4>
      {tipologias.estado === "loading" && <p className="text-xs opacity-60">Cargando tipologías…</p>}
      {tipologias.estado === "error" && <InlineAlert tone="error">{tipologias.error}</InlineAlert>}
      {tipologias.estado === "ready" && lista.length === 0 && (
        <p className="text-xs opacity-70">Tu empresa no tiene tipologías de derecho de petición habilitadas. Pídelas al administrador de FLIT.</p>
      )}
      {lista.length > 0 && (
        <label className={ETIQUETA_CLS}>
          Tipología
          <select className={INPUT_CLS} value={elegida} onChange={(e) => setElegida(e.target.value)}>
            <option value="">Elige una tipología</option>
            {lista.map((t) => (
              <option key={t.id} value={t.id}>
                {t.codigo} — {t.nombre}
                {t.procede ? "" : " (no procede hoy)"}
              </option>
            ))}
          </select>
        </label>
      )}
      {tipologia && <p className="text-xs opacity-70">{tipologia.descripcionJuridica}</p>}

      {elegida && previa.estado === "loading" && <p className="text-xs opacity-60">Revisando si se puede enviar…</p>}
      {previa.estado === "error" && <InlineAlert tone="error">{previa.error}</InlineAlert>}
      {v && (
        <div className="flex flex-col gap-3">
          {v.bloqueos.length > 0 && (
            <InlineAlert tone="error" title="No se puede enviar todavía">
              <ul className="list-disc pl-4">
                {v.bloqueos.map((b) => (
                  <li key={b.codigo}>{b.mensaje}</li>
                ))}
              </ul>
            </InlineAlert>
          )}
          {v.advertencias.length > 0 && (
            <InlineAlert tone="warning" compact>
              {v.advertencias.join(" ")}
            </InlineAlert>
          )}
          <Datos
            filas={[
              ["Radicado (se asigna al enviar)", <span key="r" className="font-mono">{v.radicado}</span>],
              ["Estado legal hoy", estadoLegal(v.estadoLegal).texto],
              ["Organismo de tránsito", v.organismo],
              ["Se envía a", v.destinatarios.join(", ") || null],
              ["Con copia (CC)", v.cc.length > 0 ? v.cc.join(", ") : null],
              ["Copia oculta (CCO)", v.bcc.length > 0 ? v.bcc.join(", ") : null],
              ["Firma", v.representante ? `${v.representante.nombres}${v.representante.cargo ? ` (${v.representante.cargo})` : ""}` : null],
              ["Anexos", v.anexos.join(", ")],
            ]}
          />
          <div className="flex flex-wrap gap-2">
            {!v.bloqueos.some((b) => b.codigo === "SIN_PLANTILLA") && (
              <button type="button" className={controlCls(false)} onClick={verPdf}>
                <FileText className="h-3.5 w-3.5" aria-hidden />
                Ver documento
              </button>
            )}
            {puedeEnviar && (
              <button type="button" className={BOTON_PRIMARIO_CLS} disabled={!v.puedeEnviar} onClick={() => setConfirmar(true)}>
                <Send className="h-3.5 w-3.5" aria-hidden />
                Generar y enviar
              </button>
            )}
          </div>
        </div>
      )}

      <Modal
        open={confirmar}
        onClose={() => setConfirmar(false)}
        busy={enviando}
        zClassName={Z}
        title="¿Enviar el derecho de petición?"
        footer={
          <div className="flex justify-end gap-2">
            <button type="button" className={controlCls(false)} onClick={() => setConfirmar(false)} disabled={enviando}>
              Cancelar
            </button>
            <button type="button" className={BOTON_PRIMARIO_CLS} disabled={enviando} onClick={enviar}>
              {enviando ? "Enviando…" : "Enviar al organismo"}
            </button>
          </div>
        }
      >
        <p className="text-sm">
          Se asigna el radicado, se genera el PDF y se envía por correo a {v?.destinatarios.join(", ") || "el organismo"}. Un comparendo tiene un solo derecho de
          petición: no se podrá generar otro.
        </p>
      </Modal>
    </div>
  );
}

/** Registrar lo que respondió el organismo (HU #13490): radicado, fecha, sentido y PDF. No se puede modificar después. */
function ResponderDp({ ctx, onCerrar }: { ctx: ContextoDetalle; onCerrar: () => void }) {
  const toast = useToast();
  const dp = ctx.detalle.dp!;
  const hoy = diaColombia();
  const [f, setF] = useState<FormRespuestaDp>({ radicadoOt: "", fecha: hoy, sentido: "", archivo: null });
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const errores = erroresRespuestaDp(f, dp.fechaRadicacion, hoy);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/comparendos/${ctx.detalle.id}/dp/respuesta`, { method: "POST", body: armarRespuestaDp(f) });
      toast.show("Respuesta del organismo registrada.", "success");
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
      title="Respuesta del organismo"
      description={`Derecho de petición ${dp.radicado}. Una vez registrada no se puede modificar.`}
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
        <label className={ETIQUETA_CLS}>
          Radicado de la respuesta
          <input className={INPUT_CLS} value={f.radicadoOt} maxLength={60} onChange={(e) => setF({ ...f, radicadoOt: e.target.value })} />
        </label>
        <label className={ETIQUETA_CLS}>
          Fecha de la respuesta
          <input className={INPUT_CLS} type="date" max={hoy} min={dp.fechaRadicacion?.slice(0, 10)} value={f.fecha} onChange={(e) => setF({ ...f, fecha: e.target.value })} />
        </label>
        <fieldset className="flex flex-col gap-1 md:col-span-2">
          <legend className="text-[11px] font-semibold text-[#5B6B82] dark:text-slate-300">Sentido</legend>
          <div className="flex gap-4 text-xs">
            {(["Favorable", "Desfavorable"] as const).map((s) => (
              <label key={s} className="flex items-center gap-1.5">
                <input type="radio" name="sentido" checked={f.sentido === s} onChange={() => setF({ ...f, sentido: s })} />
                {s}
              </label>
            ))}
          </div>
        </fieldset>
        <label className={`${ETIQUETA_CLS} md:col-span-2`}>
          PDF de la respuesta
          <input type="file" accept="application/pdf" className="text-xs" onChange={(e) => setF({ ...f, archivo: e.target.files?.[0] ?? null })} />
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

/**
 * Acciones del derecho de petición dentro del acordeón Resolución OT / DP (HU #13491, §7.3): generar y enviar si no hay
 * DP, reintentar si el correo falló y registrar la respuesta del organismo cuando ya se envió.
 */
export function AccionesDp({ ctx }: { ctx: ContextoDetalle }) {
  const toast = useToast();
  const d = ctx.detalle;
  const [responder, setResponder] = useState(false);
  const [reintentando, setReintentando] = useState(false);
  if (!capacidades(ctx.sesion).derechosPeticion) return null;

  if (!d.dp) {
    if (!d.activo || d.pagado || !tienePermiso(ctx.sesion, P.dpGenerar)) return null;
    return <GenerarDp ctx={ctx} />;
  }

  const reintentar = async () => {
    setReintentando(true);
    try {
      const r = await apiFetch<{ radicado: string; estado: string }>(`/comparendos/${d.id}/dp/reintentar`, { method: "POST" });
      toast.show(r.estado === "EnvioFallido" ? "El correo volvió a fallar. Revisa los correos del organismo." : "Derecho de petición reenviado.", r.estado === "EnvioFallido" ? "error" : "success");
      ctx.recargar();
    } catch (e) {
      toast.show(e instanceof Error ? e.message : "No se pudo reintentar.", "error");
    } finally {
      setReintentando(false);
    }
  };

  const puedeReintentar = d.dp.estado === "EnvioFallido" && tienePermiso(ctx.sesion, P.dpEnviar);
  const puedeResponder = d.dp.estado === "Enviado" && !d.dp.respuesta && tienePermiso(ctx.sesion, P.dpResponder);
  if (!puedeReintentar && !puedeResponder) return null;

  return (
    <div className="flex flex-wrap gap-2 border-t border-[#DFE5ED] pt-3 dark:border-white/10">
      {puedeReintentar && (
        <button type="button" className={controlCls(false)} disabled={reintentando} onClick={reintentar}>
          <RotateCcw className="h-3.5 w-3.5" aria-hidden />
          {reintentando ? "Reintentando…" : "Reintentar envío"}
        </button>
      )}
      {puedeResponder && (
        <button type="button" className={controlCls(false)} onClick={() => setResponder(true)}>
          <MessageSquareReply className="h-3.5 w-3.5" aria-hidden />
          Registrar respuesta del organismo
        </button>
      )}
      {responder && <ResponderDp ctx={ctx} onCerrar={() => setResponder(false)} />}
    </div>
  );
}
