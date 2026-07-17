"use client";

import { useCallback, useEffect, useMemo, useState } from "react";
import { useRouter } from "next/navigation";
import { Power, PowerOff, Plus, Search, Send, SlidersHorizontal } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { useToast } from "@/components/admin/Toast";
import {
  fetchTransitOfficesOperationalStatus,
  hasQuipuxFlagsWithoutDivipo,
  type TransitOfficeOperationalStatus,
} from "@/lib/api/admin-transit-office-tenants";
import { matchesOtOfficeSearch, otHubModulePath } from "@/components/admin/transit-offices/ot-nav";
import { TransitOfficeStatusDialog } from "@/components/admin/transit-offices/TransitOfficeStatusDialog";
import { TransitOfficeQuipuxDialog } from "@/components/admin/transit-offices/TransitOfficeQuipuxDialog";

/**
 * Listado de organismos de tránsito con estado operativo (RF01) + acciones de ciclo de
 * vida para SuperAdmin (RF02 activar, RF03 desactivar). Cada fila muestra si el OT tiene
 * tenant dado de alta y, si lo tiene, si está activo/inactivo. Sin alta → «Dar de alta»
 * (abre CreateTransitOfficeTenantDialog vía `onCreateTenant`); con tenant → «Administrar»
 * + Activar/Desactivar con confirmación.
 *
 * HU #10710 añade la columna «Radicación Quipux» (DIVIPO + banderas de la secretaría
 * DESTINO) con su acción de parametrización y el filtro «Sin DIVIPO», porque el caso
 * mayoritario es justamente el de las secretarías aún sin parametrizar (311 de 317) y el
 * administrador necesita localizarlas de un vistazo. Es un eje distinto del estado del
 * tenant: una secretaría sin alta en FLIT puede (y suele) ser destino de radicación.
 */
export interface TransitOfficesListProps {
  /** Solicita el alta de tenant para una oficina sin alta (la maneja la página). */
  onCreateTenant?: (office: TransitOfficeOperationalStatus) => void;
}

