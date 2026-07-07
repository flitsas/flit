"use client";

// Pestaña "Uso del aplicativo" (Reportes 2.0, HU-C): embudo de pasos del wizard con
// % de abandono, tiempo por paso, módulos más usados, documentos más reemplazados,
// consumo/errores de APIs externas y heatmap de horas pico. Todo de GET /usage.
// La telemetría (HU-A) es nueva: el estado vacío lo explica.
import { UiStateBoundary } from "@/components/admin/UiStateBoundary";
import { fetchUsageMetrics, type UsageData } from "@/lib/api/analytics-v2";
import { BarList } from "../BarList";
import { CompanyNotice } from "../CompanyNotice";
import { toMetricsParams, type ReportFilters } from "../filters";
import { formatDurationMs, formatInt, formatPct } from "../format";
import { KpiCard } from "../KpiCard";
import { PeakHoursHeatmap } from "../PeakHoursHeatmap";
import { useAnalyticsQuery } from "../useAnalyticsQuery";

export interface UsoTabProps {
  filters: ReportFilters;
  needsCompany: boolean;
}

function isUsageEmpty(data: UsageData): boolean {
  return (
    data.moduleUsage.length === 0 &&
    data.wizardSteps.length === 0 &&
    data.peakHours.length === 0 &&
    data.documentReplacements.length === 0 &&
    data.externalApis.length === 0
  );
}

