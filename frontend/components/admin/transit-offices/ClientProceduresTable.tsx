"use client";

import { useEffect, useRef, useState } from "react";
import {
  ArrowDown,
  ArrowUp,
  ArrowUpDown,
  Check,
  Eye,
  FileStack,
  FolderOpen,
  Paperclip,
  Pencil,
  Star,
  Tag,
  Undo2,
  X,
} from "lucide-react";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  TABLA_CELDA_SECUNDARIA_CLS,
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { OtTablePagination } from "./OtTablePagination";
import { ActionsMenu, type ActionsMenuItem } from "@/components/atom/ActionsMenu";
import type { OtClientProcedure } from "@/lib/api/types-ot";
import { formatOtDate, formatOtProcedureStatus, plateUpdateWindow, procedureStatusTone } from "./ot-utils";
import {
  esperandoProcesoDelGestor,
  plateFlowChipStyle,
  plateFlowLabel,
  puedeDecidirOt,
} from "@/lib/tramites/estados";
import {
  OT_PROCEDURES_COLUMNS,
  otProceduresSortOptions,
  type OtProceduresSortOption,
  otColumnToSortBy,
} from "@/lib/admin/ot-procedures-columns";

export interface ClientProceduresTableProps {
  rows: OtClientProcedure[];
  totalCount: number;
  page: number;
  pageSize: number;
  onPageChange: (page: number) => void;
  onApprove: (row: OtClientProcedure) => void;
  onReject: (row: OtClientProcedure) => void;
  showApprovalActions?: boolean;
  /**
   * Botón único "Ver consolidado" (Feature #10701): muestra el consolidado maestro vigente y, si no
   * lo está (nunca generado o invalidado por un cambio de estado / LT), lo genera y lo muestra. El
   * backend decide regenerar-o-reutilizar por la marca `consolidado_maestro_vigente`.
   */
  onConsolidado?: (row: OtClientProcedure) => void;
  /** Adjunta la Licencia de Transito a un tramite ya aprobado (solo OT admin). */
  onAdjuntarLt?: (row: OtClientProcedure) => void;
  /** Feature #10587 — asignar placa a un trámite en preasignado (Flujo B). */
  onAssignPlate?: (row: OtClientProcedure) => void;
  /** Feature #10587 — revocar la preasignación de un trámite. */
  onRevoke?: (row: OtClientProcedure) => void;
  /**
   * HU #12166 (Feature #12156) — el OT deshace su propia aprobación (aprobado→revocado). Distinto
   * de `onRevoke` (revoca una PREASIGNACIÓN antes de aprobar, HU #10655): mismo rótulo "Revocar" en
   * el menú porque nunca coinciden en la misma fila (aprobar limpia plateFlowStatus).
   */
  onRevokeAprobacion?: (row: OtClientProcedure) => void;
  /** HU #12167 (Feature #12156) — corregir la placa dentro de la ventana de 1 hora (una única vez). */
  onUpdatePlate?: (row: OtClientProcedure) => void;
  /** Id de la fila con accion de consolidado en curso (deshabilita sus botones). */
  consolidadoActingId?: string | null;
  /** Abre el panel de documentos del expediente para el trámite. */
  onVerDocumentos?: (row: OtClientProcedure) => void;
  /**
   * Abre el detalle del trámite. Lo disparan DOS cosas: la acción del menú y la fila entera —el
   * detalle es a lo que se entra el 90% de las veces, y obligar a pasar por un menú de ocho
   * opciones para llegar a él era peaje puro (prototipo del Feature #12059).
   */
  onVerDetalle?: (row: OtClientProcedure) => void;
  /**
   * Columnas que el usuario tiene encendidas (HU #12218 AC7). Sin la prop se pintan todas: la
   * tabla nunca se queda sin columnas porque una preferencia no cargara.
   */
  visibleColumns?: readonly string[];
  /** sortBy actual del API (vin, placa, vendedor, …). */
  sortBy?: string;
  sortDir?: "asc" | "desc";
  onSortChange?: (sortBy: string, sortDir: "asc" | "desc") => void;
}

