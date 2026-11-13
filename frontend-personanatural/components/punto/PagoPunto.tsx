"use client";

// HU #14451 / #14452 — Paso Pago del canal OT (solo pago en caja). Pasa la solicitud a pendiente_pago (POST /punto/pago/iniciar),
// sondea GET /punto/pago y, según lo que diga el servidor: espera las identidades, muestra el desglose, habilita el cobro en caja,
// confirma un total $0 o informa el motivo que impide pagar. Al aparecer `pago` (la caja confirmó) avisa con `onPagado`.
// El selector de medio queda listo para sumar otro medio: hoy `MEDIOS_DE_PAGO` solo trae la caja y la pasarela no se muestra.
import { useMutation, useQuery, useQueryClient } from "@tanstack/react-query";
import { AlertTriangle, Check, Copy } from "lucide-react";
import { useEffect, useRef, useState } from "react";
import { MobilityLoader } from "@/components/MobilityLoader";
import { boton } from "@/components/landing/estilos";
import { Btn, Card } from "@/components/tramite/piezas";
import { claveSolicitud, useSolicitud } from "@/components/tramite/useSolicitud";
import type { ClientePortal, DefinicionTipo, EstadoPagoPunto } from "@/lib/api/cliente";
import { ApiError, mensajeDeFallo } from "@/lib/api/errores";
import { formatearPesos } from "@/lib/formato";
import { ETIQUETA_VALIDACION } from "@/lib/tramite/actores";

/** Cada cuánto se consulta el estado del pago (identidades, caja confirmada). */
export const INTERVALO_SONDEO_PAGO_MS = 4000;

/** Medios del paso Pago. Agregar aquí otro medio (con su rama en `ZonaDeCobro`) lo habilita sin tocar el resto del paso. */
export const MEDIOS_DE_PAGO = [{ id: "caja", etiqueta: "Pago en caja" }] as const;
type MedioId = (typeof MEDIOS_DE_PAGO)[number]["id"];

type Lectura = { estado: EstadoPagoPunto; faltaInfo: string | null };

const claveEstadoPago = (solicitudId: string) => ["pn", "punto", "pago", solicitudId] as const;
const enBorrador = (estado: string) => estado === "borrador" || estado === "pendiente_validacion";

type Props = {
  cliente: ClientePortal;
  solicitudId: string;
  tipoCodigo: string;
  /** «Volver»: regresa al Resumen. */
  onVolver: () => void;
  /** «Ir a Actores» desde la lista de identidades pendientes. */
  onIrActores: () => void;
  /** El pago quedó confirmado (caja o total $0): sigue el cierre. */
  onPagado: () => void;
};

