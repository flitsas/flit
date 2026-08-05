"use client";

// «Análisis»: por qué rechazo, cómo decide mi equipo y qué calidad me llega.
//
// Todo lo de aquí SÍ depende del rango, así que el rango vive en esta pestaña y no encima del panel
// operativo, que describe el ahora. Es el otro lado del mismo arreglo.

import { useCallback, useEffect, useState } from "react";
import {
  fetchOtPerformance,
  fetchOtRejectionReasons,
  type OtClientCompanyOption,
  type OtMetricsParams,
  type OtPerformance,
  type OtRejectionReasons,
} from "@/lib/api/ot-metrics";
import {
  DateRangeFields,
  EmpresaSelect,
  ModalidadSelect,
  RangePresets,
  defaultRange,
  type DateRange,
} from "./filters";
import { Bar, Empty, ErrorNotice, PrimaryButton, Section, Table, Tile } from "./shared";

export interface OtAnalysisTabProps {
  transitOfficeId: string;
  companies: OtClientCompanyOption[];
}

export function OtAnalysisTab({ transitOfficeId, companies }: OtAnalysisTabProps) {
  const [range, setRange] = useState<DateRange>(() => defaultRange());
  const [modalidad, setModalidad] = useState("");
  const [clientTenantId, setClientTenantId] = useState("");

  const [performance, setPerformance] = useState<OtPerformance | null>(null);
  const [reasons, setReasons] = useState<OtRejectionReasons | null>(null);
  const [busy, setBusy] = useState(false);
  const [error, setError] = useState<string | null>(null);

  const load = useCallback(async () => {
    setBusy(true);
    setError(null);
    const params: OtMetricsParams = {
      from: range.from,
      to: range.to,
      modalidad: modalidad || undefined,
      clientTenantId: clientTenantId || undefined,
      transitOfficeId,
    };
    try {
      // En paralelo: son dos lecturas independientes y encadenarlas solo sumaría espera.
      const [perf, rea] = await Promise.all([
        fetchOtPerformance(params),
        fetchOtRejectionReasons(params),
      ]);
      setPerformance(perf);
      setReasons(rea);
    } catch (e: unknown) {
      setError(e instanceof Error ? e.message : "No se pudo cargar el análisis.");
    } finally {
      setBusy(false);
    }
  }, [range.from, range.to, modalidad, clientTenantId, transitOfficeId]);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: patrón del repo, skeleton inmediato antes del fetch
    void load();
  }, [load]);

  return (
    <div className="flex flex-col gap-6" data-testid="ot-analysis-tab">
      <div className="flex flex-col gap-3">
        <RangePresets range={range} onChange={setRange} />
        <div className="flex flex-wrap items-end gap-3">
          <DateRangeFields range={range} onChange={setRange} />
          <ModalidadSelect value={modalidad} onChange={setModalidad} />
          <EmpresaSelect value={clientTenantId} companies={companies} onChange={setClientTenantId} />
          <PrimaryButton onClick={() => void load()} disabled={busy}>
            {busy ? "Cargando…" : "Actualizar"}
          </PrimaryButton>
        </div>
      </div>

      {error && <ErrorNotice message={error} />}

      {reasons && <RejectionReasonsPanel reasons={reasons} />}
      {performance && <PerformancePanel performance={performance} />}
    </div>
  );
}

function RejectionReasonsPanel({ reasons }: { reasons: OtRejectionReasons }) {
  const conDatos = reasons.causales.filter((c) => c.rechazos > 0);

  return (
    <Section
      title="¿Por qué estoy rechazando?"
      testId="ot-reports-reasons"
      hint="Causales marcadas en los rechazos del periodo, ordenadas por peso."
    >
      {reasons.totalRechazos === 0 ? (
        <Empty>No hubo rechazos en el periodo seleccionado.</Empty>
      ) : (
        <>
          <div className="grid grid-cols-2 gap-3 lg:grid-cols-3">
            <Tile value={reasons.totalRechazos} label="Rechazos en el periodo" />
            <Tile
              value={reasons.promedioCausalesPorRechazo}
              label="Causales por rechazo (promedio)"
              hint="Si se acerca al tamaño del catálogo, alguien está marcando todo"
            />
            <Tile value={reasons.rechazosSinCausal} label="Rechazos sin causal marcada" />
          </div>

          <div className="flex flex-col gap-2">
            {conDatos.length === 0 ? (
              <Empty>
                Ningún rechazo del periodo tiene causal del catálogo. Los rechazos anteriores a esta
                funcionalidad solo tienen el motivo escrito a mano.
              </Empty>
            ) : (
              conDatos.map((c) => (
                <Bar
                  key={c.reasonId}
                  label={c.description}
                  value={c.rechazos}
                  total={reasons.totalRechazos}
                  suffix={`${c.pct} %`}
                />
              ))
            )}
          </div>

          {/* La aclaración solo tiene sentido si hay barras que leer. Sin causales, repetirla
              debajo del estado vacío es ruido. */}
          {conDatos.length > 0 && (
            <p className="text-[11px] text-[#6B7280] dark:text-white/50">
              Porcentaje de rechazos que incluyen cada causal. Como un rechazo puede tener varias, la
              suma puede pasar del 100 %.
            </p>
          )}
        </>
      )}
    </Section>
  );
}

function PerformancePanel({ performance }: { performance: OtPerformance }) {
  return (
    <>
      <Section
        title="Mi equipo de revisores"
        testId="ot-reports-reviewers"
        hint="Volumen siempre acompañado de calidad: el conteo solo premiaría a quien decide rápido y mal."
      >
        {performance.revisores.length === 0 ? (
          <Empty>Nadie decidió trámites en el periodo seleccionado.</Empty>
        ) : (
          <Table
            headers={[
              "Revisor",
              "Decididos",
              "% aprobado",
              "% rechazo",
              "Tiempo mediano",
              "Vuelven a rechazarse",
            ]}
            rows={performance.revisores.map((r) => ({
              key: r.userId,
              cells: [
                r.displayName,
                String(r.decididos),
                `${r.aprobacionPct} %`,
                `${r.rechazoPct} %`,
                r.tiempoMedianoHoras === null ? "—" : `${r.tiempoMedianoHoras} h`,
                `${r.vuelvenARechazarsePct} %`,
              ],
            }))}
          />
        )}
      </Section>

      <Section
        title="Calidad de lo que me llega, por empresa"
        testId="ot-reports-companies"
        hint="«Pasan a la primera» se mide sobre los aprobados: medirlo sobre todo lo entregado castigaría a la empresa por lo que aún está en revisión."
      >
        {performance.empresas.length === 0 ? (
          <Empty>Ninguna empresa entregó trámites en el periodo seleccionado.</Empty>
        ) : (
          <Table
            headers={["Empresa", "Entregados", "Aprobados", "Pasan a la primera", "Devoluciones promedio"]}
            rows={performance.empresas.map((e, i) => ({
              key: e.tenantId || `${e.name}-${i}`,
              cells: [
                e.name,
                String(e.entregados),
                String(e.aprobados),
                // Sin aprobados no hay base: «100 %» ahí se leería como una empresa impecable
                // cuando lo que pasa es que no hay nada que medir.
                e.aprobados === 0 ? "—" : `${e.pasanPrimeraPct} %`,
                String(e.devolucionesPromedio),
              ],
            }))}
          />
        )}
      </Section>
    </>
  );
}
