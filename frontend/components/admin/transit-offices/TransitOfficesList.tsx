"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Power, PowerOff, Landmark, Search, Send, SlidersHorizontal } from "lucide-react";
import type { UiStatus } from "@/components/admin/UiStateBoundary";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { useToast } from "@/components/admin/Toast";
import {
  fetchTransitOfficesOperationalStatus,
  hasQuipuxFlagsWithoutDivipo,
  isQuipuxElegible,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import { departmentName } from "@/lib/co-departments";
import { matchesOtOfficeSearch, otHubModulePath } from "@/components/admin/transit-offices/ot-nav";
import { TransitOfficeStatusDialog } from "@/components/admin/transit-offices/TransitOfficeStatusDialog";
import { TransitOfficeQuipuxDialog } from "@/components/admin/transit-offices/TransitOfficeQuipuxDialog";

/**
 * Listado de organismos de tránsito con estado operativo (RF01) + acciones de ciclo de
 * vida para SuperAdmin (RF02 activar tenant, RF03 desactivar). Cada fila muestra si el OT
 * tiene tenant activado y, si lo tiene, si está activo/inactivo. Sin activar → «Activar»
 * (vía `onCreateTenant`); con tenant → «Administrar» + Activar/Desactivar con confirmación.
 *
 * HU #10710 / #11223: columnas «CODIGO DIVIPOL» (código catálogo) y «CODIGO INTEGRADOR»
 * (código DIVIPO de radicación + banderas), con filtro «Sin código integrador».
 *
 * Presentación: patrón canónico card-list (`DataTable` + `StatusBadge`, HU #10844 /
 * flit-design-guardian — misma cáscara que CompanyListTable).
 */
export interface TransitOfficesListProps {
  /** Solicita la activación de tenant para una oficina sin tenant (la maneja la página). */
  onCreateTenant?: (office: TransitOfficeOperationalStatus) => void;
}

/** Eje «estado del tenant» del filtro. `todos` = sin filtrar. */
type EstadoFilter = "todos" | "sin-activar" | "activo" | "inactivo";
/** Eje «código integrador» del filtro. */
type QuipuxFilter = "todos" | "elegible" | "sin-divipo";

/** ¿Coincide la oficina con el eje de estado seleccionado? */
function matchesEstado(office: TransitOfficeOperationalStatus, filter: EstadoFilter): boolean {
  switch (filter) {
    case "sin-activar":
      return !office.hasTenant;
    case "activo":
      return office.hasTenant && office.estadoActivo === true;
    case "inactivo":
      return office.hasTenant && office.estadoActivo === false;
    default:
      return true;
  }
}

/** ¿Coincide la oficina con el eje de radicación Quipux seleccionado? */
function matchesQuipux(office: TransitOfficeOperationalStatus, filter: QuipuxFilter): boolean {
  switch (filter) {
    case "elegible":
      return isQuipuxElegible(office);
    case "sin-divipo":
      return !office.divipoCode;
    default:
      return true;
  }
}

// Selects de filtro — tokens FLIT (borde soft, foco azul marca).
const FILTER_SELECT_CLS =
  "rounded-[10px] border border-[var(--nav-borde,#DDE5F0)] bg-white px-3 py-2 text-sm text-[#162744] outline-none focus:border-[#4F74C9] focus:ring-2 focus:ring-[#4F74C9]/30 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";
const FILTER_OPTION_CLS = "bg-white text-[#162744] dark:bg-[#0B0F14] dark:text-white";
const FILTER_INPUT_CLS =
  "w-full rounded-[10px] border border-[var(--nav-borde,#DDE5F0)] bg-white py-2 pl-9 pr-3 text-sm text-[#162744] outline-none focus:border-[#4F74C9] focus:ring-2 focus:ring-[#4F74C9]/30 dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

export function TransitOfficesList({ onCreateTenant }: TransitOfficesListProps = {}) {
  const router = useRouter();
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOfficeOperationalStatus[]>([]);
  const [search, setSearch] = useState("");
  const [estadoFilter, setEstadoFilter] = useState<EstadoFilter>("todos");
  const [quipuxFilter, setQuipuxFilter] = useState<QuipuxFilter>("todos");
  const [departamento, setDepartamento] = useState("todos");
  const [statusTarget, setStatusTarget] = useState<TransitOfficeOperationalStatus | null>(null);
  const [quipuxTarget, setQuipuxTarget] = useState<TransitOfficeOperationalStatus | null>(null);

  const load = useCallback(async (signal?: AbortSignal) => {
    setStatus("loading");
    try {
      const data = await fetchTransitOfficesOperationalStatus(signal);
      if (signal?.aborted) {
        return;
      }
      setOffices(data);
      setStatus(data.length === 0 ? "empty" : "ready");
    } catch {
      if (!signal?.aborted) {
        setStatus("error");
      }
    }
  }, []);

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const filtered = useMemo(
    () =>
      offices.filter(
        (o) =>
          matchesOtOfficeSearch(o, search) &&
          matchesEstado(o, estadoFilter) &&
          matchesQuipux(o, quipuxFilter) &&
          (departamento === "todos" || o.departmentCode === departamento),
      ),
    [offices, search, estadoFilter, quipuxFilter, departamento],
  );

  const departamentos = useMemo(() => {
    const codes = Array.from(new Set(offices.map((o) => o.departmentCode))).filter(Boolean);
    return codes
      .map((code) => ({ code, name: departmentName(code) }))
      .sort((a, b) => a.name.localeCompare(b.name, "es"));
  }, [offices]);

  const hayFiltros =
    search.trim() !== "" ||
    estadoFilter !== "todos" ||
    quipuxFilter !== "todos" ||
    departamento !== "todos";

  const sinDivipoCount = useMemo(
    () => offices.filter((o) => !o.divipoCode).length,
    [offices],
  );

  const listStatus: UiStatus =
    status === "ready" && filtered.length === 0 && hayFiltros ? "empty" : status;

  const rowActions = useCallback(
    (office: TransitOfficeOperationalStatus): RowAction[] => {
      const quipuxAction: RowAction = {
        icon: Send,
        label: `Parametrizar radicación Quipux de ${office.name}`,
        onClick: () => setQuipuxTarget(office),
        tone: "default",
      };

      if (!office.hasTenant) {
        return [
          {
            icon: Landmark,
            label: `Activar ${office.name}`,
            onClick: () => onCreateTenant?.(office),
            tone: "primary",
          },
          quipuxAction,
        ];
      }

      const activo = office.estadoActivo === true;
      return [
        {
          icon: SlidersHorizontal,
          label: `Administrar ${office.name}`,
          onClick: () => router.push(otHubModulePath(office.id, "client-procedures")),
          tone: "primary",
        },
        quipuxAction,
        {
          icon: activo ? PowerOff : Power,
          label: `${activo ? "Desactivar" : "Activar"} ${office.name}`,
          onClick: () => setStatusTarget(office),
          tone: activo ? "danger" : "default",
        },
      ];
    },
    [onCreateTenant, router],
  );

  const columns: DataTableColumn<TransitOfficeOperationalStatus>[] = useMemo(
    () => [
      {
        key: "code",
        header: "CODIGO DIVIPOL",
        cellClassName: "font-mono",
        render: (office) => office.code,
      },
      {
        key: "name",
        header: "Organismo",
        cellClassName: "font-semibold",
        render: (office) => office.name,
      },
      {
        key: "department",
        header: "Departamento",
        cellClassName: "opacity-70",
        render: (office) => departmentName(office.departmentCode),
      },
      {
        key: "estado",
        header: "Estado",
        render: (office) => <EstadoBadge office={office} />,
      },
      {
        key: "quipux",
        header: "CODIGO INTEGRADOR",
        render: (office) => <QuipuxCell office={office} />,
      },
      {
        key: "actions",
        header: "Acción",
        align: "right",
        render: (office) => <RowActions actions={rowActions(office)} />,
      },
    ],
    [rowActions],
  );

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-col gap-3">
        <label className="relative block w-full max-w-md">
          <span className="sr-only">Buscar organismo de tránsito</span>
          <Search
            className="pointer-events-none absolute left-3 top-1/2 h-4 w-4 -translate-y-1/2 opacity-50"
            aria-hidden="true"
          />
          <input
            type="search"
            value={search}
            onChange={(e) => setSearch(e.target.value)}
            placeholder="Buscar por nombre o código…"
            className={FILTER_INPUT_CLS}
          />
        </label>

        {status === "ready" && (
          <div className="flex flex-wrap items-end gap-3">
            <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#59677D] dark:text-white/70">
              Departamento
              <select
                value={departamento}
                onChange={(e) => setDepartamento(e.target.value)}
                aria-label="Filtrar por departamento"
                className={FILTER_SELECT_CLS}
              >
                <option value="todos" className={FILTER_OPTION_CLS}>
                  Todos los departamentos
                </option>
                {departamentos.map((d) => (
                  <option key={d.code} value={d.code} className={FILTER_OPTION_CLS}>
                    {d.name}
                  </option>
                ))}
              </select>
            </label>

            <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#59677D] dark:text-white/70">
              Estado
              <select
                value={estadoFilter}
                onChange={(e) => setEstadoFilter(e.target.value as EstadoFilter)}
                aria-label="Filtrar por estado"
                className={FILTER_SELECT_CLS}
              >
                <option value="todos" className={FILTER_OPTION_CLS}>
                  Todos
                </option>
                <option value="sin-activar" className={FILTER_OPTION_CLS}>
                  Sin activar
                </option>
                <option value="activo" className={FILTER_OPTION_CLS}>
                  Activo
                </option>
                <option value="inactivo" className={FILTER_OPTION_CLS}>
                  Inactivo
                </option>
              </select>
            </label>

            <label className="flex flex-col gap-1 text-[11px] font-semibold text-[#59677D] dark:text-white/70">
              Código integrador
              <select
                value={quipuxFilter}
                onChange={(e) => setQuipuxFilter(e.target.value as QuipuxFilter)}
                aria-label="Filtrar por código integrador"
                className={FILTER_SELECT_CLS}
              >
                <option value="todos" className={FILTER_OPTION_CLS}>
                  Todas
                </option>
                <option value="elegible" className={FILTER_OPTION_CLS}>
                  Radica (elegible)
                </option>
                <option value="sin-divipo" className={FILTER_OPTION_CLS}>
                  Sin código integrador
                </option>
              </select>
            </label>
          </div>
        )}
      </div>

      {status === "ready" && (
        <p className="text-[11px] text-[#59677D] dark:text-white/60" data-testid="divipo-summary">
          {sinDivipoCount === 0
            ? `Las ${offices.length} secretarías del catálogo tienen código DIVIPO.`
            : `${sinDivipoCount} de ${offices.length} secretarías aún no tienen código DIVIPO cargado y no son elegibles para radicar por Quipux.`}
        </p>
      )}

      <DataTable
        columns={columns}
        rows={filtered}
        getRowKey={(office) => office.id}
        status={listStatus}
        onRetry={() => void load()}
        minWidth={768}
        ariaLabel="Organismos de tránsito"
        emptyMessage={
          hayFiltros
            ? "No hay organismos de tránsito que coincidan con los filtros."
            : "No hay organismos de tránsito en el catálogo."
        }
        errorMessage="No se pudo cargar el catálogo de organismos de tránsito."
      />

      {statusTarget && (
        <TransitOfficeStatusDialog
          office={statusTarget}
          onClose={() => setStatusTarget(null)}
          onConfirmed={(nextActivo) => {
            setOffices((current) =>
              current.map((o) =>
                o.id === statusTarget.id ? { ...o, estadoActivo: nextActivo } : o,
              ),
            );
            show(
              `Organismo de tránsito «${statusTarget.name}» ${
                nextActivo ? "activado" : "desactivado"
              }.`,
              "success",
            );
            setStatusTarget(null);
          }}
        />
      )}

      {quipuxTarget && (
        <TransitOfficeQuipuxDialog
          office={quipuxTarget}
          onClose={() => setQuipuxTarget(null)}
          onSaved={(settings) => {
            setOffices((current) =>
              current.map((o) =>
                o.id === quipuxTarget.id
                  ? {
                      ...o,
                      divipoCode: settings.divipoCode,
                      quipuxRegistration: settings.quipuxRegistration,
                      quipuxTransfer: settings.quipuxTransfer,
                      quipuxOther: settings.quipuxOther,
                    }
                  : o,
              ),
            );
            show(
              settings.divipoCode
                ? `Código integrador actualizado para «${quipuxTarget.name}».`
                : `Código integrador actualizado para «${quipuxTarget.name}». Sigue sin código: no se radicará.`,
              "success",
            );
            setQuipuxTarget(null);
          }}
        />
      )}
    </div>
  );
}