export function PagoPunto({ cliente, solicitudId, tipoCodigo, onVolver, onIrActores, onPagado }: Props) {
  const queryClient = useQueryClient();
  const [medio] = useState<MedioId>(MEDIOS_DE_PAGO[0].id);
  const [falloAccion, setFalloAccion] = useState<string | null>(null);
  const avisado = useRef(false);

  // Etiquetas de los roles para nombrar a quien falta (la definición ya está en caché desde el Resumen).
  const organismoId = useSolicitud(cliente, solicitudId).data?.organismoId ?? "";
  const definicion = useQuery<DefinicionTipo>({
    queryKey: ["pn", "punto", "definicion", tipoCodigo, organismoId],
    queryFn: ({ signal }) => cliente.catalogos.definicion(tipoCodigo, { organismoId, signal }),
    enabled: organismoId !== "",
    staleTime: Infinity,
    retry: false,
  });

  const lectura = useQuery<Lectura>({
    queryKey: claveEstadoPago(solicitudId),
    queryFn: async ({ signal }) => {
      let estado = await cliente.punto.pago.estado({ signal });
      // Con todas las identidades validadas, la solicitud pasa a pendiente_pago (idempotente). Si algo falta lo dice el servidor.
      if (enBorrador(estado.estado) && estado.identidadesPendientes.length === 0) {
        try {
          await cliente.punto.pago.iniciar({ signal });
          estado = await cliente.punto.pago.estado({ signal });
          // La solicitud ya no es editable: el Resumen (solo lectura) y el cierre deben leer el estado nuevo.
          void queryClient.invalidateQueries({ queryKey: claveSolicitud(solicitudId) });
        } catch (error) {
          if (error instanceof ApiError && error.code === "requisitos_incompletos") {
            return { estado, faltaInfo: "Falta información del trámite. Vuelve al Resumen y a Requisitos para completarla." };
          }
          if (error instanceof ApiError && error.code === "identity_pending") {
            return { estado: { ...estado, identidadesPendientes: error.fallas?.partes ?? [] }, faltaInfo: null };
          }
          throw error;
        }
      }
      return { estado, faltaInfo: null };
    },
    retry: false,
    refetchInterval: (consulta) => {
      const datos = consulta.state.data;
      if (consulta.state.error || datos?.faltaInfo || datos?.estado.pago) return false;
      return INTERVALO_SONDEO_PAGO_MS;
    },
  });

  const estado = lectura.data?.estado;
  const refrescar = () => queryClient.invalidateQueries({ queryKey: claveEstadoPago(solicitudId) });

  // Cuando la caja confirma (o se confirma el $0) se refresca la solicitud —ya no es editable— y se pasa al cierre.
  useEffect(() => {
    if (!estado?.pago || avisado.current) return;
    avisado.current = true;
    void queryClient.refetchQueries({ queryKey: claveSolicitud(solicitudId) }).finally(onPagado);
  }, [estado?.pago, queryClient, solicitudId, onPagado]);

  const generar = useMutation({
    mutationFn: () => cliente.punto.pago.caja(),
    onMutate: () => setFalloAccion(null),
    onSuccess: () => refrescar(),
    onError: (error) => {
      setFalloAccion(mensajeDeFallo(error));
      if (error instanceof ApiError && error.code === "total_cero") void refrescar();
    },
  });
  const confirmar = useMutation({
    mutationFn: () => cliente.punto.pago.confirmar(),
    onMutate: () => setFalloAccion(null),
    onSuccess: () => refrescar(),
    onError: (error) => {
      setFalloAccion(mensajeDeFallo(error));
      if (error instanceof ApiError && error.code === "total_mayor_cero") void refrescar();
    },
  });

  const volver = (
    <Btn tone="secondary" onClick={onVolver}>
      Volver
    </Btn>
  );

  if (lectura.isPending) {
    return (
      <Card title="Pago" id="punto-pago-cargando">
        <div className="relative min-h-[12rem]" aria-busy="true">
          <MobilityLoader mensaje="Cargando el pago…" />
        </div>
      </Card>
    );
  }

  if (lectura.error || !estado) {
    return (
      <Card title="Pago" id="punto-pago-error">
        <Alerta id="punto-pago-error-mensaje">{mensajeDeFallo(lectura.error)}</Alerta>
        <div className="mt-4 flex flex-wrap items-center justify-end gap-3">
          {volver}
          <Btn onClick={() => void lectura.refetch()}>Reintentar</Btn>
        </div>
      </Card>
    );
  }

  const faltaInfo = lectura.data?.faltaInfo;
  const pendientes = estado.identidadesPendientes;

  // #14452 — faltan identidades: se dice quién y no hay botón de pago. El sondeo habilita el paso al validar la última.
  if (pendientes.length > 0 && !estado.pago) {
    return (
      <Card title="Pago" id="punto-pago-identidades">
        <div role="status" className="space-y-3" aria-live="polite">
          <p className="text-sm font-semibold">Falta validar la identidad de:</p>
          <ul className="list-disc space-y-1 pl-5 text-sm" aria-label="Personas pendientes de validar">
            {pendientes.map((p) => {
              const rol = definicion.data?.actores.find((r) => r.rol === p.parte);
              const nombre = `${rol?.etiqueta || p.parte}${rol && rol.max > 1 ? ` ${p.ordinal}` : ""}`;
              return (
                <li key={`${p.parte}-${p.ordinal}`}>
                  {nombre}: validación {ETIQUETA_VALIDACION[p.estado] ?? p.estado}
                </li>
              );
            })}
          </ul>
          <p className="text-sm text-muted-foreground">El pago se habilita solo cuando todos validen. No hace falta recargar.</p>
        </div>
        <div className="mt-4 flex flex-wrap items-center justify-end gap-3">
          {volver}
          <button type="button" className={boton("link", "min-h-11 px-3", "none")} onClick={onIrActores}>
            Ir a Actores
          </button>
        </div>
      </Card>
    );
  }

  if (faltaInfo) {
    return (
      <Card title="Pago" id="punto-pago-incompleto">
        <Alerta id="punto-pago-incompleto-mensaje">{faltaInfo}</Alerta>
        <div className="mt-4 flex justify-end">{volver}</div>
      </Card>
    );
  }

  if (estado.pago) {
    return (
      <Card title="Pago" id="punto-pago-confirmado">
        <p role="status" className="flex items-center gap-2 text-sm font-semibold text-success">
          <Check aria-hidden className="h-5 w-5" />
          Pago recibido. Continuando…
        </p>
      </Card>
    );
  }

  const total = estado.totalCop ?? null;
  const ocupado = generar.isPending || confirmar.isPending;

  return (
    <Card title="Pago" id="punto-pago">
      <div className="space-y-4" aria-busy={ocupado}>
        {estado.desglose && estado.desglose.length > 0 && (
          <section aria-labelledby="punto-pago-desglose-titulo" className="rounded-[9px] border border-border p-4">
            <h4 id="punto-pago-desglose-titulo" className="mb-3 text-lg font-bold text-primary">
              Desglose
            </h4>
            <ul className="space-y-3">
              {estado.desglose.map((linea) => (
                <li key={linea.codigo} className="flex justify-between gap-4">
                  <span>{linea.nombre}</span>
                  <b>{formatearPesos(linea.valorCop)}</b>
                </li>
              ))}
              <li className="flex justify-between gap-4 border-t border-border pt-3 text-lg">
                <span className="font-bold">Total a pagar</span>
                <b className="text-primary">{total === null ? "—" : formatearPesos(total)}</b>
              </li>
            </ul>
          </section>
        )}

        {estado.bloqueo ? (
          <Alerta id="punto-pago-bloqueo">{estado.bloqueo.message}</Alerta>
        ) : (
          <ZonaDeCobro
            medio={medio}
            estado={estado}
            ocupado={ocupado}
            generando={generar.isPending}
            confirmando={confirmar.isPending}
            onGenerar={() => generar.mutate()}
            onConfirmar={() => confirmar.mutate()}
          />
        )}

        {falloAccion && <Alerta id="punto-pago-fallo">{falloAccion}</Alerta>}
      </div>
      <div className="mt-4 flex flex-wrap items-center justify-end gap-3">
        <button type="button" className={boton("back", "min-h-11 px-6", "none")} onClick={onVolver} disabled={ocupado}>
          Volver
        </button>
      </div>
    </Card>
  );
}

