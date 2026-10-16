"use client";

import { useState } from "react";
import { Wallet } from "lucide-react";
import { InlineAlert, Modal } from "@flit/ui";
import type { ContextoDetalle } from "@/components/detalle/extensiones";
import { useToast } from "@/components/Toast";
import { BOTON_PRIMARIO_CLS, controlCls, ETIQUETA_CLS, INPUT_CLS } from "@/components/ui/estilos";
import { SelectorArchivo } from "@/components/ui/SelectorArchivo";
import { apiFetch } from "@/lib/api";
import { diaColombia, formatoFecha, formatoPesos } from "@/lib/format/date";
import { P } from "@/lib/nav";
import { armarPago, diferencia, erroresPago, leerValor, porcentajeDescuento, TIPOS_PAGO, valorEsperado, type FormPago } from "@/lib/pagos";
import { tienePermiso } from "@/lib/sesion";

function ModalPago({ ctx, onCerrar }: { ctx: ContextoDetalle; onCerrar: () => void }) {
  const toast = useToast();
  const d = ctx.detalle;
  const hoy = diaColombia();
  // B11-1: dentro de una ventana de descuento lo esperado es el valor con descuento, no el total.
  const porcentaje = porcentajeDescuento(d.situacion.ventanaDescuento);
  const esperado = valorEsperado(d.valores.valorTotal, d.situacion.ventanaDescuento);
  const [f, setF] = useState<FormPago>({ tipo: "", valor: esperado ? String(esperado) : "", fecha: hoy, comprobante: null });
  const [ocupado, setOcupado] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const errores = erroresPago(f, hoy);
  const dif = diferencia(leerValor(f.valor), esperado);

  const guardar = async () => {
    setOcupado(true);
    setError(null);
    try {
      await apiFetch(`/comparendos/${d.id}/pago`, { method: "POST", body: armarPago(f) });
      toast.show("Pago registrado. El comparendo sale de los avisos y ya no admite derecho de petición.", "success");
      ctx.recargar();
      onCerrar();
    } catch (e) {
      setError(e instanceof Error ? e.message : "No se pudo registrar el pago.");
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
      size="lg"
      title="Registrar pago"
      description="Un comparendo tiene un solo pago: no hay pagos parciales ni anulación."
      footer={
        <div className="flex justify-end gap-2">
          <button type="button" className={controlCls(false)} onClick={onCerrar} disabled={ocupado}>
            Cancelar
          </button>
          <button type="button" className={BOTON_PRIMARIO_CLS} disabled={ocupado || errores.length > 0} onClick={guardar}>
            {ocupado ? "Registrando…" : "Registrar pago"}
          </button>
        </div>
      }
    >
      <div className="grid grid-cols-1 gap-3 md:grid-cols-3">
        <label className={ETIQUETA_CLS}>
          Tipo de pago
          <select className={INPUT_CLS} value={f.tipo} onChange={(e) => setF({ ...f, tipo: e.target.value as FormPago["tipo"] })}>
            <option value="">Elige el tipo</option>
            {TIPOS_PAGO.map((t) => (
              <option key={t.valor} value={t.valor}>
                {t.texto}
              </option>
            ))}
          </select>
        </label>
        <label className={ETIQUETA_CLS}>
          Valor pagado
          <input className={INPUT_CLS} inputMode="numeric" value={f.valor} onChange={(e) => setF({ ...f, valor: e.target.value })} />
          <span className="font-normal opacity-70">{formatoPesos(leerValor(f.valor))}</span>
          {porcentaje > 0 && (
            <span className="font-normal opacity-70">
              Valor con {porcentaje} % de descuento
              {d.situacion.finDescuentoVigente ? ` vigente hasta el ${formatoFecha(d.situacion.finDescuentoVigente)}` : ""} (total {formatoPesos(d.valores.valorTotal)}).
            </span>
          )}
        </label>
        <label className={ETIQUETA_CLS}>
          Fecha de pago
          <input className={INPUT_CLS} type="date" max={hoy} value={f.fecha} onChange={(e) => setF({ ...f, fecha: e.target.value })} />
        </label>
        <div className={`${ETIQUETA_CLS} md:col-span-3`}>
          Comprobante (opcional: PDF, JPG o PNG)
          <SelectorArchivo archivo={f.comprobante} accept="application/pdf,image/jpeg,image/png" onCambio={(comprobante) => setF({ ...f, comprobante })} />
        </div>
      </div>
      {dif !== null && dif !== 0 && (
        <InlineAlert tone="info" compact className="mt-3">
          {`El valor pagado ${dif > 0 ? "supera" : "es menor que"} ${porcentaje > 0 ? `el valor con ${porcentaje} % de descuento` : "el valor del comparendo"} (${formatoPesos(esperado)}) por ${formatoPesos(Math.abs(dif))}. Se registra igual.`}
        </InlineAlert>
      )}
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

/** «Registrar pago» dentro del acordeón Pago (HU #13494, §6.19). Solo si aún no tiene pago. */
export function AccionesPago({ ctx }: { ctx: ContextoDetalle }) {
  const [abierto, setAbierto] = useState(false);
  if (ctx.detalle.pago || ctx.detalle.pagado || !tienePermiso(ctx.sesion, P.pagosRegistrar)) return null;
  return (
    <div className="border-t border-[#DFE5ED] pt-3 dark:border-white/10">
      <button type="button" className={controlCls(false)} onClick={() => setAbierto(true)}>
        <Wallet className="h-3.5 w-3.5" aria-hidden />
        Registrar pago
      </button>
      {abierto && <ModalPago ctx={ctx} onCerrar={() => setAbierto(false)} />}
    </div>
  );
}
