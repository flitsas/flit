"use client";

// HU #14394 (Feature #14392, Épica #14297) — «Medios de pago» del portal: banderas de pasarela y PIN y, por cada tipo y
// medio, los switches «Valor del trámite» y «Valor de la gestión» con su monto. El monto vive en Precios y aquí solo se
// ve. El cliente se inyecta: sirve al OT, al SuperAdmin en un OT y a la web de persona (Plataforma, sin PIN).
// Siempre se muestra lo último guardado: ante un fallo no se cambia nada y se informa el motivo exacto.
import { useCallback, useEffect, useMemo, useState, type ReactNode } from "react";
import { AlertTriangle } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  PnLinkButton,
  PnSeccionHeader,
} from "@/components/admin/plataforma/persona-natural/pn-componentes";
import { pnErrorMessage } from "@/components/admin/plataforma/persona-natural/pnErrors";
import type {
  CobroApi,
  CobroPortal,
  ConceptoCobro,
  MedioDeTipo,
  MedioPago,
  TipoCobro,
} from "@/lib/api/admin-ot-portal-cobro";

export const COBRO_ON = "#8CC63F";
export const COBRO_OFF = "#91A5B5";

export const MOTIVO_SIN_MONTO =
  "Este concepto no tiene un monto mayor que cero. Define el valor en Precios y vuelve a encenderlo.";
export const MOTIVO_GESTION_SOLO_SUPERADMIN =
  "El valor de la gestión (servicio FLIT y envío) solo lo cambia el superadministrador de FLIT.";
const MOTIVO_SIN_PERMISO = "No tienes permiso para cambiar este cobro.";

const NOMBRE_MEDIO: Record<MedioPago, string> = {
  PASARELA: "Pasarela de pago",
  PIN: "PIN en caja",
};
const NOMBRE_CONCEPTO: Record<ConceptoCobro, string> = {
  TRAMITE: "Valor del trámite",
  GESTION: "Valor de la gestión",
};

const cop = new Intl.NumberFormat("es-CO", {
  style: "currency",
  currency: "COP",
  maximumFractionDigits: 0,
});

export interface OtCobroMediosSectionProps {
  api: CobroApi;
  /** La web de persona no ofrece PIN. */
  conPin?: boolean;
  /** Lleva al lugar donde se define el monto (Precios). */
  onIrAPrecios: () => void;
}

/** Interruptor accesible: nombre y estado también en texto, colores de la épica. */
export function CobroSwitch({
  nombre,
  checked,
  disabled = false,
  busy = false,
  onChange,
  describedById,
}: {
  nombre: string;
  checked: boolean;
  disabled?: boolean;
  busy?: boolean;
  onChange: (next: boolean) => void;
  describedById?: string;
}) {
  return (
    <button
      type="button"
      role="switch"
      aria-checked={checked}
      aria-label={nombre}
      aria-describedby={describedById}
      aria-busy={busy || undefined}
      disabled={disabled || busy}
      onClick={() => onChange(!checked)}
      className="relative inline-flex h-6 w-11 shrink-0 items-center rounded-full transition focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 disabled:cursor-not-allowed disabled:opacity-60"
      style={{ background: checked ? COBRO_ON : COBRO_OFF }}
    >
      <span
        aria-hidden="true"
        className="absolute top-0.5 h-5 w-5 rounded-full bg-white shadow transition-all"
        style={{ left: checked ? "calc(100% - 22px)" : "2px" }}
      />
    </button>
  );
}

interface Aviso {
  /** Clave `tipo|medio|concepto`. */
  clave: string;
  mensaje: string;
  conAccesoAPrecios?: boolean;
}

