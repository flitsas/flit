"use client";

import { useState } from "react";
import { Mail } from "lucide-react";
import { InlineAlert, Modal } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { motivoSinAviso, siguienteAviso, TIPOS_AVISO, type TipoAvisoManual } from "@/lib/avisos";
import { P } from "@/lib/nav";
import { tienePermiso } from "@/lib/sesion";

function ModalAviso({ ctx, onCerrar }: { ctx: ContextoDetalle; onCerrar: () => void }) {
  const toast = useToast();
  const d = ctx.detalle;
  const [tipo, setTipo] = useState<TipoAvisoManual>(siguienteAviso(d.estadoNotificacionConductor));
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const enviar = async () => {
    setOcupado(true);
    setError(null);
    try {
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
    <Modal
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
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado} onClick={enviar}>
            {ocupado ? "Enviando…" : "Enviar aviso"}
          </button>
        </div>
      }
    >
      <div className="flex flex-col gap-3">
        <label className={ETIQUETA_CLS}>
          Aviso
          <select className={INPUT_CLS} value={tipo} onChange={(e) => setTipo(e.target.value as TipoAvisoManual)}>
            {TIPOS_AVISO.map((t) => (
              <option key={t.valor} value={t.valor}>
                {t.etiqueta}
              </option>
            ))}
          </select>
        </label>
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
        <Mail className="h-3.5 w-3.5" aria-hidden />
        Enviar aviso
      </button>
      {abierto && <ModalAviso ctx={ctx} onCerrar={() => setAbierto(false)} />}
    </>
  );
}
