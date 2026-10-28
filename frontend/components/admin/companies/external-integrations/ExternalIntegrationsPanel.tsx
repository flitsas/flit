"use client";

// HU #14068 (Feature #14067, Épica #14021) — Pestaña «Integraciones externas» de la ficha de la compañía (solo
// SuperAdmin; la ficha solo inyecta este panel para él). Administra la integración de Persona Natural ligada a la
// compañía con la API de la HU #14064: crear (el secreto se muestra una sola vez), rotar, bloquear, desbloquear y
// desactivar. Mismo modelo de tabla que Trámites: DataTable + RowActions + paginación numerada + filtros por
// columna. El secreto vive solo en memoria mientras se muestra su modal.
import { useCallback, useEffect, useMemo, useState } from "react";
import { Ban, KeyRound, LockKeyhole, LockOpen, PlugZap, Power } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { CreateButton } from "@/components/atom/CreateButton";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { EncabezadoFiltrable, TablaFiltrosVacio } from "@/components/atom/TablaFiltrosBar";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaHora } from "@/lib/format/date";
import { filtrarFilas, type ColumnaFiltro } from "@/lib/tablas/filtrar-filas";
import { useTablaFiltros } from "@/lib/tablas/useTablaFiltros";
import {
  blockExternalIntegration,
  createExternalIntegration,
  deactivateExternalIntegration,
  externalIntegrationStatus,
  fetchExternalIntegrations,
  rotateExternalIntegrationSecret,
  unblockExternalIntegration,
  type ExternalIntegration,
  type ExternalIntegrationStatus,
} from "@/lib/api/external-integrations";
import { ExternalIntegrationActionDialog, type ExternalIntegrationAction } from "./ExternalIntegrationActionDialog";
import { ExternalIntegrationFormModal } from "./ExternalIntegrationFormModal";
import { ExternalIntegrationSecretModal } from "./ExternalIntegrationSecretModal";

const ESTADO_LABEL: Record<ExternalIntegrationStatus, string> = {
  active: "Activa",
  blocked: "Bloqueada",
  inactive: "Desactivada",
};

const COLUMNAS_FILTRO: ColumnaFiltro[] = [
  { id: "integracion", label: "Conexión", tipo: "texto" },
  {
    id: "estado",
    label: "Estado",
    tipo: "seleccion",
    opciones: (Object.keys(ESTADO_LABEL) as ExternalIntegrationStatus[]).map((v) => ({ value: v, label: ESTADO_LABEL[v] })),
  },
];

function formatOptional(iso: string | null): string {
  return iso ? formatFechaHora(new Date(iso)) : "—";
}

/** Bloqueo del administrador (año 9999) frente al temporal por intentos fallidos. */
function blockedLabel(integration: ExternalIntegration): string {
  return integration.lockedUntil && new Date(integration.lockedUntil).getUTCFullYear() >= 9999
    ? "Bloqueada hasta que se desbloquee"
    : `Bloqueada hasta ${formatOptional(integration.lockedUntil)}`;
}

type ActionState = { action: ExternalIntegrationAction; integration: ExternalIntegration } | null;

export interface ExternalIntegrationsPanelProps {
  companyId: string;
}

