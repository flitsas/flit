"use client";

// HU #14458 — Buscar el trámite por su ID, ver el desglose y confirmar el cobro en caja. El valor jamás se edita: el servidor
// cobra el total vigente y, si cambió desde que la caja lo vio, responde 409 `total_cambio` con el nuevo total.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { useEffect, useRef, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { cajaApi, CajaError } from "@/lib/caja/cliente";
import { mensajeDeCaja, type MensajeDeCaja } from "@/lib/caja/mensajes";
import { irAlLogin } from "@/lib/caja/navegacion";
import { ETIQUETA_MEDIO, type CobroConfirmado, type TramiteParaCobrar } from "@/lib/caja/tipos";
import { formatearPesos } from "@/lib/formato";
import { nuevaLlave } from "@/lib/idempotencia";
import { BuscadorCaja } from "./BuscadorCaja";
import { ConfirmarCobroForm, type DatosDeCobro } from "./ConfirmarCobroForm";
import { DesgloseCobro } from "./DesgloseCobro";
import { MensajeCaja } from "./MensajeCaja";

export const CLAVE_COBROS_HOY = ["pn", "caja", "cobros-hoy"] as const;

export function CobrarTramite() {
  const queryClient = useQueryClient();
  const [tramite, setTramite] = useState<TramiteParaCobrar | null>(null);
  const [total, setTotal] = useState(0);
  const [mensajeBusqueda, setMensajeBusqueda] = useState<MensajeDeCaja | null>(null);
  const [mensajeCobro, setMensajeCobro] = useState<MensajeDeCaja | null>(null);
  const [pagado, setPagado] = useState<CobroConfirmado | null>(null);
  const [bloqueado, setBloqueado] = useState(false);
  const temporizador = useRef<ReturnType<typeof setTimeout> | null>(null);
  // Una llave por intento de confirmación: un reintento tras un corte de red no cobra dos veces.
  const llave = useRef<string | null>(null);

  useEffect(() => () => { if (temporizador.current) clearTimeout(temporizador.current); }, []);

  const buscar = useMutation({
    mutationFn: (codigo: string) => cajaApi.tramite(codigo),
    onSuccess: (datos) => {
      setTramite(datos);
      setTotal(datos.totalCop);
      setMensajeBusqueda(null);
      setMensajeCobro(null);
      llave.current = null;
    },
    onError: (error) => {
      if (error instanceof CajaError && error.sinSesion) return irAlLogin();
      setTramite(null);
      setMensajeBusqueda(mensajeDeCaja(error));
      if (error instanceof CajaError && error.status === 429) {
        setBloqueado(true);
        if (temporizador.current) clearTimeout(temporizador.current);
        temporizador.current = setTimeout(() => setBloqueado(false), (error.reintentarEnSegundos ?? 60) * 1000);
      }
    },
  });

  const confirmar = useMutation({
    mutationFn: (datos: DatosDeCobro) => {
      llave.current ??= nuevaLlave();
      return cajaApi.confirmar({ ...datos, codigo: tramite!.codigo, totalVistoCop: total, llave: llave.current });
    },
    onSuccess: (resultado) => {
      llave.current = null;
      setPagado(resultado);
      setTramite(null);
      setMensajeCobro(null);
      void queryClient.invalidateQueries({ queryKey: CLAVE_COBROS_HOY });
    },
    onError: (error) => {
      if (error instanceof CajaError && error.sinSesion) return irAlLogin();
      // Con respuesta definitiva se abre un intento nuevo; sin ella (corte de red, servidor caído) el reintento usa la misma llave.
      if (!(error instanceof CajaError) || !error.sinRespuestaDefinitiva) llave.current = null;
      if (error instanceof CajaError && error.code === "total_cambio" && error.totalActualCop !== undefined) setTotal(error.totalActualCop);
      setMensajeCobro(mensajeDeCaja(error));
    },
  });

  function reiniciar() {
    setTramite(null);
    setPagado(null);
    setMensajeBusqueda(null);
    setMensajeCobro(null);
    llave.current = null;
    buscar.reset();
    confirmar.reset();
  }

  if (pagado) {
    return (
      <div className="grid gap-4">
        <MensajeCaja tono="exito">Pago registrado.</MensajeCaja>
        <section aria-labelledby="titulo-pagado-caja" className="rounded-[9px] border border-border bg-card p-6">
          <h2 id="titulo-pagado-caja" className="text-xl font-semibold">
            {pagado.codigo}
          </h2>
          <dl className="mt-3 grid gap-1 text-sm">
            <div className="flex gap-2">
              <dt className="font-medium">Valor cobrado:</dt>
              <dd>{formatearPesos(pagado.valorCop)}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="font-medium">Medio:</dt>
              <dd>{pagado.medioDetalle ? `${ETIQUETA_MEDIO[pagado.medio] ?? pagado.medio} (${pagado.medioDetalle})` : (ETIQUETA_MEDIO[pagado.medio] ?? pagado.medio)}</dd>
            </div>
          </dl>
          <div className="mt-5 flex justify-end">
            <button type="button" className={boton("flit", "min-h-11 px-6", "none")} onClick={reiniciar}>
              Cobrar otro trámite
            </button>
          </div>
        </section>
      </div>
    );
  }

  if (tramite) {
    return (
      <div className="grid gap-4">
        <DesgloseCobro tramite={tramite} totalCop={total} />
        <ConfirmarCobroForm enviando={confirmar.isPending} mensaje={mensajeCobro} onConfirmar={(d) => confirmar.mutate(d)} onVolver={reiniciar} />
      </div>
    );
  }

  return <BuscadorCaja buscando={buscar.isPending} mensaje={mensajeBusqueda} onBuscar={(c) => buscar.mutate(c)} bloqueado={bloqueado} />;
}
