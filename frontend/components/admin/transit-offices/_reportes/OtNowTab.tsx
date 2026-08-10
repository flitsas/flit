"use client";

// «Ahora mismo»: el estado de la cola y el movimiento del día.
//
// Esta pestaña NO tiene rango de fechas, y esa ausencia es el arreglo. Antes el selector de fechas
// presidía la pantalla justo encima de un bloque titulado «¿Cómo vamos hoy?»: moverlo no cambiaba un
// solo número de la cola, porque la cola describe el AHORA. Un control que promete filtrar y no
// filtra es peor que no tenerlo.
//
// El único indicador que sí necesita una ventana es la mediana de decisión. Se fija en 30 días y se
// DICE en la propia tarjeta, en vez de dejar que el usuario crea que la eligió él.

import { useCallback, useEffect, useState } from "react";
import {
  fetchOtDrilldown,
  fetchOtOperationalPanel,
  OT_DRILLDOWN_BUCKETS,
  type OtClientCompanyOption,
  type OtDrilldownBucket,
  type OtMetricsParams,
  type OtOperationalPanel,
} from "@/lib/api/ot-metrics";
import { DrilldownPanel, type DrilldownState } from "./DrilldownPanel";
import { EmpresaSelect, ModalidadSelect, defaultRange } from "./filters";
import { formatHours } from "./report-columns";
import { Bar, Bucket, Empty, ErrorNotice, PrimaryButton, Section, SubTitle, Tile } from "./shared";

/** Ventana de la mediana de decisión. Fija y declarada; ver la nota de cabecera. */
const VENTANA_MEDIANA_DIAS = 30;

export interface OtNowTabProps {
  transitOfficeId: string;
  companies: OtClientCompanyOption[];
}

export function OtNowTab({ transitOfficeId, companies }: OtNowTabProps) {
  const [modalidad, setModalidad] = useState("");
  const [clientTenantId, setClientTenantId] = useState("");
  const [panel, setPanel] = useState<OtOperationalPanel | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);
  const [drilldown, setDrilldown] = useState<DrilldownState | null>(null);

  const buildParams = useCallback((): OtMetricsParams => {
    const range = defaultRange();
    return {
      from: range.from,
      to: range.to,
      modalidad: modalidad || undefined,
      clientTenantId: clientTenantId || undefined,
      transitOfficeId,
    };
  }, [modalidad, clientTenantId, transitOfficeId]);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    try {
      setPanel(await fetchOtOperationalPanel(buildParams()));
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el panel.");
    } finally {
      setBusy(false);
    }
  }, [buildParams]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: patrón del repo, skeleton inmediato antes del fetch
    void load();
  }, [load]);

  // El drill-down se abre con los MISMOS filtros del panel, para que la lista nunca contradiga la
  // tarjeta que la originó.
  const openDrilldown = useCallback(
    (bucket: OtDrilldownBucket, label: string) => {
      setDrilldown({ bucket, label, loading: true, error: null, data: null });
      fetchOtDrilldown(buildParams(), bucket)
        .then((data) => setDrilldown({ bucket, label, loading: false, error: null, data }))
        .catch((e: unknown) =>
          setDrilldown({
            bucket,
            label,
            loading: false,
            error: e instanceof Error ? e.message : "No se pudo cargar el detalle.",
            data: null,
          }),
        );
    },
    [buildParams],
  );

  return (
    <div className="flex flex-col gap-6" data-testid="ot-now-tab">
      <Section
        title="Parámetros"
        testId="ot-now-filters"
        hint="Esta pestaña no lleva rango de fechas: siempre enseña el estado de este momento."
      >
        <div className="flex flex-wrap items-end gap-3">
          <ModalidadSelect value={modalidad} onChange={setModalidad} />
          <EmpresaSelect value={clientTenantId} companies={companies} onChange={setClientTenantId} />
          <PrimaryButton onClick={() => void load()} disabled={busy}>
            {busy ? "Cargando…" : "Actualizar"}
          </PrimaryButton>
        </div>
      </Section>

      {error && <ErrorNotice message={error} />}

      {panel && <OperationalPanel panel={panel} onOpenDrilldown={openDrilldown} />}

      <DrilldownPanel
        state={drilldown}
        transitOfficeId={transitOfficeId}
        onClose={() => setDrilldown(null)}
      />
    </div>
  );
}

