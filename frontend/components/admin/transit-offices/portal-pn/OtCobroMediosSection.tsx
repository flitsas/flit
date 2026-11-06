"use client";

// HU #14394 (Feature #14392, Épica #14297) — «Medios de pago» del portal: banderas de pasarela y PIN y, por cada tipo y
// medio, los switches «Valor del trámite» y «Valor de la gestión» con su monto. El monto vive en Precios y aquí solo se
// ve. El cliente se inyecta: sirve al OT, al SuperAdmin en un OT y a la web de persona (Plataforma, sin PIN).
// Siempre se muestra lo último guardado: ante un fallo no se cambia nada y se informa el motivo exacto.
import { useCallback, useEffect, useId, useMemo, useState, type ReactNode } from "react";
import { CreditCard, Lock, Store, type LucideIcon } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { InlineAlert } from "@/components/atom/InlineAlert";
import { StatusBadge } from "@/components/atom/StatusBadge";
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
const AYUDA_MEDIO: Record<MedioPago, string> = {
  PASARELA: "La persona paga en línea desde el portal.",
  PIN: "La persona paga en la caja del organismo con un PIN.",
};
const ICONO_MEDIO: Record<MedioPago, LucideIcon> = {
  PASARELA: CreditCard,
  PIN: Store,
};
const NOMBRE_CONCEPTO: Record<ConceptoCobro, string> = {
  TRAMITE: "Valor del trámite",
  GESTION: "Valor de la gestión",
};
const NOMBRE_CORTO: Record<ConceptoCobro, string> = {
  TRAMITE: "Trámite",
  GESTION: "Gestión",
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
  /** Lleva al lugar donde se define el monto. */
  onIrAPrecios: () => void;
  /** Cómo se llama ese lugar en esta pantalla: «Precios» en el OT, «Tarifas» en Plataforma. */
  nombrePrecios?: string;
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
  nombrePrecios = "Precios",
}: OtCobroMediosSectionProps) {
  const baseId = useId();
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
      setAviso({
        clave,
        mensaje: MOTIVO_SIN_MONTO.replace("Precios", nombrePrecios),
        conAccesoAPrecios: true,
      });
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

  const idNotaGestion = `${baseId}-gestion`;
  const tipos = cobro?.tipos ?? [];
  const gestionBloqueada = tipos.some((t) =>
    t.medios.some((m) => m.conceptos.some((c) => c.charge === "GESTION" && !c.editable)),
  );

  /** El monto es del trámite, no del medio: es el mismo en todos los medios. */
  function celdaMonto(tipo: TipoCobro, charge: ConceptoCobro): ReactNode {
    const monto = tipo.medios
      .flatMap((m) => m.conceptos)
      .find((c) => c.charge === charge)?.montoCop;
    return monto && monto > 0 ? (
      <span className="text-sm font-semibold tabular-nums text-flit-primary dark:text-white">
        {cop.format(monto)}
      </span>
    ) : (
      <span className="text-sm text-[#59677D] dark:text-white/70">Sin monto</span>
    );
  }

  function interruptor(tipo: TipoCobro, medio: MedioDeTipo, charge: ConceptoCobro): ReactNode {
    const c = medio.conceptos.find((x) => x.charge === charge);
    if (!c) return null;
    const clave = `${tipo.tipoTramiteId}|${medio.method}|${charge}`;
    const nombre = `${NOMBRE_CONCEPTO[charge]}, ${NOMBRE_MEDIO[medio.method]}, ${tipo.nombre}`;
    const motivoId = `${baseId}-motivo-${clave.replace(/[^a-zA-Z0-9]/g, "-")}`;
    const soloSuperAdmin = !c.editable && charge === "GESTION";
    const sinPermiso = !c.editable && charge !== "GESTION";
    const avisoFila = aviso?.clave === clave && aviso.conAccesoAPrecios ? aviso : null;
    const describedById = soloSuperAdmin
      ? idNotaGestion
      : sinPermiso || avisoFila
        ? motivoId
        : undefined;
    return (
      <div className="flex flex-col gap-1">
        <div className="flex items-center gap-2">
          <CobroSwitch
            nombre={nombre}
            checked={c.enabled}
            disabled={!c.editable}
            busy={guardando === clave}
            describedById={describedById}
            onChange={(next) => moverSwitch(tipo, medio, charge, next)}
          />
          <span className="flex flex-col leading-tight">
            <span className="flex items-center gap-1 text-sm font-semibold text-flit-primary dark:text-white">
              {NOMBRE_CORTO[charge]}
              {soloSuperAdmin && <Lock className="h-3 w-3" aria-hidden="true" />}
            </span>
            <span className="text-xs text-[#59677D] dark:text-white/70">
              {c.enabled ? "Se cobra" : "No se cobra"}
            </span>
          </span>
        </div>
        {(sinPermiso || avisoFila) && (
          <p id={motivoId} className="max-w-[14rem] text-xs text-[#59677D] dark:text-white/70">
            {avisoFila?.mensaje ?? MOTIVO_SIN_PERMISO}
            {avisoFila && (
              <>
                {" "}
                <PnLinkButton
                  onClick={onIrAPrecios}
                  ariaLabel={`Ir a ${nombrePrecios} para definir el monto de ${nombre}`}
                >
                  Ir a {nombrePrecios}
                </PnLinkButton>
              </>
            )}
          </p>
        )}
      </div>
    );
  }

  function celdaMedio(tipo: TipoCobro, method: MedioPago): ReactNode {
    const medio = tipo.medios.find((m) => m.method === method);
    if (!medio) return <span className="text-xs text-[#59677D] dark:text-white/70">No aplica</span>;
    const total = medio.conceptos.filter((c) => c.enabled).reduce((suma, c) => suma + c.montoCop, 0);
    return (
      <div className="flex flex-wrap items-start gap-x-5 gap-y-2">
        {interruptor(tipo, medio, "TRAMITE")}
        {interruptor(tipo, medio, "GESTION")}
        <div className="flex flex-col items-start gap-1 leading-tight">
          <span className="text-sm font-semibold tabular-nums text-flit-primary dark:text-white">
            {total > 0 ? cop.format(total) : "Sin cobro"}
          </span>
          {medio.ofrecido ? (
            <StatusBadge label="Se ofrece" tone="success" />
          ) : (
            <StatusBadge label="No se ofrece" tone="neutral" />
          )}
        </div>
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
    { key: "TRAMITE", header: NOMBRE_CONCEPTO.TRAMITE, render: (t) => celdaMonto(t, "TRAMITE") },
    { key: "GESTION", header: NOMBRE_CONCEPTO.GESTION, render: (t) => celdaMonto(t, "GESTION") },
    ...medios.map<DataTableColumn<TipoCobro>>((m) => ({
      key: m.method,
      header: `Se cobra en ${NOMBRE_MEDIO[m.method]}`,
      render: (t) => celdaMedio(t, m.method),
    })),
  ];

  function tarjetaMedio(m: { method: MedioPago; enabled: boolean }): ReactNode {
    const Icono = ICONO_MEDIO[m.method];
    const ofrecidos = tipos.filter(
      (t) => t.medios.find((x) => x.method === m.method)?.ofrecido,
    ).length;
    return (
      <div
        key={m.method}
        className="flex flex-wrap items-center gap-4 rounded-[14px] border border-[#DFE5ED] bg-white p-4 dark:border-white/10 dark:bg-[#162744]"
      >
        <span
          aria-hidden="true"
          className="flex h-11 w-11 shrink-0 items-center justify-center rounded-full bg-[#E6EFFC] text-[#557EFF] dark:bg-white/10 dark:text-white"
        >
          <Icono className="h-5 w-5" />
        </span>
        <div className="min-w-[12rem] flex-1">
          <p className="text-base font-semibold text-flit-primary dark:text-white">
            {NOMBRE_MEDIO[m.method]}
          </p>
          <p className="text-sm text-[#59677D] dark:text-white/70">{AYUDA_MEDIO[m.method]}</p>
          <p className="text-xs font-semibold text-[#59677D] dark:text-white/70">
            {m.enabled
              ? `${ofrecidos} de ${tipos.length} trámites se ofrecen`
              : "La persona no ve este medio. Sus interruptores se aplican cuando lo enciendas."}
          </p>
        </div>
        <div className="flex items-center gap-2">
          <span className="text-sm font-semibold text-flit-primary dark:text-white">
            {m.enabled ? "Activado" : "Desactivado"}
          </span>
          <CobroSwitch
            nombre={NOMBRE_MEDIO[m.method]}
            checked={m.enabled}
            busy={guardando === `medio|${m.method}`}
            onChange={(next) => moverMedio(m.method, next)}
          />
        </div>
      </div>
    );
  }

  return (
    <section aria-labelledby="pn-cobro-medios-titulo" className="flex flex-col gap-4">
      <PnSeccionHeader
        id="pn-cobro-medios-titulo"
        titulo="Medios de pago"
        descripcion={`Cada valor es uno solo y se cambia en ${nombrePrecios}. Aquí eliges si se cobra en cada medio de pago.`}
        accion={
          <PnLinkButton onClick={onIrAPrecios} ariaLabel={`Ir a ${nombrePrecios}`}>
            Ir a {nombrePrecios}
          </PnLinkButton>
        }
      />

      <UiStateBoundary
        status={status}
        emptyMessage={`Aún no hay trámites con cobro para configurar. Define sus valores en ${nombrePrecios} y vuelve aquí.`}
        errorMessage="No se pudieron cargar los medios de pago."
        onRetry={() => {
          setStatus("loading");
          cargar().catch(() => setStatus("error"));
        }}
      >
        <div className="flex flex-col gap-4">
          <div className={medios.length > 1 ? "grid gap-4 lg:grid-cols-2" : "grid gap-4"}>
            {medios.map(tarjetaMedio)}
          </div>

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

          {gestionBloqueada && (
            <p
              id={idNotaGestion}
              className="flex items-center gap-1.5 text-xs text-[#59677D] dark:text-white/70"
            >
              <Lock className="h-3.5 w-3.5 shrink-0" aria-hidden="true" />
              {MOTIVO_GESTION_SOLO_SUPERADMIN}
            </p>
          )}

          <DataTable<TipoCobro>
            ariaLabel="Valores por trámite y si se cobran en cada medio de pago"
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

          <p className="text-xs text-[#59677D] dark:text-white/70">
            El total de cada medio suma lo que se cobra en él. No incluye el envío ni las transformaciones: se suman cuando aplican.
            {!conPin && " Si un trámite no tiene valor general, se muestra el mayor valor por organismo."}
          </p>
        </div>
      </UiStateBoundary>
    </section>
  );
}