/**
 * Desplegable de orden para una columna que muestra varios datos (HU #12219).
 *
 * <p>Cierra al pulsar fuera y con Escape, devolviendo el foco al disparador: sin eso, quien navega
 * con teclado se queda dentro de un menú que ya no está.</p>
 */
function SortMenu({
  label,
  opciones,
  sortBy,
  sortDir,
  onSortChange,
}: {
  label: string;
  opciones: OtProceduresSortOption[];
  sortBy?: string;
  sortDir?: "asc" | "desc";
  onSortChange: (sortBy: string, sortDir: "asc" | "desc") => void;
}) {
  const [open, setOpen] = useState(false);
  const triggerRef = useRef<HTMLButtonElement>(null);
  const panelRef = useRef<HTMLDivElement>(null);

  useEffect(() => {
    if (!open) return undefined;
    const onDocDown = (e: MouseEvent) => {
      const target = e.target as Node;
      if (!panelRef.current?.contains(target) && !triggerRef.current?.contains(target)) {
        setOpen(false);
      }
    };
    const onKey = (e: KeyboardEvent) => {
      if (e.key === "Escape") {
        setOpen(false);
        triggerRef.current?.focus();
      }
    };
    document.addEventListener("mousedown", onDocDown);
    document.addEventListener("keydown", onKey);
    return () => {
      document.removeEventListener("mousedown", onDocDown);
      document.removeEventListener("keydown", onKey);
    };
  }, [open]);

  const activa = opciones.find((o) => o.sort === sortBy);
  const Icon = !activa ? ArrowUpDown : sortDir === "asc" ? ArrowUp : ArrowDown;

  return (
    <div className="relative inline-block">
      <button
        ref={triggerRef}
        type="button"
        onClick={() => setOpen((v) => !v)}
        aria-haspopup="menu"
        aria-expanded={open}
        // El nombre accesible dice por qué está ordenando AHORA, no solo que se puede ordenar: es
        // la única forma de saberlo sin ver el icono.
        aria-label={
          activa
            ? `Ordenar ${label}. Ahora: ${activa.label} ${sortDir === "asc" ? "ascendente" : "descendente"}`
            : `Ordenar ${label}`
        }
        className={`inline-flex items-center gap-1.5 rounded-md px-1.5 py-1 uppercase transition-colors hover:opacity-80 ${
          activa ? "underline underline-offset-4" : ""
        }`}
      >
        {label}
        <Icon className={`h-3 w-3 ${activa ? "opacity-100" : "opacity-45"}`} aria-hidden="true" />
      </button>

      {open ? (
        <div
          ref={panelRef}
          role="menu"
          aria-label={`Ordenar por, en ${label}`}
          className="absolute left-0 top-full z-50 mt-1.5 w-[16rem] overflow-hidden rounded-xl border border-[#DFE5ED] bg-white normal-case text-[#162744] shadow-xl dark:border-white/10 dark:bg-[#162744] dark:text-white"
        >
          <p className="border-b border-[#EEF2F7] px-3 py-2 text-[11px] font-semibold uppercase tracking-wide text-[#7B8794] dark:border-white/10 dark:text-white/50">
            Ordenar por
          </p>
          <div className="p-1.5">
            {opciones.map((opcion) => (
              <div
                key={opcion.id}
                className="rounded-lg px-2 py-1.5 [&+&]:mt-0.5 [&+&]:border-t [&+&]:border-[#F1F5F9] [&+&]:pt-2 dark:[&+&]:border-white/5"
              >
                <p className="mb-1.5 text-xs font-medium">{opcion.label}</p>
                <span className="flex items-center gap-1.5">
                  {(["asc", "desc"] as const).map((dir) => {
                    const seleccionada = sortBy === opcion.sort && sortDir === dir;
                    const Flecha = dir === "asc" ? ArrowUp : ArrowDown;
                    return (
                      <button
                        key={dir}
                        type="button"
                        role="menuitemradio"
                        aria-checked={seleccionada}
                        aria-label={`${opcion.label}: ${dir === "asc" ? "A-Z" : "Z-A"}`}
                        onClick={() => {
                          onSortChange(opcion.sort, dir);
                          setOpen(false);
                        }}
                        className={`inline-flex flex-1 items-center justify-center gap-1 rounded-md border px-2 py-1 text-[11px] font-medium transition-colors ${
                          seleccionada
                            ? "border-[#2C6BED] bg-[#2C6BED] text-white"
                            : "border-[#DFE5ED] text-[#5A6B7F] hover:bg-[#EEF5FF] dark:border-white/15 dark:text-white/70 dark:hover:bg-white/10"
                        }`}
                      >
                        <Flecha className="h-3 w-3" aria-hidden="true" />
                        {dir === "asc" ? "A-Z" : "Z-A"}
                      </button>
                    );
                  })}
                </span>
              </div>
            ))}
          </div>
        </div>
      ) : null}
    </div>
  );
}

