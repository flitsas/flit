"use client";

import { useMutation, useQuery } from "@tanstack/react-query";
import { CircleDollarSign } from "lucide-react";
import { useRef, useState } from "react";
import { boton } from "@/components/landing/estilos";
import { SelectorOrganismo } from "@/components/landing/SelectorOrganismo";
import { SelectorTipoTramite } from "@/components/landing/SelectorTipoTramite";
import { useOrganismos } from "@/components/landing/useCatalogos";
import { MobilityLoader } from "@/components/MobilityLoader";
import { ApiError, mensajeDe } from "@/lib/api/errores";
import { portal, type ClientePortal, type Cotizacion, type Organismo, type TipoTramite } from "@/lib/api/cliente";
import { useBloqueo } from "@/lib/api/useBloqueo";
import { correoValido } from "@/lib/entradas";
import { formatearPesos } from "@/lib/formato";
import { nuevaLlave } from "@/lib/idempotencia";
import { comoLista, tiposDisponibles } from "@/lib/landing/catalogos";
import { ModalFlit } from "./ModalFlit";

interface Props {
  abierto: boolean;
  onCerrar: () => void;
  cliente?: ClientePortal;
  /** Organismo ya elegido (el enlace «Ver tarifas» de la columna de organismos). */
  organismoInicial?: Organismo | null;
}

export const MENSAJE_COTIZACION_FALLIDA = "No pudimos calcular la cotización. Intenta de nuevo.";
export const MENSAJE_SIN_TARIFAS = "Este organismo aún no tiene tarifas publicadas.";
const CODIGOS_DE_TARIFA = new Set(["tipo_no_habilitado", "tarifa_no_vigente", "tipo_no_disponible", "organismo_no_habilitado"]);

/** Mensaje al fallar el cálculo: el específico si el back dijo por qué no hay tarifa o si pidió esperar; si no, el genérico. */
export function mensajeDeCotizacion(error: unknown): string {
  if (error instanceof ApiError && (CODIGOS_DE_TARIFA.has(error.code) || error.status === 429)) return mensajeDe(error);
  return MENSAJE_COTIZACION_FALLIDA;
}

/** Modal «Cotizar mi trámite»: organismo + tipo disponible → tabla de dos líneas y total, y envío por correo. */
export function CotizarModal({ abierto, onCerrar, cliente = portal, organismoInicial = null }: Props) {
  return (
    <ModalFlit abierto={abierto} onCerrar={onCerrar} titulo="Cotizar mi trámite" icono={CircleDollarSign} desborde>
      <ContenidoCotizar cliente={cliente} organismoInicial={organismoInicial} />
    </ModalFlit>
  );
}

function ContenidoCotizar({ cliente, organismoInicial }: { cliente: ClientePortal; organismoInicial: Organismo | null }) {
  const organismos = useOrganismos(cliente);
  const [organismo, setOrganismo] = useState<Organismo | null>(organismoInicial);
  const [tipo, setTipo] = useState("");
  const bloqueo = useBloqueo();

  const tipos = useQuery({
    queryKey: ["pn", "tipos-tramite", organismo?.id ?? null],
    enabled: organismo !== null,
    queryFn: async ({ signal }) => tiposDisponibles(comoLista<TipoTramite>(await cliente.catalogos.tiposTramite({ organismoId: organismo?.id, signal }))),
  });
  const opciones = tipos.data ?? [];
  const sinTarifas = organismo !== null && tipos.isSuccess && opciones.length === 0;

  const calculo = useMutation({
    mutationFn: () => cliente.cotizaciones.cotizar({ organismoId: organismo?.id ?? "", tipoCodigo: tipo }),
    onError: (error) => {
      bloqueo.registrar(error);
    },
  });

  const lista = organismo !== null && tipo !== "";
  const nombreTipo = opciones.find((t) => t.code === tipo)?.name ?? tipo;
  const alCambiarSeleccion = () => {
    calculo.reset();
  };

  return (
    <>
      {calculo.isSuccess && organismo && <p className="mt-1 text-center text-xs text-foreground/70">{organismo.nombre} · {nombreTipo}</p>}
      <form
        className="mt-4"
        onSubmit={(e) => {
          e.preventDefault();
          if (lista && !calculo.isPending && !bloqueo.bloqueado && !calculo.isSuccess) calculo.mutate();
        }}
      >
        <div className="dashboard-registration-fields !grid-cols-1 sm:!grid-cols-2">
          <div className="dashboard-label" aria-busy={organismos.isPending}>
            Organismo de tránsito
            {organismos.isPending && <MobilityLoader />}
            {organismos.isError && (
              <div role="alert" className="flex flex-col items-start gap-2 text-sm">
                <p>{mensajeDe(organismos.error)}</p>
                <button type="button" className={boton("flit", "h-11 px-6", "none")} onClick={() => void organismos.refetch()} disabled={organismos.isFetching}>Reintentar</button>
              </div>
            )}
            {organismos.isSuccess && (
              <SelectorOrganismo
                organismos={organismos.data ?? []}
                valor={organismo}
                onChange={(o) => {
                  setOrganismo(o);
                  setTipo("");
                  alCambiarSeleccion();
                }}
              />
            )}
          </div>
          <div className="dashboard-label" aria-busy={tipos.isFetching && organismo !== null}>
            Tipo de trámite
            <SelectorTipoTramite
              etiqueta="Tipo de trámite"
              opciones={opciones}
              valor={tipo}
              onChange={(code) => {
                setTipo(code);
                alCambiarSeleccion();
              }}
              disabled={organismo === null || !tipos.isSuccess || sinTarifas}
              marcador={organismo === null ? "Primero elige un organismo" : tipos.isPending ? "Cargando..." : "Selecciona"}
            />
            <div aria-live="polite" className="text-xs font-normal">
              {tipos.isError && (
                <p role="alert">
                  {mensajeDe(tipos.error)}{" "}
                  <button type="button" className={boton("link", "h-auto p-0 text-xs", "none")} onClick={() => void tipos.refetch()}>Reintentar</button>
                </p>
              )}
              {sinTarifas && <p role="status">{MENSAJE_SIN_TARIFAS}</p>}
            </div>
          </div>
        </div>

        {!calculo.isSuccess && (
          <div className="mt-4 flex flex-col items-center gap-2">
            <button type="submit" className={boton("flit", "h-11 px-10", "none")} disabled={!lista || calculo.isPending || bloqueo.bloqueado}>
              {calculo.isPending && <span aria-hidden className="mobility-mini" />}
              Calcular
            </button>
            <div aria-live="polite" aria-busy={calculo.isPending}>
              {calculo.isPending && <p role="status" className="text-xs text-foreground/70">Calculando...</p>}
              {calculo.isError && <p role="alert" className="text-center text-sm text-alert">{mensajeDeCotizacion(calculo.error)}</p>}
              {bloqueo.bloqueado && <p role="status" className="text-center text-xs text-foreground/70">Podrás intentarlo de nuevo en {bloqueo.segundosRestantes} s.</p>}
            </div>
          </div>
        )}
      </form>

      {calculo.isSuccess && organismo && (
        <ResultadoCotizacion
          cotizacion={calculo.data}
          cliente={cliente}
          organismoId={organismo.id}
          tipoCodigo={tipo}
        />
      )}
    </>
  );
}

