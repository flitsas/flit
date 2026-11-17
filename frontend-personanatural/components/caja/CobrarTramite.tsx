"use client";

// HU #14458 — Buscar el trámite por su ID, ver el desglose y confirmar el cobro en caja. El valor jamás se edita: el servidor
// cobra el total vigente y, si cambió desde que la caja lo vio, responde 409 `total_cambio` con el nuevo total.
import { useMutation, useQueryClient } from "@tanstack/react-query";
import { Check } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { Btn, Card } from "@/components/tramite/piezas";
import { cajaApi, CajaError } from "@/lib/caja/cliente";
import { mensajeDeCaja, type MensajeDeCaja } from "@/lib/caja/mensajes";
import { irAlLogin } from "@/lib/caja/navegacion";
import { ETIQUETA_MEDIO, type CobroConfirmado, type TramiteParaCobrar } from "@/lib/caja/tipos";
import { formatearPesos } from "@/lib/formato";
import { nuevaLlave } from "@/lib/idempotencia";
import { BuscadorCaja } from "./BuscadorCaja";
import { ConfirmarCobroForm, type DatosDeCobro } from "./ConfirmarCobroForm";
import { DesgloseCobro } from "./DesgloseCobro";

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
    const medio = pagado.medioDetalle ? `${ETIQUETA_MEDIO[pagado.medio] ?? pagado.medio} (${pagado.medioDetalle})` : (ETIQUETA_MEDIO[pagado.medio] ?? pagado.medio);
    return (
      <div className="grid gap-4">
        <Card>
          <div className="flex flex-col items-center text-center">
            <div role="status" className="flex flex-col items-center">
              <span aria-hidden className="grid h-16 w-16 place-items-center rounded-full bg-success text-primary-foreground">
                <Check className="h-9 w-9" />
              </span>
              <h2 className="mt-4 text-2xl font-bold leading-tight text-success sm:text-[32px]">Pago registrado.</h2>
            </div>
            <div className="mt-6 w-full overflow-x-auto rounded-[9px] border border-border">
              <table className="w-full min-w-[400px] text-center text-sm">
                <thead className="bg-primary text-primary-foreground">
                  <tr>
                    {["ID del trámite", "Valor cobrado", "Medio"].map((t) => (
                      <th key={t} scope="col" className="p-4 text-center align-middle font-bold">
                        {t}
                      </th>
                    ))}
                  </tr>
                </thead>
                <tbody className="bg-card">
                  <tr>
                    <td className="border-t border-border p-4 text-center align-middle font-semibold">{pagado.codigo}</td>
                    <td className="border-t border-border p-4 text-center align-middle">{formatearPesos(pagado.valorCop)}</td>
                    <td className="border-t border-border p-4 text-center align-middle">{medio}</td>
                  </tr>
                </tbody>
              </table>
            </div>
          </div>
        </Card>
        <div className="flex flex-wrap items-center justify-end gap-3">
          <Btn onClick={reiniciar}>Cobrar otro trámite</Btn>
        </div>
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
