"use client";

// «Consultas» de la empresa gestora: la consola compartida, amarrada al catálogo y a la API de la
// empresa.
//
// Es la misma pantalla que usa el organismo de tránsito —fichas de filtro, enlace vivo, aviso de
// cobertura, export que recorre todas las páginas— sobre otro catálogo. La pregunta que resuelve es
// la de una gestora: «pegué las cuarenta placas de esta flota, ¿por qué salieron treinta y siete?».

import { useMemo } from "react";
import { QueryConsole } from "@/components/consultas/QueryConsole";
import type { QuerySource, SavedQuery } from "@/lib/api/queries";
import {
  COMPANY_DATE_FIELDS,
  deleteCompanySavedQuery,
  fetchCompanyQueryFields,
  fetchCompanySavedQueries,
  runCompanyQuery,
  saveCompanyQuery,
  type CompanyQueryRow,
} from "@/lib/api/company-queries";
import {
  deleteSuperAdminSavedQuery,
  fetchSuperAdminQueryFields,
  fetchSuperAdminSavedQueries,
  runSuperAdminQuery,
  saveSuperAdminQuery,
} from "@/lib/api/superadmin-queries";
import {
  COMPANY_QUERY_COLUMNS,
  COMPANY_QUERY_PRESETS,
  defaultCompanyQueryColumns,
  estadoEmpresa,
} from "../consultas/company-columns";

export interface ConsultasTabProps {
  /** Solo SuperAdmin: la compañía que se está mirando. El resto va con la suya. */
  tenantId?: string;
  /** SuperAdmin sin compañía elegida: en el resto de pestañas no hay nada que mostrar. */
  needsCompany?: boolean;
  /**
   * SuperAdmin sin compañía elegida es justo el caso de esta pestaña: en vez del aviso que
   * muestran las demás, corre sobre TODAS las compañías a la vez (motor de operaciones).
   */
  isSuper?: boolean;
  /**
   * Reportes 2.0 (HU-D, segunda ola) — "Programar este informe" en una consulta guardada. Sin
   * esto el botón no aparece (mismo criterio que Consultas del organismo, que no lo pasa).
   */
  onScheduleQuery?: (query: SavedQuery, scope: "empresa" | "superadmin") => void;
}

export function ConsultasTab({
  tenantId,
  needsCompany = false,
  isSuper = false,
  onScheduleQuery,
}: ConsultasTabProps) {
  // SuperAdmin sin compañía elegida en el selector global: el motor de operaciones, sobre todas las
  // compañías. Elegir una compañía ahí sigue funcionando igual que para una empresa normal.
  const superAdmin = isSuper && !tenantId;

  // El origen se memoriza por compañía: la consola lo usa como dependencia de sus efectos, y una
  // identidad nueva en cada render relanzaría el catálogo y las guardadas sin parar.
  const companySource = useMemo<QuerySource<CompanyQueryRow>>(
    () => ({
      testIdPrefix: "empresa-query",
      dateFields: COMPANY_DATE_FIELDS,
      defaultDateField: "creacion",
      columnsStorageKey: "flit-empresa-consultas-columnas",
      // Distinto del «consulta» del organismo a propósito: los dos módulos exportan el mismo
      // rango de fechas, así que con el mismo prefijo los dos archivos se llaman igual y en la
      // carpeta de descargas quedan como «consulta… (1)», sin forma de saber cuál es cuál.
      exportPrefix: "consulta-empresa",
      rowNoun: ["trámite", "trámites"],
      fetchFields: (signal) => fetchCompanyQueryFields(tenantId, signal),
      run: (definition, options) => runCompanyQuery(definition, { ...options, tenantId }),
      fetchSaved: (signal) => fetchCompanySavedQueries(tenantId, signal),
      save: (input) => saveCompanyQuery(input, tenantId),
      remove: (id) => deleteCompanySavedQuery(id, tenantId),
      onSchedule: onScheduleQuery ? (query) => onScheduleQuery(query, "empresa") : undefined,
    }),
    [tenantId, onScheduleQuery],
  );

  const superAdminSource = useMemo<QuerySource<CompanyQueryRow>>(
    () => ({
      testIdPrefix: "superadmin-query",
      dateFields: COMPANY_DATE_FIELDS,
      defaultDateField: "creacion",
      // Aparte de la de una compañía sola: las columnas elegidas (con «Compañía» de más) no
      // deberían aplicarse sin querer la próxima vez que se mire una sola.
      columnsStorageKey: "flit-superadmin-consultas-columnas",
      exportPrefix: "consulta-todas-companias",
      rowNoun: ["trámite", "trámites"],
      fetchFields: fetchSuperAdminQueryFields,
      run: runSuperAdminQuery,
      fetchSaved: fetchSuperAdminSavedQueries,
      save: saveSuperAdminQuery,
      remove: deleteSuperAdminSavedQuery,
      onSchedule: onScheduleQuery ? (query) => onScheduleQuery(query, "superadmin") : undefined,
    }),
    [onScheduleQuery],
  );

  // Sin compañía elegida no se consulta: el backend responde 400 y un error rojo se lee como una
  // avería. Se dice qué falta, que es lo único accionable.
  if (needsCompany && !superAdmin) {
    return (
      <div
        className="rounded-2xl border border-[#DFE5ED] p-8 text-center dark:border-white/10"
        data-testid="empresa-query-sin-compania"
      >
        <p className="text-sm font-medium">Selecciona una compañía para consultar sus trámites.</p>
        <p className="mt-1 text-xs opacity-70">
          Las consultas guardadas y el catálogo de filtros son propios de cada compañía.
        </p>
      </div>
    );
  }

  if (superAdmin) {
    return (
      <QueryConsole
        source={superAdminSource}
        columns={COMPANY_QUERY_COLUMNS}
        presets={COMPANY_QUERY_PRESETS}
        defaultColumns={["compania", ...defaultCompanyQueryColumns()]}
        rowKey={(fila) => fila.procedureInstanceId}
        sheetName="Consulta"
        ambito="todas las compañías"
        renderCell={(columnId, fila) =>
          columnId === "estado" ? (
            <span
              className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
              style={{
                background: `${estadoEmpresa(fila.status).color}1A`,
                color: estadoEmpresa(fila.status).color,
              }}
            >
              {estadoEmpresa(fila.status).label}
            </span>
          ) : null
        }
      />
    );
  }

  return (
    <QueryConsole
      source={companySource}
      columns={COMPANY_QUERY_COLUMNS}
      presets={COMPANY_QUERY_PRESETS}
      defaultColumns={defaultCompanyQueryColumns()}
      rowKey={(fila) => fila.procedureInstanceId}
      sheetName="Consulta"
      ambito="tu empresa"
      renderCell={(columnId, fila) =>
        columnId === "estado" ? (
          <span
            className="rounded-full px-2 py-0.5 text-[11px] font-semibold"
            style={{
              background: `${estadoEmpresa(fila.status).color}1A`,
              color: estadoEmpresa(fila.status).color,
            }}
          >
            {estadoEmpresa(fila.status).label}
          </span>
        ) : null
      }
    />
  );
}