/**
 * Celda de código integrador (HU #10710 / #11223): DIVIPO de radicación + familias.
 * Sin código → tone neutral; inconsistencia (banderas sin código) → warning.
 */
function QuipuxCell({ office }: { office: TransitOfficeOperationalStatus }) {
  const familias = [
    office.quipuxRegistration && "Matrículas",
    office.quipuxTransfer && "Traspasos",
    office.quipuxOther && "Otros",
  ].filter((f): f is string => Boolean(f));

  const inconsistente = hasQuipuxFlagsWithoutDivipo(office);

  return (
    <div className="flex flex-col gap-1">
      {office.divipoCode ? (
        <span className="font-mono text-xs text-[#162744] dark:text-white">{office.divipoCode}</span>
      ) : (
        <StatusBadge
          label="Sin código integrador"
          tone="neutral"
          ariaLabel="Aún no se ha cargado el código integrador de esta secretaría"
        />
      )}

      {familias.length > 0 && (
        <span className="text-[11px] text-[#59677D] dark:text-white/70">{familias.join(" · ")}</span>
      )}

      {inconsistente && <StatusBadge label="Sin código integrador no se radica" tone="warning" />}
    </div>
  );
}

/** Badge de estado operativo: Sin activar | Activo | Inactivo (StatusBadge + tones FLIT). */
function EstadoBadge({ office }: { office: TransitOfficeOperationalStatus }) {
  if (!office.hasTenant) {
    return <StatusBadge label="Sin activar" tone="neutral" />;
  }
  if (office.estadoActivo === true) {
    return <StatusBadge label="Activo" tone="success" />;
  }
  return <StatusBadge label="Inactivo" tone="danger" />;
}