function SortableTh({
  label,
  sortLabel,
  columnKey,
  sortBy,
  sortDir,
  onSortChange,
  className = "",
}: {
  label: string;
  /** Por qué ordena, si no es lo que dice el rótulo (ver `sortLabel` en el catálogo). */
  sortLabel?: string;
  columnKey: string;
  sortBy?: string;
  sortDir?: "asc" | "desc";
  onSortChange?: (sortBy: string, sortDir: "asc" | "desc") => void;
  className?: string;
}) {
  const apiKey = otColumnToSortBy(columnKey);
  const active = sortBy === apiKey || (columnKey === "fechaRadicacion" && sortBy === "createdAt");
  const nextDir: "asc" | "desc" = active && sortDir === "asc" ? "desc" : "asc";
  const Icon = !active ? ArrowUpDown : sortDir === "asc" ? ArrowUp : ArrowDown;

  const cls = `${TABLA_HEADER_CELL_CLS} ${className}`.trim();
  const style = { background: TABLA_HEADER_BG, color: TABLA_HEADER_FG };

  if (!onSortChange) {
    return (
      <th className={cls} style={style}>
        {label}
      </th>
    );
  }

  // HU #12219 — una columna que apila varios datos no puede ordenarse con un clic: no habría forma
  // de decir por cuál. Con más de una opción, la cabecera abre un desplegable.
  const opciones = otProceduresSortOptions(columnKey);
  if (opciones.length > 1) {
    return (
      <th className={cls} style={style}>
        <SortMenu
          label={label}
          opciones={opciones}
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={onSortChange}
        />
      </th>
    );
  }

  return (
    <th className={cls} style={style}>
      <button
        type="button"
        className="inline-flex items-center gap-1 uppercase hover:opacity-80"
        aria-label={`Ordenar por ${sortLabel ?? label}${active ? ` (${sortDir === "asc" ? "ascendente" : "descendente"})` : ""}`}
        onClick={() => onSortChange(apiKey, nextDir)}
      >
        {label}
        <Icon className="h-3 w-3 opacity-60" aria-hidden="true" />
      </button>
    </th>
  );
}

/**
 * Clases propias de cada celda, más allá de las comunes. Viven aquí y no dentro del render para que
 * añadir una columna sea una entrada en dos mapas y no un `if` en medio del JSX.
 */
const CELDA_CLS: Record<string, string> = {
  radicado: "font-semibold",
  vin: "font-mono text-[11px]",
  placa: "font-semibold",
  fechaRadicacion: "opacity-70",
};

