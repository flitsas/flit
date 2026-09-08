"use client";

// Módulo "Historial por placa" (Feature #12189 · HU #12194).
//
// Superficie dedicada para responder «qué le ha pasado a esta placa dentro de FLIT»: buscador de
// placa + tabla de los trámites asociados, en orden cronológico descendente.
//
// Dos decisiones que conviene no revertir sin leer el plan técnico:
//
//  1. El orden y el alcance por compañía los fija el SERVIDOR (`GET /instances/plate-history`).
//     SuperAdmin recibe la placa en todas las compañías; el resto, solo la suya. Aquí no se
//     reordena ni se manda tenant: duplicar esa decisión en el cliente es cómo nacen los bugs de
//     scope que ya mordieron en improntas.
//  2. Una placa sin trámites es un RESULTADO, no un fallo: el backend responde 200 con lista vacía
//     y la vista lo pinta como estado vacío redactado con la placa consultada, distinto del estado
//     inicial "aún no se ha buscado nada".
//  3. HU #12195 — el detalle de cada fila NO es un modal propio: se reutiliza el MISMO
//     `TramiteDetalleModal` del módulo de Trámites, en modo `readOnly`. Clonarlo aquí crearía dos
//     detalles del mismo trámite que se separarían al primer cambio funcional.
import { useCallback, useMemo, useState } from "react";
import { Eye, History, Search } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { TramiteDetalleModal } from "@/components/operacion/TramiteDetalleModal";
import { tramitesClient } from "@/lib/api/tramites-client";
import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";
import { estadoLabel } from "@/lib/tramites/estados";

/** Fases de la vista. `idle` = todavía no se consultó nada (distinto de vacío). */
type Phase = "idle" | "loading" | "error" | "empty" | "ready";

const PAGE_SIZE = 20;

const INPUT_CLS =
  "w-full rounded-xl border border-[#DFE5ED] bg-white px-3 py-2 text-xs uppercase text-[#162244] placeholder:text-[#59677D]/70 focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] dark:border-white/10 dark:bg-[#0B0F14] dark:text-white";

/** Tono semántico del chip de estado (verde válido / azul proceso / naranja-rojo alerta / gris). */
const ESTADO_TONE: Record<string, StatusTone> = {
  borrador: "neutral",
  anulado: "neutral",
  preparado: "info",
  entregado: "info",
  aprobado: "success",
  rechazado: "danger",
  subsanacion: "warning",
};

function formatDateTime(iso: string | null | undefined): string {
  if (!iso) return "—";
  const date = new Date(iso);
  if (Number.isNaN(date.getTime())) return iso;
  return date.toLocaleString("es-CO", { dateStyle: "short", timeStyle: "short" });
}

function textoOGuion(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "—";
}