function OperationalPanel({
  panel,
  onOpenDrilldown,
}: {
  panel: OtOperationalPanel;
  onOpenDrilldown: (bucket: OtDrilldownBucket, label: string) => void;
}) {
  const { movimiento, cola, antiguedad } = panel;
  const explicados = cola.porRevisar + cola.esperandoAsignarPlaca;

  return (
    <Section
      title="¿Cómo vamos hoy?"
      testId="ot-reports-operational"
      hint="Estado de la cola en este momento y movimiento del día calendario de Bogotá. No depende de ningún rango de fechas."
    >
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Tile
          value={movimiento.entregadosHoy}
          label="Entregados hoy"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.entregadosHoy, "Entregados hoy")}
        />
        <Tile
          value={movimiento.decididosHoy}
          label="Decididos hoy"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.decididosHoy, "Decididos hoy")}
        />
        <Tile
          value={movimiento.pendientesTotal}
          label="Pendientes en total"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.pendientes, "Pendientes en total")}
        />
        <Tile
          // Se formatea en vez de interpolar el número crudo: con datos reales, una mediana de
          // dos minutos salía como «0.03 h» — con punto decimal inglés y en una unidad en la que
          // el dato no significa nada.
          value={formatHours(movimiento.tiempoMedianoDecisionHoras)}
          label="Tiempo mediano de decisión"
          hint={`Últimos ${VENTANA_MEDIANA_DIAS} días`}
        />
      </div>

      <SubTitle>En qué está esperando cada trámite</SubTitle>
      <div className="flex flex-col gap-2">
        <Bar
          label="Por revisar"
          value={cola.porRevisar}
          total={movimiento.pendientesTotal}
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.porRevisar, "Por revisar")}
        />
        <Bar
          label="Esperando asignar placa"
          value={cola.esperandoAsignarPlaca}
          total={movimiento.pendientesTotal}
          onClick={() =>
            onOpenDrilldown(OT_DRILLDOWN_BUCKETS.esperandoPlaca, "Esperando asignar placa")
          }
        />
      </div>
      {cola.enEsperaDelCliente > 0 && (
        // El desglose no suma el total a propósito: se decidió no mostrar las esperas del cliente
        // pero sí dejarlas dentro del conteo. Decirlo evita que el número parezca un error.
        <p className="text-[11px] text-amber-700 dark:text-amber-400">
          El desglose suma {explicados} y hay {movimiento.pendientesTotal} pendientes: los{" "}
          <button
            type="button"
            className="underline underline-offset-2 hover:text-amber-900 dark:hover:text-amber-200"
            onClick={() =>
              onOpenDrilldown(OT_DRILLDOWN_BUCKETS.enEsperaDelCliente, "En espera del cliente")
            }
          >
            {cola.enEsperaDelCliente} restantes
          </button>{" "}
          esperan algo del cliente (SOAT o trámite pausado) y no se desglosan aquí.
        </p>
      )}

      <SubTitle>Antigüedad de lo pendiente</SubTitle>
      <div className="grid grid-cols-2 gap-3 lg:grid-cols-4">
        <Bucket
          value={antiguedad.hasta1Dia}
          label="0–1 día"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.hasta1Dia, "Antigüedad · 0–1 día")}
        />
        <Bucket
          value={antiguedad.entre2y3Dias}
          label="2–3 días"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.entre2y3Dias, "Antigüedad · 2–3 días")}
        />
        <Bucket
          value={antiguedad.entre4y7Dias}
          label="4–7 días"
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.entre4y7Dias, "Antigüedad · 4–7 días")}
        />
        <Bucket
          value={antiguedad.masDe7Dias}
          label="+7 días"
          hot
          onClick={() => onOpenDrilldown(OT_DRILLDOWN_BUCKETS.masDe7Dias, "Antigüedad · +7 días")}
        />
      </div>
      {antiguedad.prioritariosEstancados > 0 && (
        <button
          type="button"
          className="rounded-xl bg-amber-50 px-4 py-3 text-left text-xs text-amber-800 hover:bg-amber-100 dark:bg-amber-500/10 dark:text-amber-300 dark:hover:bg-amber-500/20"
          data-testid="ot-reports-prioritarios"
          onClick={() =>
            onOpenDrilldown(OT_DRILLDOWN_BUCKETS.prioritariosEstancados, "Prioritarios estancados")
          }
        >
          {antiguedad.prioritariosEstancados}{" "}
          {antiguedad.prioritariosEstancados === 1 ? "trámite marcado" : "trámites marcados"} como
          prioritario llevan más de 3 días sin tocar.
        </button>
      )}
      {movimiento.pendientesTotal === 0 && <Empty>No hay nada pendiente en la cola.</Empty>}
    </Section>
  );
}