export function ExternalIntegrationsPanel({ companyId }: ExternalIntegrationsPanelProps) {
  const [items, setItems] = useState<ExternalIntegration[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [creating, setCreating] = useState(false);
  const [pending, setPending] = useState<ActionState>(null);
  const [revealed, setRevealed] = useState<{ clientId: string; secret: string } | null>(null);
  const pg = usePaginacion("companies.ExternalIntegrationsPanel");
  const { setPage } = pg;
  const volverAPrimeraPagina = useCallback(() => setPage(1), [setPage]);
  const filtros = useTablaFiltros({ columnas: COLUMNAS_FILTRO, onCambio: volverAPrimeraPagina });

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const list = await fetchExternalIntegrations(companyId, signal);
        if (signal?.aborted) return;
        setItems(list);
        setStatus(list.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [companyId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const visibles = useMemo(
    () =>
      filtrarFilas(items, filtros.condicionesTotales, "", {
        columnas: {
          integracion: (i) => [i.clientId, i.displayName],
          estado: (i) => externalIntegrationStatus(i),
        },
      }),
    [items, filtros.condicionesTotales],
  );

  const hasActive = items.some((i) => i.isActive);

  function replace(updated: ExternalIntegration) {
    setItems((prev) => prev.map((i) => (i.id === updated.id ? updated : i)));
  }

  async function runAction(
    action: ExternalIntegrationAction,
    integration: ExternalIntegration,
    revocarAnterior: boolean,
  ) {
    switch (action) {
      case "rotate": {
        const res = await rotateExternalIntegrationSecret(companyId, integration.id, revocarAnterior);
        replace(res.client);
        setRevealed({ clientId: res.client.clientId, secret: res.clientSecret });
        break;
      }
      case "block":
        replace(await blockExternalIntegration(companyId, integration.id));
        break;
      case "unblock":
        replace(await unblockExternalIntegration(companyId, integration.id));
        break;
      case "deactivate":
        replace(await deactivateExternalIntegration(companyId, integration.id));
        break;
    }
    setPending(null);
  }

  function actionsFor(integration: ExternalIntegration): RowAction[] {
    const estado = externalIntegrationStatus(integration);
    const name = integration.clientId;
    if (estado === "inactive") {
      return [{ icon: Ban, label: `${name} está desactivada`, onClick: () => {}, disabled: true }];
    }
    const actions: RowAction[] = [
      {
        icon: KeyRound,
        label: `Generar clave nueva para ${name}`,
        tone: "primary",
        disabled: estado === "blocked",
        disabledTitle: "Desbloquea la conexión para generar una clave nueva",
        onClick: () => setPending({ action: "rotate", integration }),
      },
    ];
    actions.push(
      estado === "blocked"
        ? {
            icon: LockOpen,
            label: `Desbloquear ${name}`,
            tone: "primary",
            onClick: () => setPending({ action: "unblock", integration }),
          }
        : {
            icon: LockKeyhole,
            label: `Bloquear ${name}`,
            tone: "danger",
            onClick: () => setPending({ action: "block", integration }),
          },
    );
    actions.push({
      icon: Power,
      label: `Desactivar ${name}`,
      tone: "danger",
      onClick: () => setPending({ action: "deactivate", integration }),
    });
    return actions;
  }

  const columns: DataTableColumn<ExternalIntegration>[] = [
    {
      key: "integracion",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="integracion">
          Conexión
        </EncabezadoFiltrable>
      ),
      render: (i) => (
        <>
          <span className="block font-mono text-sm font-semibold">{i.clientId}</span>
          <span className="block text-xs text-[#59677D] dark:text-white/70">{i.displayName}</span>
        </>
      ),
    },
    {
      key: "tipo",
      header: "Tipo",
      render: () => <StatusBadge label="Persona Natural" tone="info" />,
    },
    {
      key: "finalidad",
      header: "Finalidad",
      cellClassName: "max-w-[280px] text-xs text-[#59677D] dark:text-white/78",
      render: (i) => i.purpose,
    },
    {
      key: "alcance",
      header: "Alcance",
      render: () => (
        <StatusBadge label="Solo lectura" tone="neutral" ariaLabel="Alcance: solo lectura de la fachada del portal" />
      ),
    },
    {
      key: "estado",
      header: (
        <EncabezadoFiltrable filtros={filtros} columnaId="estado">
          Estado
        </EncabezadoFiltrable>
      ),
      render: (i) => {
        const estado = externalIntegrationStatus(i);
        return (
          <div className="flex flex-wrap gap-1">
            {estado === "active" && <StatusBadge label="Activa" tone="success" />}
            {estado === "inactive" && <StatusBadge label="Desactivada" tone="neutral" />}
            {estado === "blocked" && <StatusBadge label={blockedLabel(i)} tone="danger" ariaLabel={`Estado: ${blockedLabel(i)}`} />}
            {i.isActive && i.mustRotate && (
              <StatusBadge label="Debe cambiar la clave" tone="warning" ariaLabel="Debe cambiar la clave secreta" />
            )}
          </div>
        );
      },
    },
    {
      key: "ultimo-pase",
      header: "Último acceso",
      cellClassName: "whitespace-nowrap text-xs",
      render: (i) => formatOptional(i.lastTokenAt),
    },
    {
      key: "alta",
      header: "Creada el",
      cellClassName: "whitespace-nowrap text-xs",
      render: (i) => formatOptional(i.createdAt),
    },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (i) => <RowActions actions={actionsFor(i)} />,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-start justify-between gap-3">
        <div className="flex max-w-3xl flex-col gap-1">
          <p className="text-sm text-[#59677D] dark:text-white/78">
            Aquí conectas el portal público de Persona Natural con esta compañía. La conexión solo puede consultar
            información, no modificarla. Al crearla o cambiar su clave, la clave secreta se muestra una sola vez.
          </p>
          {hasActive && (
            <p className="text-xs text-[#59677D] dark:text-white/70">
              Esta compañía ya tiene una conexión activa. Para crear otra, desactiva primero la actual.
            </p>
          )}
        </div>
        <CreateButton
          label="Crear conexión"
          icon={PlugZap}
          onClick={() => setCreating(true)}
          disabled={hasActive}
        />
      </div>

      {status === "loading" ? (
        <CarLoaderModal label="Cargando conexiones…" />
      ) : (
        <UiStateBoundary
          status={status}
          emptyMessage="Esta compañía todavía no tiene conexiones. Crea la de Persona Natural con el botón «Crear conexión»."
          errorMessage="No se pudieron cargar las conexiones."
          onRetry={() => void load()}
        >
          <DataTable<ExternalIntegration>
            ariaLabel="Conexiones externas de la compañía"
            columns={columns}
            rows={pg.paginar(visibles)}
            getRowKey={(i) => i.id}
            emptyContent={
              filtros.hayFiltros ? <TablaFiltrosVacio onLimpiar={filtros.limpiar} /> : undefined
            }
            pagination={{
              page: pg.page,
              pageSize: pg.pageSize,
              totalCount: visibles.length,
              onPageChange: pg.setPage,
              onPageSizeChange: pg.setPageSize,
            }}
          />
        </UiStateBoundary>
      )}

      {creating && (
        <ExternalIntegrationFormModal
          onClose={() => setCreating(false)}
          onCreate={async (body) => {
            const res = await createExternalIntegration(companyId, body);
            setItems((prev) => [res.client, ...prev]);
            pg.setPage(1);
            setStatus("ready");
            setCreating(false);
            setRevealed({ clientId: res.client.clientId, secret: res.clientSecret });
          }}
        />
      )}

      {pending && (
        <ExternalIntegrationActionDialog
          action={pending.action}
          integration={pending.integration}
          onClose={() => setPending(null)}
          onConfirm={({ revocarAnterior }) => runAction(pending.action, pending.integration, revocarAnterior)}
        />
      )}

      {revealed && (
        <ExternalIntegrationSecretModal
          clientId={revealed.clientId}
          secret={revealed.secret}
          onClose={() => setRevealed(null)}
        />
      )}
    </div>
  );
}