function Alerta({ children, id }: { children: string; id: string }) {
  return (
    <div id={id} role="alert" className="flex gap-2 rounded-[9px] border border-alert bg-alert/10 p-3 text-sm text-alert">
      <AlertTriangle aria-hidden className="h-5 w-5 shrink-0" />
      <span>{children}</span>
    </div>
  );
}

function ZonaDeCobro({
  medio,
  estado,
  ocupado,
  generando,
  confirmando,
  onGenerar,
  onConfirmar,
}: {
  medio: MedioId;
  estado: EstadoPagoPunto;
  ocupado: boolean;
  generando: boolean;
  confirmando: boolean;
  onGenerar: () => void;
  onConfirmar: () => void;
}) {
  // Un solo medio hoy (caja): el selector solo aparece cuando haya más de uno.
  if (medio !== "caja") return null;

  if (generando || confirmando) {
    return (
      <div className="relative min-h-[8rem]">
        <MobilityLoader mensaje={generando ? "Generando el pago en caja…" : "Confirmando…"} />
      </div>
    );
  }

  if (estado.totalCop === 0) {
    return (
      <div className="space-y-3">
        <p className="text-sm">Este trámite no tiene valor a pagar. Confirma para continuar.</p>
        <Btn onClick={onConfirmar} disabled={ocupado}>
          Confirmar
        </Btn>
      </div>
    );
  }

  const cobro = estado.cobro;
  if (cobro?.estado === "vigente") return <CobroVigente codigo={cobro.codigo} total={estado.totalCop ?? null} />;

  return (
    <div className="space-y-3">
      {cobro?.estado === "vencido" && (
        <p role="status" className="text-sm font-semibold text-alert">
          El pago en caja venció. Genera uno nuevo; el valor se actualiza si cambió.
        </p>
      )}
      <Btn onClick={onGenerar} disabled={ocupado}>
        {cobro?.estado === "vencido" ? "Generar pago en caja de nuevo" : "Generar pago en caja"}
      </Btn>
    </div>
  );
}

function CobroVigente({ codigo, total }: { codigo: string; total: number | null }) {
  const [copiado, setCopiado] = useState(false);
  const copiar = async () => {
    try {
      await navigator.clipboard.writeText(codigo);
      setCopiado(true);
    } catch {
      setCopiado(false);
    }
  };
  return (
    <section aria-labelledby="punto-pago-caja-titulo" className="space-y-3 rounded-[9px] border border-border p-4">
      <div className="flex flex-wrap items-center justify-between gap-2">
        <h4 id="punto-pago-caja-titulo" className="text-lg font-bold text-primary">
          Pago en caja
        </h4>
        <span className="badge-arrow inline-block bg-warning/10 text-xs font-semibold text-warning">Pendiente de pago</span>
      </div>
      <p className="text-sm">Ve a la caja del organismo y dicta este código:</p>
      <div className="flex flex-wrap items-center gap-3">
        <p aria-label="Código de pago en caja" className="text-3xl font-bold tracking-wider" data-testid="codigo-cobro">
          {codigo}
        </p>
        <button type="button" className={boton("back", "min-h-11 px-4", "none")} onClick={() => void copiar()}>
          <Copy aria-hidden />
          {copiado ? "Copiado" : "Copiar código"}
        </button>
      </div>
      {total !== null && (
        <p className="text-sm">
          Total a pagar: <b>{formatearPesos(total)}</b>
        </p>
      )}
      <p className="inline-flex w-fit rounded-full bg-warning px-3 py-1 text-sm font-semibold text-secondary">
        Vale hasta hoy a las 11:59 p. m.
      </p>
      <p role="status" className="text-sm text-muted-foreground">
        Esperando la confirmación de la caja. Esta pantalla avanza sola.
      </p>
    </section>
  );
}
