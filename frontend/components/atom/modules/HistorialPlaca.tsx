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
//  4. HU #12196 — `initialPlaca` permite entrar con una placa ya consultada (`?m=historial-placa
//     &placa=ABC123`), que es como se llega desde una fila del listado de trámites. La consulta la
//     dispara ESTE módulo con el mismo `load` del formulario: el llamador no trae datos, solo la
//     placa, así que entrar por el atajo y entrar escribiéndola producen exactamente la misma
//     pantalla — que es el criterio del PO.
import { useCallback, useEffect, useMemo, useRef, useState } from "react";
import { Eye, Search } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { StatusBadge, type StatusTone } from "@/components/atom/StatusBadge";
import { ModuleTitle } from "./ModuleTitle";
import { TramiteDetalleModal } from "@/components/operacion/TramiteDetalleModal";
import { tramitesClient } from "@/lib/api/tramites-client";
import type { InstanceSummary } from "@/lib/api/types/procedure-runtime";
import { estadoLabel } from "@/lib/tramites/estados";

/** Fases de la vista. `idle` = todavía no se consultó nada (distinto de vacío). */
type Phase = "idle" | "loading" | "error" | "empty" | "ready";

const PAGE_SIZE = 20;

// Clases del input: el color sale de tokens, no de hex (HU #12197). `border` a secas ya toma
// `--border` (== #DFE5ED en claro, white/10 en oscuro) desde la capa base de globals.css, así que
// la pareja `border-[#DFE5ED] dark:border-white/10` era el token escrito a mano.
const INPUT_CLS =
  "w-full rounded-xl border bg-white px-3 py-2 text-xs uppercase text-flit-primary placeholder:text-muted-foreground focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand dark:bg-[#0B0F14] dark:text-white";

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

/** Forma canónica de la placa en el cliente: sin espacios y en mayúscula (el servidor repite). */
function normalizarPlaca(value: string | null | undefined): string {
  return (value ?? "").trim().toUpperCase();
}

function textoOGuion(value: string | null | undefined): string {
  const trimmed = value?.trim();
  return trimmed ? trimmed : "—";
}

export function HistorialPlaca({
  isSuperAdmin = false,
  initialPlaca = null,
}: {
  isSuperAdmin?: boolean;
  /** Placa precargada desde otra pantalla (HU #12196). `null` = entrada normal, sin consulta. */
  initialPlaca?: string | null;
}) {
  const [placaInput, setPlacaInput] = useState(() => normalizarPlaca(initialPlaca));
  const [appliedPlaca, setAppliedPlaca] = useState("");
  const [phase, setPhase] = useState<Phase>("idle");
  const [errorMessage, setErrorMessage] = useState<string | null>(null);
  const [rows, setRows] = useState<InstanceSummary[]>([]);
  const [total, setTotal] = useState(0);
  const [page, setPage] = useState(1);
  /** Fila abierta en el detalle. `null` = modal cerrado (el modal es controlado por `open`). */
  const [detalle, setDetalle] = useState<InstanceSummary | null>(null);

  const load = useCallback(async (placa: string, pageToLoad: number) => {
    const normalized = normalizarPlaca(placa);
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

  // HU #12196 — entrada con placa precargada. El `ref` guarda la última placa auto-consultada para
  // no repetir la petición en cada render, y a la vez permitir que llegar OTRA vez con una placa
  // distinta (segundo atajo, sin desmontar el módulo) vuelva a consultar.
  const autoConsultada = useRef<string | null>(null);
  useEffect(() => {
    const normalized = normalizarPlaca(initialPlaca);
    if (!normalized || autoConsultada.current === normalized) return;
    autoConsultada.current = normalized;
    setPlacaInput(normalized);
    void load(normalized, 1);
  }, [initialPlaca, load]);

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
            className="inline-flex items-center justify-center rounded-lg border p-1.5 text-flit-brand transition hover:bg-flit-brand/10 focus:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 focus-visible:ring-offset-background"
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
    // Cáscara canónica de módulo (la misma de Auditoría, LOG QX, Reportes…): fondo de app,
    // respiro px-6/pt-6/pb-10 y tinta de marca. Antes era `p-4` sin `app-bg`, que dejaba el
    // módulo con menos aire y sin el fondo azul claro del prototipo.
    <div
      className="app-bg flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10 text-flit-primary dark:text-white"
      data-testid="historial-placa-module"
    >
      {/* El título de una pantalla interna va SIEMPRE en tarjeta blanca (prototipo FLIT +
          HU #10493): el h1 suelto sobre el fondo era el patrón de otra familia de pantallas. */}
      <ModuleTitle
        title="Historial por placa"
        subtitle="Consulte todos los trámites asociados a una placa, del más reciente al más antiguo."
      />

      <form
        className="flex shrink-0 flex-wrap items-end gap-3 rounded-2xl border bg-white p-3 dark:bg-[#0B0F14]"
        onSubmit={handleSubmit}
        aria-label="Consultar historial de trámites por placa"
      >
        <div className="min-w-[10rem] flex-1 sm:max-w-xs">
          <label
            htmlFor="historial-placa-input"
            className="mb-1 block text-xs font-semibold text-flit-primary dark:text-white"
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
          className="inline-flex items-center gap-1.5 rounded-xl bg-flit-brand px-4 py-2 text-xs font-semibold text-white focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 focus-visible:ring-offset-background"
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
