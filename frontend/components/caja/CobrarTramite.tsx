"use client";

// Pestaña «Cobrar»: buscar el trámite por su ID (PN-AAAA-NNNNNN), ver el desglose y confirmar el cobro. El valor nunca se
// edita. Estados: buscando / cobrando (carrito), error por código (rojo; «ya pagado» y «total cambió» en amarillo),
// bloqueo por intentos (429) con la espera y confirmación de pago registrado.
import { useEffect, useRef, useState, type FormEvent } from "react";
import { Search } from "lucide-react";
import { Button } from "@flit/ui/Button";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { PN_INPUT_CLS, PN_LABEL_CLS } from "@/components/admin/plataforma/persona-natural/pn-ui";
import { formatPesos } from "@/lib/api/admin-pn-tarifas";
import {
  comoCajaError,
  ETIQUETA_MEDIO,
  FORMATO_CODIGO,
  nuevaLlave,
  type CobroConfirmado,
  type OtCajaApi,
  type TramiteParaCobrar,
} from "@/lib/api/ot-caja";
import { mensajeDeCaja, type MensajeDeCaja } from "@/lib/caja/mensajes";
import { ConfirmarCobroForm, DesgloseCobro, MensajeCaja, type DatosDeCobro } from "./ConfirmarCobro";

export const CAJA_BUSCANDO = "Buscando el trámite…";
export const CAJA_COBRANDO = "Registrando el cobro…";

export interface CobrarTramiteProps {
  api: OtCajaApi;
  /** Se avisa al registrarse un cobro: la lista de «Cobros del día» se refresca. */
  onCobrado?: () => void;
}