/** Qué pinta cada columna. Un solo sitio donde mirar cuando una celda muestra lo que no debe. */
function renderCelda(columnKey: string, row: OtClientProcedure) {
  switch (columnKey) {
    case "radicado":
      return (
        <span className="flex items-center gap-1.5">
          {row.prioritario && (
            <Star
              className="h-3.5 w-3.5 shrink-0"
              style={{ color: "#F59E0B", fill: "#F59E0B" }}
              aria-label="Trámite prioritario"
            />
          )}
          {row.referenceNumber}
        </span>
      );
    case "vin":
      return row.vin?.trim() || "—";
    case "placa":
      return row.placa?.trim() || "—";
    case "vendedor":
      return row.vendedorNombre?.trim() || "—";
    case "comprador":
      return row.compradorNombre?.trim() || "—";
    case "tipoTramite":
      return row.procedureTypeName ?? row.procedureTypeId;
    // Empresa arriba, gestor debajo y atenuado: la empresa es la responsable del trámite; el
    // gestor, la persona concreta con la que hablar.
    case "empresaGestor":
      return (
        <span className="block min-w-0">
          <span className="block truncate font-semibold">
            {row.clientTenantName ?? row.clientTenantId}
          </span>
          <span className={`block truncate ${TABLA_CELDA_SECUNDARIA_CLS}`}>
            {row.gestorNombre?.trim() || "—"}
          </span>
        </span>
      );
    case "estado":
      return (
        <div className="flex flex-wrap items-center gap-1.5">
          <StatusBadge
            label={formatOtProcedureStatus(row.status)}
            tone={procedureStatusTone(row.status)}
          />
          {plateFlowChipStyle(row.plateFlowStatus) && (
            <span
              title="Progreso de la placa (sub-estado interno; el trámite sigue en Entregado)"
              className="rounded-full px-2 py-0.5 text-[10px] font-semibold"
              style={{
                background: plateFlowChipStyle(row.plateFlowStatus)!.bg,
                color: plateFlowChipStyle(row.plateFlowStatus)!.color,
                border: `1px solid ${plateFlowChipStyle(row.plateFlowStatus)!.border}`,
              }}
            >
              {plateFlowLabel(row.plateFlowStatus)}
            </span>
          )}
        </div>
      );
    case "fechaRadicacion":
      return formatOtDate(row.createdAt);
    default:
      return null;
  }
}