export function HistorialPlaca({ isSuperAdmin = false }: { isSuperAdmin?: boolean }) {
  const [placaInput, setPlacaInput] = useState("");
  const [appliedPlaca, setAppliedPlaca] = useState("");
  const [phase, setPhase] = useState<Phase>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [rows, setRows] = useState<InstanceSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  /** Fila abierta en el detalle. `null` = modal cerrado (el modal es controlado por `open`). */
  const [detalle, setDetalle] = useState<InstanceSummary | null>(null);

  const load = useCallback(async (placa: string, pageToLoad: number) => {
    const normalized = placa.trim().toUpperCase();
    // Placa vacía: no se consulta al servidor. Volver a `idle` es deliberado — nadie ha buscado
    // nada, así que anunciar "no hay resultados" sería responder una pregunta que no se hizo.
    if (!normalized) {
      setPhase("idle");
      setRows([]);
      setTotal(0);
      setPage(1);
      setAppliedPlaca("");
      setErrorMessage(null);
      return;
    }

    setPhase("loading");
    setErrorMessage(null);
    try {
      const res = await tramitesClient.listPlateHistory({
        placa: normalized,
        skip: (pageToLoad - 1) * PAGE_SIZE,
        take: PAGE_SIZE,
      });
      setRows(res.items);
      setTotal(res.total);
      setPage(pageToLoad);
      setAppliedPlaca(normalized);
      setPhase(res.items.length === 0 ? "empty" : "ready");
    } catch (err) {
      setRows([]);
      setTotal(0);
      setAppliedPlaca(normalized);
      setPhase("error");
      setErrorMessage(
        err instanceof Error && err.message
          ? err.message
          : "No se pudo consultar el historial de la placa.",
      );
    }
  }, []);

  const handleSubmit = (event: React.FormEvent) => {
    event.preventDefault();
    void load(placaInput, 1);
  };

  const columns: DataTableColumn<InstanceSummary>[] = useMemo(() => {
    const base: DataTableColumn<InstanceSummary>[] = [
      {
        key: "referenceNumber",
        header: "Radicado",
        cellClassName: "font-mono font-semibold",
        render: (row) => (
          <span title={`Id interno: ${row.id}`}>{textoOGuion(row.referenceNumber)}</span>
        ),
      },
      {
        key: "tipo",
        header: "Tipo de trámite",
        render: (row) => textoOGuion(row.tipoNombre ?? row.modalidad),
      },
      {
        key: "estado",
        header: "Estado",
        render: (row) => (
          <StatusBadge label={estadoLabel(row.estado)} tone={ESTADO_TONE[row.estado] ?? "neutral"} />
        ),
      },
      {
        key: "createdAt",
        header: "Radicado el",
        render: (row) => formatDateTime(row.createdAt),
      },
      {
        key: "updatedAt",
        header: "Última actualización",
        render: (row) => formatDateTime(row.updatedAt),
      },
      { key: "gestor", header: "Gestor", render: (row) => textoOGuion(row.gestorNombre) },
      {
        key: "organismoTransito",
        header: "Organismo de tránsito",
        render: (row) => textoOGuion(row.organismoTransito),
      },
      {
        key: "comprador",
        header: "Comprador",
        render: (row) => textoOGuion(row.compradorNombre),
      },
      {
        key: "vendedor",
        header: "Vendedor",
        render: (row) => textoOGuion(row.vendedorNombre),
      },
      {
        key: "vin",
        header: "VIN",
        cellClassName: "font-mono",
        render: (row) => textoOGuion(row.vin),
      },
      {
        key: "acciones",
        header: "Acciones",
        align: "center",
        render: (row) => (
          <button
            type="button"
            onClick={() => setDetalle(row)}
            // El nombre accesible lleva el radicado: en una tabla con N filas, diez botones
            // llamados "Ver detalle" son diez destinos indistinguibles para un lector de pantalla.
            aria-label={`Ver detalle del trámite ${row.referenceNumber ?? row.id}`}
            title="Ver detalle del trámite"
            data-testid={`historial-placa-ver-${row.id}`}
            className="inline-flex items-center justify-center rounded-lg border border-[#DFE5ED] p-1.5 text-[#557EFF] transition hover:bg-[#557EFF]/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2 dark:border-white/10"
          >
            <Eye className="h-3.5 w-3.5" aria-hidden />
          </button>
        ),
      },
    ];

    // Solo SuperAdmin consulta varias compañías a la vez (decisión D1 del PO): para el resto la
    // columna diría siempre lo mismo y solo restaría ancho a lo que sí distingue una fila de otra.
    if (!isSuperAdmin) return base;
    return [
      base[0],
      {
        key: "compania",
        header: "Compañía",
        render: (row: InstanceSummary) => textoOGuion(row.companiaNombre),
      },
      ...base.slice(1),
    ];
  }, [isSuperAdmin]);

  const boundaryStatus: UiStatus =
    phase === "loading"
      ? "loading"
      : phase === "error"
        ? "error"
        : phase === "empty"
          ? "empty"
          : "ready";

  // Texto que anuncia el resultado a lectores de pantalla: sin esto la búsqueda cambia la tabla en
  // silencio y quien navega por teclado no se entera de que ya hay (o no hay) resultados.
  const anuncio =
    phase === "loading"
      ? `Consultando el historial de la placa ${appliedPlaca || placaInput.trim().toUpperCase()}…`
      : phase === "error"
        ? (errorMessage ?? "No se pudo consultar el historial de la placa.")
        : phase === "empty"
          ? `No hay trámites registrados para la placa ${appliedPlaca}.`
          : phase === "ready"
            ? `${total} ${total === 1 ? "trámite encontrado" : "trámites encontrados"} para la placa ${appliedPlaca}.`
            : "";

  return (
    <div className="flex flex-col gap-4 p-4" data-testid="historial-placa-module">
      <header className="flex flex-wrap items-center gap-2">
        <History className="h-5 w-5 text-[#557EFF]" aria-hidden />
        <h1 className="text-xl font-semibold text-[#162744] dark:text-white">
          Historial por placa
        </h1>
      </header>
      <p className="max-w-3xl text-xs leading-relaxed text-[#59677D] dark:text-white/65">
        Consulte todos los trámites asociados a una placa, del más reciente al más antiguo.
      </p>

      <form
        className="flex flex-wrap items-end gap-3"
        onSubmit={handleSubmit}
        aria-label="Consultar historial de trámites por placa"
      >
        <div className="min-w-[10rem] flex-1 sm:max-w-xs">
          <label
            htmlFor="historial-placa-input"
            className="mb-1 block text-xs font-semibold text-[#162244] dark:text-white"
          >
            Placa
          </label>
          <input
            id="historial-placa-input"
            name="placa"
            type="text"
            value={placaInput}
            onChange={(e) => setPlacaInput(e.target.value.toUpperCase())}
            placeholder="ABC123"
            className={INPUT_CLS}
            autoComplete="off"
            data-testid="historial-placa-input"
          />
        </div>
        <button
          type="submit"
          className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-[#557EFF] focus-visible:ring-offset-2"
          style={{ background: "#557EFF" }}
          data-testid="historial-placa-search-btn"
        >
          <Search className="h-3.5 w-3.5" aria-hidden />
          Consultar
        </button>
      </form>

      <p role="status" aria-live="polite" className="sr-only" data-testid="historial-placa-anuncio">
        {anuncio}
      </p>

      {phase === "idle" ? (
        <div data-testid="historial-placa-idle">
          <UiStateBoundary
            status="empty"
            emptyMessage="Ingrese una placa y pulse Consultar para ver su historial de trámites."
          />
        </div>
      ) : (
        <UiStateBoundary
          status={boundaryStatus}
          onRetry={() => void load(appliedPlaca || placaInput, page)}
          skeletonRows={5}
          emptyMessage={
            appliedPlaca
              ? `No hay trámites registrados para la placa ${appliedPlaca}.`
              : "No hay trámites registrados para esta placa."
          }
          errorMessage={errorMessage ?? "No se pudo consultar el historial de la placa."}
        >
          <div data-testid="historial-placa-table">
            <DataTable
              columns={columns}
              rows={rows}
              getRowKey={(row) => row.id}
              ariaLabel={`Historial de trámites de la placa ${appliedPlaca}`}
              minWidth={1400}
              pagination={{
                page,
                pageSize: PAGE_SIZE,
                totalCount: total,
                onPageChange: (next) => void load(appliedPlaca, next),
              }}
            />
          </div>
        </UiStateBoundary>
      )}

      {/* Mismo modal «Ver» del módulo de Trámites, en modo consulta (decisión D3 del PO). Sin
          `onAbrirAsistente` y con `readOnly`: desde el historial no se edita ni se transiciona el
          trámite. `tenantId` solo viaja para SuperAdmin, que es el único que ve filas de otras
          compañías; para el resto el backend ya resolvió el alcance por su propio tenant. */}
      <TramiteDetalleModal
        open={detalle !== null}
        onClose={() => setDetalle(null)}
        instanceId={detalle?.id ?? null}
        tenantId={isSuperAdmin ? detalle?.tenantId : undefined}
        item={detalle}
        readOnly
      />
    </div>
  );
}