export function OtCobroMediosSection({
  api,
  conPin = true,
  onIrAPrecios,
}: OtCobroMediosSectionProps) {
  const { show } = useToast();
  const [cobro, setCobro] = useState<CobroPortal | null>(null);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [guardando, setGuardando] = useState<string | null>(null);
  const [aviso, setAviso] = useState<Aviso | null>(null);
  const [error, setError] = useState<string | null>(null);
  const pg = usePaginacion("pn.cobro.medios");

  const cargar = useCallback(
    async (signal?: AbortSignal) => {
      const datos = await api.obtener(signal);
      if (signal?.aborted) return null;
      setCobro(datos);
      setStatus(datos.tipos.length === 0 ? "empty" : "ready");
      return datos;
    },
    [api],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    setStatus("loading");
    cargar(controller.signal).catch(() => {
      if (!controller.signal.aborted) setStatus("error");
    });
    return () => controller.abort();
  }, [cargar]);

  const medios = useMemo(
    () => (cobro?.medios ?? []).filter((m) => conPin || m.method !== "PIN"),
    [cobro, conPin],
  );
  const metodos = useMemo<MedioPago[]>(
    () => (conPin ? ["PASARELA", "PIN"] : ["PASARELA"]),
    [conPin],
  );

  async function guardar(
    clave: string,
    accion: () => Promise<unknown>,
    despues?: (datos: CobroPortal) => string | null,
  ) {
    setGuardando(clave);
    setError(null);
    setAviso(null);
    try {
      await accion();
    } catch (err) {
      // Nada cambió en pantalla: sigue lo último guardado.
      setError(pnErrorMessage(err));
      setGuardando(null);
      return;
    }
    try {
      const datos = await cargar();
      show("Cambio guardado.", "success");
      const texto = datos && despues ? despues(datos) : null;
      if (texto) setAviso({ clave, mensaje: texto });
    } catch {
      setError(
        "Se guardó el cambio, pero no se pudo recargar la pantalla. Actualiza la página para verlo.",
      );
    } finally {
      setGuardando(null);
    }
  }

  function moverMedio(method: MedioPago, enabled: boolean) {
    void guardar(`medio|${method}`, () => api.guardarMedio({ method, enabled }));
  }

  function moverSwitch(
    tipo: TipoCobro,
    medio: MedioDeTipo,
    charge: ConceptoCobro,
    enabled: boolean,
  ) {
    const concepto = medio.conceptos.find((c) => c.charge === charge);
    const clave = `${tipo.tipoTramiteId}|${medio.method}|${charge}`;
    if (enabled && concepto && concepto.montoCop <= 0) {
      setError(null);
      setAviso({ clave, mensaje: MOTIVO_SIN_MONTO, conAccesoAPrecios: true });
      return;
    }
    void guardar(
      clave,
      () =>
        api.guardarSwitch({
          tipoTramiteId: tipo.tipoTramiteId,
          method: medio.method,
          charge,
          enabled,
        }),
      (datos) => {
        const actual = datos.tipos
          .find((t) => t.tipoTramiteId === tipo.tipoTramiteId)
          ?.medios.find((m) => m.method === medio.method);
        return actual && actual.conceptos.every((c) => !c.enabled)
          ? `Se guardó. Con los dos valores apagados, ${NOMBRE_MEDIO[medio.method]} no se ofrecerá para ${tipo.nombre}.`
          : null;
      },
    );
  }

  function celdaMedio(tipo: TipoCobro, method: MedioPago): ReactNode {
    const medio = tipo.medios.find((m) => m.method === method);
    if (!medio) return <span className="text-xs text-[#59677D] dark:text-white/70">No aplica</span>;
    const apagados = medio.conceptos.length > 0 && medio.conceptos.every((c) => !c.enabled);
    return (
      <div className="flex flex-col gap-2">
        <span className="text-xs font-semibold text-[#59677D] dark:text-white/70">
          {medio.ofrecido ? "Se ofrece en el portal" : "No se ofrece en el portal"}
        </span>
        {(["TRAMITE", "GESTION"] as ConceptoCobro[]).map((charge) => {
          const c = medio.conceptos.find((x) => x.charge === charge);
          if (!c) return null;
          const clave = `${tipo.tipoTramiteId}|${method}|${charge}`;
          const nombre = `${NOMBRE_CONCEPTO[charge]}, ${NOMBRE_MEDIO[method]}, ${tipo.nombre}`;
          const motivoId = `cobro-motivo-${clave.replace(/[^a-zA-Z0-9]/g, "-")}`;
          const motivo = !c.editable
            ? charge === "GESTION"
              ? MOTIVO_GESTION_SOLO_SUPERADMIN
              : MOTIVO_SIN_PERMISO
            : null;
          const avisoFila = aviso?.clave === clave ? aviso : null;
          return (
            <div key={charge} className="flex flex-col gap-1">
              <div className="flex items-center gap-2">
                <CobroSwitch
                  nombre={nombre}
                  checked={c.enabled}
                  disabled={!c.editable}
                  busy={guardando === clave}
                  describedById={motivo || avisoFila ? motivoId : undefined}
                  onChange={(next) => moverSwitch(tipo, medio, charge, next)}
                />
                <span className="text-sm text-flit-primary dark:text-white">
                  {NOMBRE_CONCEPTO[charge]}:{" "}
                  <span className="font-semibold">
                    {c.montoCop > 0 ? cop.format(c.montoCop) : "Sin monto"}
                  </span>
                  <span className="ml-2 text-xs font-semibold text-[#59677D] dark:text-white/70">
                    {c.enabled ? "Activado" : "Desactivado"}
                  </span>
                </span>
              </div>
              {(motivo || avisoFila) && (
                <p id={motivoId} className="ml-[3.25rem] text-xs text-[#59677D] dark:text-white/70">
                  {avisoFila?.mensaje ?? motivo}
                  {avisoFila?.conAccesoAPrecios && (
                    <>
                      {" "}
                      <PnLinkButton
                        onClick={onIrAPrecios}
                        ariaLabel={`Ir a Precios para definir el monto de ${nombre}`}
                      >
                        Ir a Precios
                      </PnLinkButton>
                    </>
                  )}
                </p>
              )}
            </div>
          );
        })}
        {apagados && (
          <p className="flex items-center gap-1.5 text-xs font-semibold text-[#C2410C]">
            <AlertTriangle className="h-4 w-4" aria-hidden="true" />
            Este medio no se ofrecerá: sus dos valores están apagados.
          </p>
        )}
      </div>
    );
  }

  const columnas: DataTableColumn<TipoCobro>[] = [
    {
      key: "tipo",
      header: "Trámite",
      render: (t) => (
        <span className="font-semibold text-flit-primary dark:text-white">{t.nombre}</span>
      ),
    },
    ...metodos.map<DataTableColumn<TipoCobro>>((m) => ({
      key: m,
      header: NOMBRE_MEDIO[m],
      render: (t) => celdaMedio(t, m),
    })),
  ];

  const tipos = cobro?.tipos ?? [];

  return (
    <section aria-labelledby="pn-cobro-medios-titulo" className="flex flex-col gap-4">
      <PnSeccionHeader
        id="pn-cobro-medios-titulo"
        titulo="Medios de pago"
        descripcion="Elige qué medios ofrece el portal y qué se cobra en cada uno. El monto se define en Precios; aquí solo se ve."
      />

      <UiStateBoundary
        status={status}
        emptyMessage="Aún no hay trámites con cobro para configurar. Define sus precios y vuelve aquí."
        errorMessage="No se pudieron cargar los medios de pago."
        onRetry={() => {
          setStatus("loading");
          cargar().catch(() => setStatus("error"));
        }}
      >
        <div className="flex flex-col gap-4">
          <fieldset className="flex flex-wrap gap-6 rounded-[14px] border border-[#DFE5ED] px-4 py-3 dark:border-white/10">
            <legend className="px-1 text-sm font-semibold text-flit-primary dark:text-white">
              Medios que ofrece el portal
            </legend>
            {medios.map((m) => (
              <div key={m.method} className="flex items-center gap-2">
                <CobroSwitch
                  nombre={NOMBRE_MEDIO[m.method]}
                  checked={m.enabled}
                  busy={guardando === `medio|${m.method}`}
                  onChange={(next) => moverMedio(m.method, next)}
                />
                <span className="text-sm text-flit-primary dark:text-white">
                  {NOMBRE_MEDIO[m.method]}
                  <span className="ml-2 text-xs font-semibold text-[#59677D] dark:text-white/70">
                    {m.enabled ? "Activado" : "Desactivado"}
                  </span>
                </span>
              </div>
            ))}
          </fieldset>

          {aviso && !aviso.conAccesoAPrecios && (
            <InlineAlert tone="warning" title="Medio sin cobro">
              {aviso.mensaje}
            </InlineAlert>
          )}
          {error && (
            <InlineAlert tone="error" title="No se guardó el cambio">
              {error} Se conserva lo último guardado.
            </InlineAlert>
          )}

          <DataTable<TipoCobro>
            ariaLabel="Valores que se cobran por trámite y medio de pago"
            columns={columnas}
            rows={pg.paginar(tipos)}
            getRowKey={(t) => t.tipoTramiteId}
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: tipos.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </div>
      </UiStateBoundary>
    </section>
  );
}