/** Tabla paginada tramites clientes OT — patron CompanyListTable (HU #10220). */
export function ClientProceduresTable({
  rows,
  totalCount,
  page,
  pageSize,
  onPageChange,
  onApprove,
  onReject,
  showApprovalActions = true,
  onConsolidado,
  onAdjuntarLt,
  onAssignPlate,
  onRevoke,
  onRevokeAprobacion,
  onUpdatePlate,
  consolidadoActingId = null,
  onVerDocumentos,
  onVerDetalle,
  visibleColumns,
  sortBy,
  sortDir,
  onSortChange,
}: ClientProceduresTableProps) {
  // Sin preferencia (o con una que dejara la tabla vacía) se pintan todas: una tabla sin columnas
  // no es una tabla, y eso NO puede depender de que una llamada de preferencias haya respondido.
  const columnasVisibles =
    visibleColumns && visibleColumns.length > 0
      ? OT_PROCEDURES_COLUMNS.filter((c) => visibleColumns.includes(c.key))
      : OT_PROCEDURES_COLUMNS;

  /**
   * Acciones disponibles para una fila, en el orden en que el operador las necesita: primero
   * decidir, luego la placa, luego consultar. Cada una aparece bajo la MISMA condición con la que
   * se pintaba su botón — el menú cambia dónde viven, no cuándo existen.
   */
  const buildRowActions = (row: OtClientProcedure): ActionsMenuItem[] => {
    const items: ActionsMenuItem[] = [];
    const decidible =
      row.status === "entregado" &&
      puedeDecidirOt(row.plateFlowStatus, row.soatEstado) &&
      showApprovalActions;

    if (decidible) {
      items.push({
        key: "aprobar",
        label: "Aprobar",
        icon: Check,
        onSelect: () => onApprove(row),
      });
      items.push({
        key: "rechazar",
        label: "Rechazar",
        icon: X,
        onSelect: () => onReject(row),
      });
    }

    if (row.plateFlowStatus === "preasignado" && showApprovalActions && onAssignPlate) {
      items.push({
        key: "asignar-placa",
        label: "Asignar placa",
        icon: Tag,
        onSelect: () => onAssignPlate(row),
      });
    }

    if (
      (row.plateFlowStatus === "preasignado" || row.plateFlowStatus === "asignado") &&
      showApprovalActions &&
      onRevoke
    ) {
      items.push({ key: "revocar", label: "Revocar", icon: Undo2, onSelect: () => onRevoke(row) });
    }

    // HU #12168 AC1 — "Revocar" (la aprobación) solo existe en Aprobado: es la única transición que
    // la máquina de estados permite desde ahí (aprobado→revocado), y solo el OT puede dispararla.
    if (row.status === "aprobado" && onRevokeAprobacion) {
      items.push({ key: "revocar-aprobacion", label: "Revocar", icon: Undo2, onSelect: () => onRevokeAprobacion(row) });
    }

    // HU #12168 AC2/AC3 — "Actualizar placa" existe mientras haya una placa asignada por este flujo
    // (plateAssignedAt), y se autodeshabilita con motivo cuando la ventana cerró o ya se usó la
    // única corrección — igual que "Ver consolidado" arriba, nunca se OMITE la opción: verla
    // deshabilitada con el motivo es lo que le dice al OT por qué ya no puede corregirla.
    if (row.plateAssignedAt && onUpdatePlate) {
      const ventana = plateUpdateWindow(row.plateAssignedAt, row.plateUpdatedAt);
      items.push({
        key: "actualizar-placa",
        label: ventana.disabled ? "Actualizar placa" : `Actualizar placa (${ventana.minutosRestantes} min)`,
        icon: Pencil,
        disabled: ventana.disabled,
        disabledReason: ventana.disabledReason,
        onSelect: () => onUpdatePlate(row),
      });
    }

    if (row.status === "aprobado" && onAdjuntarLt) {
      items.push({ key: "adjuntar-lt", label: "Adjuntar LT", icon: Paperclip, onSelect: () => onAdjuntarLt(row) });
    }

    if ((row.status === "entregado" || row.status === "aprobado") && onConsolidado) {
      items.push({
        key: "consolidado",
        label: consolidadoActingId === row.id ? "Abriendo…" : "Ver consolidado",
        icon: FileStack,
        // Se deshabilita mientras se abre: el consolidado puede tener que generarse, y un segundo
        // clic dispararía una segunda generación del mismo expediente.
        disabled: consolidadoActingId === row.id,
        disabledReason: "Abriendo el consolidado…",
        onSelect: () => onConsolidado(row),
      });
    }

    if (onVerDocumentos) {
      items.push({
        key: "documentos",
        label: "Ver documentos",
        icon: FolderOpen,
        onSelect: () => onVerDocumentos(row),
      });
    }

    if (onVerDetalle) {
      items.push({
        key: "detalle",
        label: "Detalle del trámite",
        icon: Eye,
        onSelect: () => onVerDetalle(row),
      });
    }

    return items;
  };

  return (
    <div className="flex flex-1 flex-col">
      <div className="overflow-x-auto">
      <table className="w-full min-w-[1100px] border-separate border-spacing-y-2 text-xs">
        <thead>
          <tr>
            {columnasVisibles.map((columna, indice) => (
              <SortableTh
                key={columna.key}
                label={columna.label}
                sortLabel={columna.sortLabel}
                columnKey={columna.key}
                sortBy={sortBy}
                sortDir={sortDir}
                onSortChange={columna.sortable ? onSortChange : undefined}
                className={indice === 0 ? "rounded-l-xl" : ""}
              />
            ))}
            <th
              className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
              style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
            >
              Acciones
            </th>
          </tr>
        </thead>
        <tbody>
          {rows.map((row) => (
            <tr
              key={row.id}
              className={`bg-card ${TABLA_ROW_HOVER_CLS} ${onVerDetalle ? "cursor-pointer" : ""}`}
              onClick={onVerDetalle ? () => onVerDetalle(row) : undefined}
              // La fila no es un `<button>`: sigue siendo una fila de tabla, así que el teclado
              // necesita su propia puerta. Enter y Espacio hacen lo mismo que el clic, y el menú
              // de acciones sigue siendo el camino accesible de siempre para el resto.
              tabIndex={onVerDetalle ? 0 : undefined}
              role={onVerDetalle ? "button" : undefined}
              aria-label={onVerDetalle ? `Ver el detalle del trámite ${row.referenceNumber}` : undefined}
              onKeyDown={
                onVerDetalle
                  ? (e) => {
                      if (e.key === "Enter" || e.key === " ") {
                        e.preventDefault();
                        onVerDetalle(row);
                      }
                    }
                  : undefined
              }
            >
              {columnasVisibles.map((columna, indice) => (
                <td
                  key={columna.key}
                  className={`${indice === 0 ? "rounded-l-xl border-l " : ""}border-y px-4 py-3 ${CELDA_CLS[columna.key] ?? ""}`}
                >
                  {renderCelda(columna.key, row)}
                </td>
              ))}
              {/*
                Las ACCIONES van en un menú (`ActionsMenu`, el mismo del listado del gestor). Sueltas
                eran hasta ocho botones condicionales en una celda: la columna crecía o encogía según
                el estado de cada fila y la tabla nunca tenía el mismo ancho dos filas seguidas.

                Lo que NO entra en el menú es lo INFORMATIVO —"Esperando proceso del gestor" y los
                sellos de SOAT/Impuesto—: no son cosas que el operador pueda ejecutar, y escondidas
                tras un clic dejarían de avisar, que es justo para lo que están.
              */}
              {/* Las acciones cortan la propagación: pulsar «Aprobar» abriría además el detalle. */}
              <td
                className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                onClick={(e) => e.stopPropagation()}
                onKeyDown={(e) => e.stopPropagation()}
              >
                <div className="flex items-center justify-end gap-2">
                  {row.status === "entregado" &&
                    esperandoProcesoDelGestor(row.plateFlowStatus) &&
                    showApprovalActions && (
                      <span
                        className="text-[10px] font-medium italic"
                        style={{ color: "#b45309" }}
                        title="El gestor debe procesar el trámite (Asignado → Terminado) antes de que el OT apruebe o rechace."
                      >
                        Esperando proceso del gestor
                      </span>
                    )}
                  {row.plateFlowStatus === "terminado" && (
                    <span className="flex flex-wrap justify-end gap-1">
                      {row.soatPagado && (
                        <span className="rounded-full bg-emerald-50 px-2 py-0.5 text-[10px] font-semibold text-emerald-700">
                          SOAT
                        </span>
                      )}
                      {row.impuestoDepartamentalPagado && (
                        <span className="rounded-full bg-sky-50 px-2 py-0.5 text-[10px] font-semibold text-sky-700">
                          Impuesto
                        </span>
                      )}
                    </span>
                  )}
                  <ActionsMenu
                    ariaLabel={`Acciones del trámite ${row.referenceNumber}`}
                    items={buildRowActions(row)}
                  />
                </div>
              </td>
            </tr>
          ))}
        </tbody>
      </table>
      </div>
      <OtTablePagination
        totalCount={totalCount}
        page={page}
        pageSize={pageSize}
        onPageChange={onPageChange}
      />
    </div>
  );
}