export function UsoTab({ filters, needsCompany }: UsoTabProps) {
  const params = toMetricsParams(filters);
  const usage = useAnalyticsQuery(
    (signal) => fetchUsageMetrics(params, signal),
    [JSON.stringify(params)],
    { skip: needsCompany, isEmpty: (res) => isUsageEmpty(res.current) },
  );

  if (needsCompany) {
    return <CompanyNotice />;
  }

  return (
    <UiStateBoundary
      status={usage.status}
      errorMessage={usage.errorMessage}
      onRetry={usage.retry}
      emptyMessage="Aún no hay datos de uso registrados. La telemetría es nueva y empezará a llenarse con la actividad del aplicativo."
      skeletonRows={4}
    >
      {usage.data && (
        <div className="flex flex-col gap-4">
          <div className="grid grid-cols-2 gap-3 max-w-xl">
            <KpiCard
              label="Duración media del wizard"
              value={formatDurationMs(usage.data.current.avgWizardDurationMs)}
              tooltip="Promedio del tiempo total desde que se abre el wizard hasta radicar el trámite."
              color="#557EFF"
            />
            <KpiCard
              label="Duración mediana del wizard"
              value={formatDurationMs(usage.data.current.medianWizardDurationMs)}
              tooltip="Mediana del tiempo total del wizard: la mitad de los trámites se radica en este tiempo o menos."
              color="#00DBD5"
            />
          </div>

          <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="wizard-steps-title">
            <h2
              id="wizard-steps-title"
              className="text-sm font-bold mb-3"
              title="Vistas y completados por paso; % de abandono = (1 − completados/vistas) × 100."
            >
              Embudo de pasos del wizard
            </h2>
            {usage.data.current.wizardSteps.length === 0 ? (
              <p className="text-xs opacity-60">Aún no hay telemetría del wizard.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-xs" data-testid="wizard-steps-table">
                  <thead>
                    <tr className="text-[10px] uppercase text-left opacity-70">
                      <th className="px-2 py-2 font-semibold">Paso</th>
                      <th className="px-2 py-2 font-semibold">Vistas</th>
                      <th className="px-2 py-2 font-semibold">Completados</th>
                      <th className="px-2 py-2 font-semibold">Abandono</th>
                      <th className="px-2 py-2 font-semibold">Tiempo promedio</th>
                      <th className="px-2 py-2 font-semibold">Tiempo mediano</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usage.data.current.wizardSteps.map((step) => (
                      <tr key={step.stepKey} className="border-t">
                        <td className="px-2 py-2 font-medium">{step.stepKey}</td>
                        <td className="px-2 py-2">{formatInt(step.views)}</td>
                        <td className="px-2 py-2">{formatInt(step.completions)}</td>
                        <td className="px-2 py-2">
                          <span className="inline-flex items-center gap-2">
                            <span
                              className="font-semibold"
                              style={{ color: step.abandonmentPct >= 30 ? "#FF4E00" : step.abandonmentPct >= 10 ? "#F9AC00" : "#8CC63F" }}
                            >
                              {formatPct(step.abandonmentPct)}
                            </span>
                            <span className="h-1.5 w-16 rounded-full bg-[#DFE5ED] dark:bg-[#1E2A3C] overflow-hidden">
                              <span
                                className="block h-full rounded-full"
                                style={{ width: `${Math.min(100, step.abandonmentPct)}%`, background: "#FF4E00" }}
                              />
                            </span>
                          </span>
                        </td>
                        <td className="px-2 py-2 opacity-80">{formatDurationMs(step.avgDurationMs)}</td>
                        <td className="px-2 py-2 opacity-80">{formatDurationMs(step.medianDurationMs)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <div className="grid grid-cols-1 lg:grid-cols-2 gap-4">
            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="modulos-title">
              <h2 id="modulos-title" className="text-sm font-bold mb-3" title="Eventos de apertura de módulo y accesos a la API, agrupados por módulo.">
                Módulos más usados
              </h2>
              <BarList
                items={usage.data.current.moduleUsage.map((m) => ({
                  key: m.module,
                  label: m.module,
                  value: m.events,
                  hint: `${formatInt(m.uniqueUsers)} usuarios distintos`,
                }))}
                color="#557EFF"
                emptyMessage="Aún no hay uso de módulos registrado."
                testId="modulos-mas-usados"
              />
            </section>

            <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="documentos-title">
              <h2
                id="documentos-title"
                className="text-sm font-bold mb-3"
                title="Reemplazos = cargas adicionales del mismo tipo de documento en una misma instancia."
              >
                Documentos más reemplazados
              </h2>
              <BarList
                items={usage.data.current.documentReplacements.map((d) => ({
                  key: d.documentTipo,
                  label: d.documentTipo,
                  value: d.replacements,
                  hint: `${formatInt(d.uploads)} cargas totales`,
                }))}
                color="#F9AC00"
                emptyMessage="Sin reemplazos de documentos en el periodo."
                testId="documentos-reemplazados"
              />
            </section>
          </div>

          <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="apis-title">
            <h2 id="apis-title" className="text-sm font-bold mb-3" title="Llamadas a integraciones externas; error = HTTP ≥ 400 o mensaje de error registrado.">
              APIs externas: consumo y errores
            </h2>
            {usage.data.current.externalApis.length === 0 ? (
              <p className="text-xs opacity-60">Sin llamadas a APIs externas en el periodo.</p>
            ) : (
              <div className="overflow-x-auto">
                <table className="w-full text-xs" data-testid="apis-externas-table">
                  <thead>
                    <tr className="text-[10px] uppercase text-left opacity-70">
                      <th className="px-2 py-2 font-semibold">Endpoint</th>
                      <th className="px-2 py-2 font-semibold">Dirección</th>
                      <th className="px-2 py-2 font-semibold">Llamadas</th>
                      <th className="px-2 py-2 font-semibold">Errores</th>
                      <th className="px-2 py-2 font-semibold">% error</th>
                      <th className="px-2 py-2 font-semibold">Promedio</th>
                      <th className="px-2 py-2 font-semibold">p90</th>
                    </tr>
                  </thead>
                  <tbody>
                    {usage.data.current.externalApis.map((api) => (
                      <tr key={`${api.endpoint}|${api.direction}`} className="border-t">
                        <td className="px-2 py-2 font-medium break-all">{api.endpoint}</td>
                        <td className="px-2 py-2 opacity-80">{api.direction === "outbound" ? "Saliente" : "Entrante"}</td>
                        <td className="px-2 py-2">{formatInt(api.calls)}</td>
                        <td className="px-2 py-2" style={{ color: api.errors > 0 ? "#FF4E00" : undefined }}>
                          {formatInt(api.errors)}
                        </td>
                        <td className="px-2 py-2">{formatPct(api.errorRatePct)}</td>
                        <td className="px-2 py-2 opacity-80">{formatDurationMs(api.avgDurationMs)}</td>
                        <td className="px-2 py-2 opacity-80">{formatDurationMs(api.p90DurationMs)}</td>
                      </tr>
                    ))}
                  </tbody>
                </table>
              </div>
            )}
          </section>

          <section className="rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border" aria-labelledby="heatmap-title">
            <h2 id="heatmap-title" className="text-sm font-bold mb-3" title="Eventos registrados por día de la semana y hora, en hora de Bogotá.">
              Horas pico de uso
            </h2>
            <PeakHoursHeatmap data={usage.data.current.peakHours} />
          </section>
        </div>
      )}
    </UiStateBoundary>
  );
}