interface ResultadoProps {
  cotizacion: Cotizacion;
  cliente: ClientePortal;
  organismoId: string;
  tipoCodigo: string;
}

function ResultadoCotizacion({ cotizacion, cliente, organismoId, tipoCodigo }: ResultadoProps) {
  const [correo, setCorreo] = useState("");
  const llave = useRef<string | null>(null);
  const bloqueo = useBloqueo();

  const envio = useMutation({
    mutationFn: ({ llaveEnvio }: { llaveEnvio: string }) =>
      cliente.cotizaciones.enviarCorreo({ organismoId, tipoCodigo, correo: correo.trim() }, { idempotencyKey: llaveEnvio }),
    onSuccess: () => {
      setCorreo("");
      llave.current = null;
    },
    onError: (error) => {
      bloqueo.registrar(error);
    },
  });

  const puedeEnviar = correoValido(correo) && !envio.isPending && !bloqueo.bloqueado;

  return (
    <>
      <table className="mt-4 w-full overflow-hidden rounded-[9px] text-sm">
        <caption className="sr-only">Cotización del trámite</caption>
        <thead className="bg-primary text-primary-foreground">
          <tr>
            <th scope="col" className="p-2 text-center">Servicio / Trámite</th>
            <th scope="col" className="p-2 text-center">Valor</th>
          </tr>
        </thead>
        <tbody>
          {cotizacion.lineas.map((l) => (
            <tr key={l.codigo || l.nombre} className="border-b border-border">
              <td className="p-2">
                {l.nombre}
                {l.subtitulo && <span className="block text-xs font-light text-foreground/70">{l.subtitulo}</span>}
              </td>
              <td className="whitespace-nowrap p-2 text-right">{formatearPesos(l.valorCop)}</td>
            </tr>
          ))}
          <tr className="font-bold">
            <th scope="row" className="p-2 text-left">Total</th>
            <td className="whitespace-nowrap p-2 text-right">{formatearPesos(cotizacion.totalCop)}</td>
          </tr>
        </tbody>
      </table>

      <form
        className="mx-auto mt-4 grid max-w-lg gap-2 sm:grid-cols-[minmax(0,1fr)_auto]"
        onSubmit={(e) => {
          e.preventDefault();
          if (!puedeEnviar) return;
          llave.current ??= nuevaLlave();
          envio.mutate({ llaveEnvio: llave.current });
        }}
      >
        <input
          required
          type="email"
          aria-label="Correo electrónico"
          autoComplete="off"
          className="field"
          placeholder="correo@ejemplo.com"
          value={correo}
          onChange={(e) => {
            setCorreo(e.target.value);
            llave.current = null;
            envio.reset();
          }}
        />
        <button type="submit" className={boton("back", "h-11 px-6", "none")} disabled={!puedeEnviar}>
          {envio.isPending && <span aria-hidden className="mobility-mini" />}
          Enviar al correo
        </button>
      </form>
      <div aria-live="polite" className="mt-2 text-center text-xs">
        {envio.isPending && <p role="status" className="text-foreground/70">Enviando...</p>}
        {envio.isSuccess && <p role="status" className="text-foreground/70">Te enviamos la cotización a tu correo</p>}
        {envio.isError && <p role="alert" className="text-alert">{mensajeDe(envio.error)}</p>}
      </div>
    </>
  );
}
