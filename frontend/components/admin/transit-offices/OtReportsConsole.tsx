"use client";

import { useCallback, useEffect, useState } from "react";
import {
  fetchOtClientCompanies,
  fetchOtReviewerOptions,
  type OtClientCompanyOption,
  type OtReviewerOption,
} from "@/lib/api/ot-metrics";
import { OtAnalysisTab } from "./_reportes/OtAnalysisTab";
import { OtNowTab } from "./_reportes/OtNowTab";
import { OtQueriesTab } from "./_reportes/OtQueriesTab";
import { OtReportBuilder } from "./_reportes/OtReportBuilder";
import { OtReviewersTab } from "./_reportes/OtReviewersTab";

// Reportes del organismo de tránsito.
//
// Hasta hace poco el módulo de reportes solo existía para la empresa gestora: el organismo usaba
// FLIT sin ningún instrumento para ver su propia operación. El eje está invertido respecto a los
// reportes de empresa — aquí un organismo mira hacia las empresas que le radican.
//
// La consola se parte en pestañas porque cada una responde una pregunta con un horizonte distinto,
// y meterlas en una sola pantalla obligaba a compartir filtros que no todas usan. El síntoma era un
// selector de rango de fechas presidiendo un bloque titulado «¿Cómo vamos hoy?», donde no cambiaba
// ni un número. Ahora cada pestaña trae los filtros que de verdad gobierna:
//
//   · Ahora mismo — estado de la cola y movimiento del día. Sin fechas.
//   · Análisis    — por qué rechazo y qué calidad me llega. Con rango.
//   · Informe     — qué recibí en un periodo y en qué acabó, trámite a trámite.
//   · Revisores   — qué hizo cada persona del organismo en un periodo.
//   · Consultas   — la pregunta que el usuario quiera hacerse, guardada y exportable.
//
// «Revisores» se llevó la tabla que antes vivía dentro de «Análisis». Mantener las dos habría dejado
// los mismos números en dos sitios con filtros distintos —Análisis no filtra por persona—, que es la
// forma más rápida de que un reporte deje de merecer confianza.

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
    hint: "Por qué rechazo y qué calidad me llega",
  },
  {
    id: "informe",
    label: "Informe",
    hint: "Qué recibí en un periodo y en qué acabó",
  },
  {
    id: "revisores",
    label: "Revisores",
    hint: "Qué hizo cada persona del organismo en un periodo",
  },
  {
    id: "consultas",
    label: "Consultas personalizadas",
    hint: "Arma tu propia búsqueda, guárdala y expórtala",
  },
] as const;

type TabId = (typeof TABS)[number]["id"];

/**
 * La pestaña activa vive en la dirección, no solo en memoria.
 *
 * Sin esto, la dirección miente: dice «reportes» mientras la pantalla enseña «Revisores», así que
 * recargar devolvía a «Ahora mismo» y un enlace copiado abría una pestaña distinta de la que lo
 * generó. Lo segundo se notaba sobre todo en «Consultas», donde el enlace lleva la consulta en
 * `?q=`: llegaba entera, pero la leía un componente que no llegaba a montarse.
 *
 * Se usa `replaceState` y no `pushState` a propósito. Con `pushState`, quien recorre las cinco
 * pestañas necesita cinco veces «atrás» para salir de reportes; con `replaceState`, «atrás» hace lo
 * que se espera —salir— y volver del detalle de un trámite sigue aterrizando en la pestaña correcta,
 * que es el caso que de verdad importa.
 *
 * Mismo mecanismo que la consola de reportes de la empresa (`atom/modules/Reportes.tsx`), que ya
 * guardaba su pestaña en `?reportesTab=`.
 */
const TAB_QUERY_PARAM = "tab";

function initialTab(): TabId {
  if (typeof window === "undefined") return "ahora";
  const requested = new URLSearchParams(window.location.search).get(TAB_QUERY_PARAM);
  // Una pestaña que ya no existe —un enlace viejo, un parámetro escrito a mano— no puede dejar la
  // consola en blanco: cae en la primera.
  return TABS.some((t) => t.id === requested) ? (requested as TabId) : "ahora";
}

export function OtReportsConsole({ transitOfficeId }: OtReportsConsoleProps) {
  const [tab, setTab] = useState<TabId>(initialTab);
  const [companies, setCompanies] = useState<OtClientCompanyOption[]>([]);
  const [reviewers, setReviewers] = useState<OtReviewerOption[]>([]);

  // Los dos catálogos se resuelven UNA vez por organismo y se reparten a las pestañas: ninguno
  // cambia con el rango ni con la modalidad, y recargarlos en cada pestaña solo sumaría llamadas.
  // Si alguno falla se deja vacío en vez de romper la consola: sin catálogo se pierde un filtro,
  // no el reporte.
  useEffect(() => {
    fetchOtClientCompanies(transitOfficeId)
      .then(setCompanies)
      .catch(() => setCompanies([]));
    fetchOtReviewerOptions(transitOfficeId)
      .then(setReviewers)
      .catch(() => setReviewers([]));
  }, [transitOfficeId]);

  const selectTab = useCallback((id: TabId) => {
    setTab(id);
    try {
      const url = new URL(window.location.href);
      url.searchParams.set(TAB_QUERY_PARAM, id);
      // Se conserva el resto de la dirección: «Consultas» guarda su propia consulta en `?q=` y
      // borrarla aquí perdería lo que el usuario acaba de armar.
      window.history.replaceState(window.history.state, "", url);
    } catch {
      /* entorno sin history: la pestaña cambia igual, solo no queda en la dirección */
    }
  }, []);

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
              onClick={() => selectTab(item.id)}
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
      {tab === "revisores" && (
        <OtReviewersTab
          transitOfficeId={transitOfficeId}
          companies={companies}
          reviewers={reviewers}
        />
      )}
      {tab === "consultas" && <OtQueriesTab transitOfficeId={transitOfficeId} />}
    </div>
  );
}
