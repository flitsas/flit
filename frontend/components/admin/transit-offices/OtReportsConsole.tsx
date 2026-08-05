"use client";

import { useEffect, useState } from "react";
import { fetchOtClientCompanies, type OtClientCompanyOption } from "@/lib/api/ot-metrics";
import { OtAnalysisTab } from "./_reportes/OtAnalysisTab";
import { OtNowTab } from "./_reportes/OtNowTab";
import { OtReportBuilder } from "./_reportes/OtReportBuilder";

// Reportes del organismo de tránsito.
//
// Hasta hace poco el módulo de reportes solo existía para la empresa gestora: el organismo usaba
// FLIT sin ningún instrumento para ver su propia operación. El eje está invertido respecto a los
// reportes de empresa — aquí un organismo mira hacia las empresas que le radican.
//
// La consola se parte en tres pestañas porque las tres responden preguntas con horizontes distintos,
// y meterlas en una sola pantalla obligaba a compartir filtros que no todas usan. El síntoma era un
// selector de rango de fechas presidiendo un bloque titulado «¿Cómo vamos hoy?», donde no cambiaba
// ni un número. Ahora cada pestaña trae los filtros que de verdad gobierna:
//
//   · Ahora mismo — estado de la cola y movimiento del día. Sin fechas.
//   · Análisis    — causales, revisores y calidad del periodo. Con rango.
//   · Informe     — la pregunta cerrada sobre un rango, con detalle exportable.

export interface OtReportsConsoleProps {
  transitOfficeId: string;
}

const TABS = [
  {
    id: "ahora",
    label: "Ahora mismo",
    hint: "Qué tengo en la cola en este momento",
  },
  {
    id: "analisis",
    label: "Análisis",
    hint: "Por qué rechazo y cómo decide mi equipo",
  },
  {
    id: "informe",
    label: "Informe",
    hint: "Qué recibí en un periodo y en qué acabó",
  },
] as const;

type TabId = (typeof TABS)[number]["id"];

export function OtReportsConsole({ transitOfficeId }: OtReportsConsoleProps) {
  const [tab, setTab] = useState<TabId>("ahora");
  const [companies, setCompanies] = useState<OtClientCompanyOption[]>([]);

  // El catálogo de empresas se resuelve UNA vez por organismo y se reparte a las tres pestañas: no
  // cambia con el rango ni con la modalidad, y recargarlo en cada pestaña solo sumaría llamadas.
  useEffect(() => {
    fetchOtClientCompanies(transitOfficeId)
      .then(setCompanies)
      .catch(() => setCompanies([]));
  }, [transitOfficeId]);

  return (
    <div className="flex flex-col gap-6" data-testid="ot-reports-console">
      <div
        role="tablist"
        aria-label="Reportes del organismo"
        className="flex flex-wrap gap-1 border-b border-[#DFE5ED] dark:border-white/10"
      >
        {TABS.map((item) => {
          const active = tab === item.id;
          return (
            <button
              key={item.id}
              type="button"
              role="tab"
              aria-selected={active}
              title={item.hint}
              onClick={() => setTab(item.id)}
              className={`-mb-px border-b-2 px-4 py-2 text-xs font-semibold transition ${
                active
                  ? "border-[#557EFF] text-[#557EFF]"
                  : "border-transparent text-[#6B7280] hover:text-[#162744] dark:text-white/50 dark:hover:text-white"
              }`}
            >
              {item.label}
            </button>
          );
        })}
      </div>

      {/* Cada pestaña se monta y desmonta: son consultas distintas y mantenerlas vivas en segundo
          plano dejaría al organismo mirando cifras que se cargaron hace media hora. */}
      {tab === "ahora" && (
        <OtNowTab transitOfficeId={transitOfficeId} companies={companies} />
      )}
      {tab === "analisis" && (
        <OtAnalysisTab transitOfficeId={transitOfficeId} companies={companies} />
      )}
      {tab === "informe" && (
        <OtReportBuilder transitOfficeId={transitOfficeId} companies={companies} />
      )}
    </div>
  );
}