export function TransitOfficesList({ onCreateTenant }: TransitOfficesListProps = {}) {
  const router = useRouter();
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [offices, setOffices] = useState<TransitOfficeOperationalStatus[]>([]);
  const [search, setSearch] = useState("");
  const [soloSinDivipo, setSoloSinDivipo] = useState(false);
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
        (o) => matchesOtOfficeSearch(o, search) && (!soloSinDivipo || !o.divipoCode),
      ),
    [offices, search, soloSinDivipo],
  );

  // Cuántas secretarías faltan por parametrizar. Es el dato de cabecera del trabajo pendiente:
  // la carga del DIVIPO es manual, secretaría por secretaría.
  const sinDivipoCount = useMemo(
    () => offices.filter((o) => !o.divipoCode).length,
    [offices],
  );

  const listStatus: UiStatus =
    status === "ready" && filtered.length === 0 && (search.trim() !== "" || soloSinDivipo)
      ? "empty"
      : status;

  const rowActions = useCallback(
    (office: TransitOfficeOperationalStatus): RowAction[] => {
      // Parametrizar la radicación Quipux aplica a CUALQUIER secretaría del catálogo, tenga
      // o no tenant OT: el destino de radicación no depende de que sea cliente de FLIT.
      const quipuxAction: RowAction = {
        icon: Send,
        label: `Parametrizar radicación Quipux de ${office.name}`,
        onClick: () => setQuipuxTarget(office),
        tone: "default",
      };

      if (!office.hasTenant) {
        return [
          {
            icon: Plus,
            label: `Dar de alta ${office.name}`,
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
          onClick: () => router.push(otHubModulePath(office.id, "tramites")),
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

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
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
            className="w-full rounded-xl border py-2 pl-9 pr-3 text-sm"
          />
        </label>

        {/* Atajo al trabajo pendiente: la carga del DIVIPO es manual y hoy falta en la
            mayoría del catálogo. `aria-pressed` comunica el estado del filtro sin depender
            solo del color. */}
        {status === "ready" && sinDivipoCount > 0 && (
          <button
            type="button"
            onClick={() => setSoloSinDivipo((v) => !v)}
            aria-pressed={soloSinDivipo}
            className="inline-flex items-center gap-2 rounded-xl border px-3 py-2 text-xs font-semibold transition"
            style={
              soloSinDivipo
                ? { background: "#557EFF", color: "#FFFFFF", borderColor: "#557EFF" }
                : undefined
            }
          >
            Sin DIVIPO
            <span
              className="rounded-full px-1.5 py-0.5 text-[10px] font-bold"
              style={
                soloSinDivipo
                  ? { background: "rgba(255,255,255,0.25)", color: "#FFFFFF" }
                  : { background: "#E2E8F0", color: "#475569" }
              }
            >
              {sinDivipoCount}
            </span>
          </button>
        )}
      </div>

      {status === "ready" && (
        <p className="text-[11px] opacity-60" data-testid="divipo-summary">
          {sinDivipoCount === 0
            ? `Las ${offices.length} secretarías del catálogo tienen código DIVIPO.`
            : `${sinDivipoCount} de ${offices.length} secretarías aún no tienen código DIVIPO cargado y no son elegibles para radicar por Quipux.`}
        </p>
      )}

      <UiStateBoundary
        status={listStatus}
        onRetry={() => void load()}
        emptyMessage={
          soloSinDivipo
            ? "No hay secretarías sin código DIVIPO que coincidan con la búsqueda."
            : search.trim()
              ? "No hay organismos de tránsito que coincidan con la búsqueda."
              : "No hay organismos de tránsito en el catálogo."
        }
        errorMessage="No se pudo cargar el catálogo de organismos de tránsito."
      >
        <div className="overflow-x-auto rounded-xl border">
          <table className="w-full min-w-[48rem] text-left text-sm">
            <thead>
              <tr className="border-b text-xs font-semibold uppercase tracking-wide opacity-70">
                <th className="px-4 py-3" scope="col">
                  Código
                </th>
                <th className="px-4 py-3" scope="col">
                  Organismo
                </th>
                <th className="px-4 py-3" scope="col">
                  Departamento
                </th>
                <th className="px-4 py-3" scope="col">
                  Estado
                </th>
                <th className="px-4 py-3" scope="col">
                  Radicación Quipux
                </th>
                <th className="px-4 py-3 text-right" scope="col">
                  Acción
                </th>
              </tr>
            </thead>
            <tbody>
              {filtered.map((office) => (
                <tr
                  key={office.id}
                  className="border-b last:border-b-0 hover:bg-slate-50/80 dark:hover:bg-white/5"
                >
                  <td className="px-4 py-3 font-mono text-xs">{office.code}</td>
                  <td className="px-4 py-3 font-medium">{office.name}</td>
                  <td className="px-4 py-3 text-xs opacity-80">{office.departmentCode}</td>
                  <td className="px-4 py-3">
                    <EstadoBadge office={office} />
                  </td>
                  <td className="px-4 py-3">
                    <QuipuxCell office={office} />
                  </td>
                  <td className="px-4 py-3 text-right">
                    <RowActions actions={rowActions(office)} />
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

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
                ? `Radicación Quipux actualizada para «${quipuxTarget.name}».`
                : `Radicación Quipux actualizada para «${quipuxTarget.name}». Sigue sin código DIVIPO: no se radicará.`,
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
 * Celda de radicación Quipux (HU #10710): código DIVIPO + familias de trámite habilitadas.
 *
 * Sin DIVIPO se pinta en GRIS neutro, igual que «Sin alta» y nunca en naranja/rojo: no es un
 * fallo, es el estado normal de una secretaría todavía no integrada (el caso mayoritario).
 * El único estado que sí se advierte es el inconsistente —banderas activas sin DIVIPO—,
 * porque ahí el administrador puede creer que se está radicando cuando no es así.
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
        <span className="font-mono text-xs">{office.divipoCode}</span>
      ) : (
        <span
          className="inline-flex w-fit items-center rounded-full px-2.5 py-0.5 text-xs font-semibold"
          style={{ color: "#475569", backgroundColor: "#E2E8F0" }}
          title="Aún no se ha cargado el código DIVIPO de esta secretaría."
        >
          Sin DIVIPO
        </span>
      )}

      {familias.length > 0 && (
        <span className="text-[11px] opacity-70">{familias.join(" · ")}</span>
      )}

      {inconsistente && (
        <span className="text-[11px] font-medium" style={{ color: "#B45309" }}>
          Sin DIVIPO no se radica
        </span>
      )}
    </div>
  );
}

/** Badge de estado operativo: Sin alta | Activo | Inactivo (texto + color, WCAG AA). */
function EstadoBadge({ office }: { office: TransitOfficeOperationalStatus }) {
  const { label, color, bg } = !office.hasTenant
    ? { label: "Sin alta", color: "#475569", bg: "#E2E8F0" }
    : office.estadoActivo === true
      ? { label: "Activo", color: "#0F766E", bg: "#CCFBF1" }
      : { label: "Inactivo", color: "#B91C1C", bg: "#FEE2E2" };

  return (
    <span
      className="inline-flex items-center rounded-full px-2.5 py-0.5 text-xs font-semibold"
      style={{ color, backgroundColor: bg }}
    >
      {label}
    </span>
  );
}
