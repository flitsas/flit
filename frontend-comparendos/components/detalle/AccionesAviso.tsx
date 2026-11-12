"use client";

import { useState } from "react";
import { Icono } from "@flit/ui/Icono";
import { InlineAlert, Modal } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { avisoPorDefecto, motivoSinAviso, opcionesAviso, type AvisosPermitidos, type TipoAvisoManual } from "@/lib/avisos";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";
import { useCarga } from "@/lib/useCarga";

function ModalAviso({ ctx, onCerrar }: { ctx: ContextoDetalle; onCerrar: () => void }) {
  const toast = useToast();
  const d = ctx.detalle;
  // Bug #14270 (AV-03): solo se ofrecen los avisos que la API dejaría enviar hoy (misma regla de secuencia).
  const permitidos = useCarga(`avisos-permitidos:${d.id}`, (signal) => apiFetch<AvisosPermitidos>(`/comparendos/${d.id}/avisos/permitidos`, { signal }));
  const lista = permitidos.datos?.permitidos ?? [];
  const opciones = opcionesAviso(lista, d.estadoNotificacionConductor);
  const [elegido, setElegido] = useState<TipoAvisoManual | null>(null);
  const tipo = elegido && lista.includes(elegido) ? elegido : avisoPorDefecto(lista);
  // Por qué no salen los demás (sin repetir: si el comparendo no es elegible, los tres comparten motivo).
  const motivos = [...new Set((permitidos.datos?.avisos ?? []).flatMap((a) => (a.motivo ? [a.motivo] : [])))];
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const enviar = async () => {
    setOcupado(true);
    setError(null);
    try {
      if (!tipo) return;
      const r = await apiFetch<{ estado: string }>(`/comparendos/${d.id}/avisos`, { method: "POST", body: { tipo } });
      toast.show(r.estado === "Enviado" ? "Aviso enviado al conductor." : `Aviso registrado (${r.estado.toLowerCase()}): lo verás en el Centro de Mensajes.`, "success");
      ctx.recargar();
      onCerrar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo enviar el aviso.");
    } finally {
      setOcupado(false);
    }
  };

  return (
    <Modal icono="campana"
      open
      onClose={onCerrar}
      busy={ocupado}
      zClassName="z-[120]"
      title="Enviar aviso al conductor"
      description={`Se envía a ${d.contraventor?.correo ?? "el correo del conductor"} con el enlace al portal y el descuento vigente.`}
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || !tipo} onClick={enviar}>
            {ocupado ? "Enviando…" : "Enviar aviso"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        {permitidos.estado === "loading" && <p className="text-xs opacity-70">Revisando qué aviso corresponde…</p>}
        {permitidos.error && <InlineAlert tone="error">{permitidos.error}</InlineAlert>}
        {permitidos.estado === "ready" && opciones.length === 0 && (
          <InlineAlert tone="warning">Hoy no corresponde enviar ningún aviso a este conductor.</InlineAlert>
        )}
        {opciones.length > 0 && tipo && (
          <label className={ETIQUETA_CLS}>
            Aviso
            <select className={INPUT_CLS} value={tipo} onChange={(e) => setElegido(e.target.value as TipoAvisoManual)}>
              {opciones.map((t) => (
                <option key={t.valor} value={t.valor}>
                  {t.etiqueta}
                </option>
              ))}
            </select>
          </label>
        )}
        {motivos.length > 0 && (
          <ul className="text-xs opacity-70">
            {motivos.map((m) => (
              <li key={m}>{m}</li>
            ))}
          </ul>
        )}
        <p className="text-xs opacity-70">El aviso 4 sale con la solicitud de cambio de contraventor y no se envía desde aquí.</p>
        {error && <InlineAlert tone="error">{error}</InlineAlert>}
      </div>
    </Modal>
  );
}

/** «Enviar aviso» en el encabezado del detalle (HU #13485, envío manual de los avisos 1 a 3). */
export function AccionesAviso({ ctx }: { ctx: ContextoDetalle }) {
  const [abierto, setAbierto] = useState(false);
  if (ctx.soloLectura || !tienePermiso(ctx.sesion, P.avisosEnviar)) return null;
  const motivo = motivoSinAviso(ctx.detalle);
  return (
    <>
      <button type="button" className={controlCls(false)} disabled={!!motivo} title={motivo ?? undefined} onClick={() => setAbierto(true)}>
        <Icono nombre="correo" className="h-3.5 w-3.5" />
        Enviar aviso
      </button>
      {abierto && <ModalAviso ctx={ctx} onCerrar={() => setAbierto(false)} />}
    </>
  );
}