export function CobrarTramite({ api, onCobrado }: CobrarTramiteProps) {
  const [texto, setTexto] = useState("");
  const [tramite, setTramite] = useState<TramiteParaCobrar | null>(null);
  const [total, setTotal] = useState(0);
  const [buscando, setBuscando] = useState(false);
  const [confirmando, setConfirmando] = useState(false);
  const [mensajeBusqueda, setMensajeBusqueda] = useState<MensajeDeCaja | null>(null);
  const [mensajeCobro, setMensajeCobro] = useState<MensajeDeCaja | null>(null);
  const [pagado, setPagado] = useState<CobroConfirmado | null>(null);
  const [bloqueadoHasta, setBloqueadoHasta] = useState<number | null>(null);
  const [bloqueado, setBloqueado] = useState(false);
  const entrada = useRef<HTMLInputElement>(null);
  // Una llave por intento de confirmación: un reintento tras un corte de red no cobra dos veces.
  const llave = useRef<string | null>(null);

  // Mientras dure la espera de un 429 no se puede buscar.
  useEffect(() => {
    if (bloqueadoHasta === null) return;
    const restante = bloqueadoHasta - Date.now();
    if (restante <= 0) {
      // eslint-disable-next-line react-hooks/set-state-in-effect -- el bloqueo termina por tiempo
      setBloqueado(false);
      return;
    }
    setBloqueado(true);
    const t = setTimeout(() => setBloqueado(false), restante);
    return () => clearTimeout(t);
  }, [bloqueadoHasta]);

  async function buscar(e: FormEvent) {
    e.preventDefault();
    const codigo = texto.trim().toUpperCase();
    // Una búsqueda sin la forma del código no llega al servidor: cada fallo cuenta para el tope de intentos.
    if (!FORMATO_CODIGO.test(codigo)) {
      setMensajeBusqueda({ texto: "No encontramos ese trámite.", tono: "error" });
      entrada.current?.focus();
      return;
    }
    setMensajeBusqueda(null);
    setBuscando(true);
    try {
      const datos = await api.tramite(codigo);
      setTramite(datos);
      setTotal(datos.totalCop);
      setMensajeCobro(null);
      llave.current = null;
    } catch (error) {
      setTramite(null);
      const e = comoCajaError(error);
      setMensajeBusqueda(mensajeDeCaja(e));
      if (e.status === 429) setBloqueadoHasta(Date.now() + (e.reintentarEnSegundos ?? 60) * 1000);
    } finally {
      setBuscando(false);
    }
  }

  async function confirmar(datos: DatosDeCobro) {
    if (!tramite) return;
    llave.current ??= nuevaLlave();
    setConfirmando(true);
    try {
      const resultado = await api.confirmar({ ...datos, codigo: tramite.codigo, totalVistoCop: total, llave: llave.current });
      llave.current = null;
      setPagado(resultado);
      setTramite(null);
      setMensajeCobro(null);
      onCobrado?.();
    } catch (error) {
      // Con respuesta definitiva se abre un intento nuevo; sin ella (corte de red, servidor caído) el reintento usa la misma llave.
      const e = comoCajaError(error);
      if (!e.sinRespuestaDefinitiva) llave.current = null;
      if (e.code === "total_cambio" && e.totalActualCop !== undefined) setTotal(e.totalActualCop);
      setMensajeCobro(mensajeDeCaja(e));
    } finally {
      setConfirmando(false);
    }
  }

  function reiniciar() {
    setTexto("");
    setTramite(null);
    setPagado(null);
    setMensajeBusqueda(null);
    setMensajeCobro(null);
    llave.current = null;
  }

  if (pagado) {
    return (
      <div className="grid gap-4">
        <InlineAlert tone="success">Pago registrado.</InlineAlert>
        <section aria-labelledby="caja-pagado-titulo" className="rounded-[9px] border border-border bg-card p-6">
          <h2 id="caja-pagado-titulo" className="text-xl font-bold text-[color:var(--di-heading)]">
            {pagado.codigo}
          </h2>
          <dl className="mt-3 grid gap-1 text-sm">
            <div className="flex gap-2">
              <dt className="font-semibold">Valor cobrado:</dt>
              <dd>{formatPesos(pagado.valorCop)}</dd>
            </div>
            <div className="flex gap-2">
              <dt className="font-semibold">Medio:</dt>
              <dd>
                {pagado.medioDetalle
                  ? `${ETIQUETA_MEDIO[pagado.medio] ?? pagado.medio} (${pagado.medioDetalle})`
                  : (ETIQUETA_MEDIO[pagado.medio] ?? pagado.medio)}
              </dd>
            </div>
          </dl>
          <div className="mt-5 flex justify-end">
            <Button onClick={reiniciar}>Cobrar otro trámite</Button>
          </div>
        </section>
      </div>
    );
  }

  if (tramite) {
    return (
      <div className="grid gap-4">
        {confirmando && <CarLoaderModal label={CAJA_COBRANDO} />}
        <DesgloseCobro tramite={tramite} totalCop={total} />
        <ConfirmarCobroForm enviando={confirmando} mensaje={mensajeCobro} onConfirmar={(d) => void confirmar(d)} onVolver={reiniciar} />
      </div>
    );
  }

  return (
    <form onSubmit={(e) => void buscar(e)} noValidate aria-labelledby="caja-buscar-titulo" className="rounded-[9px] border border-border bg-card p-6">
      {buscando && <CarLoaderModal label={CAJA_BUSCANDO} />}
      <h2 id="caja-buscar-titulo" className="mb-4 text-xl font-bold text-[color:var(--di-heading)]">
        Buscar trámite
      </h2>
      <div className="flex flex-wrap items-end gap-3">
        <label htmlFor="caja-id-tramite" className={`${PN_LABEL_CLS} min-w-[14rem] flex-1`}>
          ID del trámite
          <input
            ref={entrada}
            id="caja-id-tramite"
            // El foco inicial es el campo del ID: la persona de caja llega a buscar.
            autoFocus
            className={PN_INPUT_CLS}
            inputMode="text"
            autoComplete="off"
            spellCheck={false}
            placeholder="PN-2026-000123"
            value={texto}
            maxLength={20}
            aria-invalid={mensajeBusqueda?.tono === "error" ? true : undefined}
            aria-describedby={mensajeBusqueda ? "caja-mensaje-buscar" : undefined}
            onChange={(e) => {
              setTexto(e.target.value);
              setMensajeBusqueda((m) => (m && !bloqueado ? null : m));
            }}
          />
        </label>
        <Button type="submit" disabled={buscando || bloqueado || texto.trim() === ""}>
          <Search aria-hidden className="h-4 w-4" />
          Buscar
        </Button>
      </div>
      {mensajeBusqueda && !buscando && (
        <div className="mt-3">
          <MensajeCaja id="caja-mensaje-buscar" mensaje={mensajeBusqueda} />
        </div>
      )}
    </form>
  );
}
