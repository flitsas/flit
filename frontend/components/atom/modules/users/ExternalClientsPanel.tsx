"use client";

// HU #13200 (Feature #13065, Épica #12737) — Submódulo «Clientes de integración» del módulo Usuarios, solo
// SuperAdmin (AC6 lo asegura Usuarios.tsx). Administra los clientes EXTERNOS que leen el feed de trámites de
// todas las compañías (p. ej. Flito) con la API de la HU #13088. Patrón base: submódulo «Clientes ICT».
//
// El secreto solo existe en memoria mientras se muestra la tarjeta (AC2/AC4): no se guarda en estado
// persistente, almacenamiento del navegador ni logs, y al cerrar la tarjeta se descarta.
import { useCallback, useEffect, useState } from "react";
import { Check, Copy, KeyRound, LockOpen, Pencil, PlugZap, Power, RefreshCcw, X } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { DataTable, type DataTableColumn } from "@/components/atom/DataTable";
import { RowActions, type RowAction } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { formatFechaHora } from "@/lib/format/date";
import {
  EXTERNAL_SCOPE_ATTACHMENTS,
  EXTERNAL_SCOPE_PII,
  createExternalClient,
  fetchExternalClients,
  isExternalClientLocked,
  regenerateExternalClientSecret,
  unlockExternalClient,
  updateExternalClient,
  type ExternalClient,
} from "@/lib/api/external-clients";
import { ExternalClientActionDialog, type ExternalClientAction } from "./ExternalClientActionDialog";
import { ExternalClientFormModal } from "./ExternalClientFormModal";

function formatOptional(iso: string | null): string {
  return iso ? formatFechaHora(new Date(iso)) : "—";
}

type FormState = { mode: "create" } | { mode: "edit"; client: ExternalClient } | null;
type ActionState = { action: ExternalClientAction; client: ExternalClient } | null;

export function ExternalClientsPanel() {
  const [clients, setClients] = useState<ExternalClient[]>([]);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [form, setForm] = useState<FormState>(null);
  const [pending, setPending] = useState<ActionState>(null);
  const [revealed, setRevealed] = useState<{ clientId: string; secret: string } | null>(null);
  const [copied, setCopied] = useState(false);
  // HU #13918 — tabla estándar con paginación en cliente: el listado llega completo.
  const pg = usePaginacion("users.ExternalClientsPanel");

  const load = useCallback(async () => {
    setStatus("loading");
    try {
      const items = await fetchExternalClients();
      setClients(items);
      setStatus(items.length === 0 ? "empty" : "ready");
    } catch {
      setStatus("error");
    }
  }, []);

  useEffect(() => {
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga async: los setState ocurren tras el await
    void load();
  }, [load]);

  function replace(updated: ExternalClient) {
    setClients((prev) => prev.map((c) => (c.id === updated.id ? updated : c)));
  }

  function reveal(clientId: string, secret: string) {
    setCopied(false);
    setRevealed({ clientId, secret });
  }

  async function copySecret() {
    if (!revealed) return;
    await navigator.clipboard?.writeText(revealed.secret);
    setCopied(true);
  }

  async function runAction(action: ExternalClientAction, client: ExternalClient, revocarAnterior: boolean) {
    switch (action) {
      case "regenerate": {
        const res = await regenerateExternalClientSecret(client.id, revocarAnterior);
        replace(res.client);
        reveal(res.client.clientId, res.clientSecret);
        break;
      }
      case "unlock":
        replace(await unlockExternalClient(client.id));
        break;
      case "must-rotate":
        replace(await updateExternalClient(client.id, { mustRotate: true }));
        break;
      case "toggle-active":
        replace(await updateExternalClient(client.id, { isActive: !client.isActive }));
        break;
    }
    setPending(null);
  }

  function actionsFor(client: ExternalClient): RowAction[] {
    const actions: RowAction[] = [
      { icon: Pencil, label: `Editar ${client.clientId}`, onClick: () => setForm({ mode: "edit", client }) },
      {
        icon: RefreshCcw,
        label: `Regenerar secreto de ${client.clientId}`,
        tone: "primary",
        onClick: () => setPending({ action: "regenerate", client }),
      },
    ];
    // AC5: «Desbloquear» solo tiene sentido si está bloqueado.
    if (isExternalClientLocked(client)) {
      actions.push({
        icon: LockOpen,
        label: `Desbloquear ${client.clientId}`,
        tone: "primary",
        onClick: () => setPending({ action: "unlock", client }),
      });
    }
    if (!client.mustRotate) {
      actions.push({
        icon: KeyRound,
        label: `Obligar a rotar el secreto de ${client.clientId}`,
        tone: "danger",
        onClick: () => setPending({ action: "must-rotate", client }),
      });
    }
    actions.push({
      icon: Power,
      label: `${client.isActive ? "Desactivar" : "Activar"} ${client.clientId}`,
      tone: client.isActive ? "danger" : "primary",
      onClick: () => setPending({ action: "toggle-active", client }),
    });
    return actions;
  }

  const columns: DataTableColumn<ExternalClient>[] = [
    {
      key: "cliente",
      header: "Cliente",
      render: (c) => (
        <>
          <span className="block font-mono text-sm font-semibold">{c.clientId}</span>
          <span className="block text-xs text-[#59677D] dark:text-white/70">{c.displayName}</span>
        </>
      ),
    },
    {
      key: "finalidad",
      header: "Finalidad",
      cellClassName: "max-w-[280px] text-xs text-[#59677D] dark:text-white/78",
      render: (c) => c.purpose,
    },
    {
      key: "permisos",
      header: "Permisos",
      render: (c) => (
        <div className="flex flex-wrap gap-1">
          <StatusBadge label="Lectura" tone="info" ariaLabel="Permiso: lectura de trámites" />
          {c.scopes.includes(EXTERNAL_SCOPE_PII) ? (
            <StatusBadge label="Datos personales" tone="info" ariaLabel="Permiso: datos personales sin enmascarar" />
          ) : (
            <StatusBadge label="Enmascarado" tone="neutral" ariaLabel="Sin permiso de datos personales: llegan enmascarados" />
          )}
          {c.scopes.includes(EXTERNAL_SCOPE_ATTACHMENTS) && (
            <StatusBadge label="Adjuntos" tone="info" ariaLabel="Permiso: envío de adjuntos" />
          )}
        </div>
      ),
    },
    {
      key: "estado",
      header: "Estado",
      render: (c) => (
        <div className="flex flex-wrap gap-1">
          <StatusBadge label={c.isActive ? "Activo" : "Inactivo"} tone={c.isActive ? "success" : "neutral"} />
          {isExternalClientLocked(c) && (
            <StatusBadge
              label={`Bloqueado hasta ${formatOptional(c.lockedUntil)}`}
              tone="danger"
              ariaLabel={`Bloqueado por intentos fallidos hasta ${formatOptional(c.lockedUntil)}`}
            />
          )}
          {c.mustRotate && <StatusBadge label="Debe rotar" tone="warning" ariaLabel="Debe rotar el secreto" />}
        </div>
      ),
    },
    {
      key: "ultimo-pase",
      header: "Último pase",
      cellClassName: "whitespace-nowrap text-xs",
      render: (c) => formatOptional(c.lastTokenAt),
    },
    {
      key: "alta",
      header: "Alta",
      cellClassName: "whitespace-nowrap text-xs",
      render: (c) => formatOptional(c.createdAt),
    },
    {
      key: "acciones",
      header: "Acciones",
      align: "right",
      render: (c) => <RowActions actions={actionsFor(c)} />,
    },
  ];

  return (
    <div className="flex flex-col gap-4">
      <div className="flex flex-wrap items-center justify-between gap-3">
        <p className="max-w-3xl text-sm text-[#59677D] dark:text-white/78">
          Sistemas externos que leen el feed de trámites de todas las compañías (p. ej. Flito), uno por ambiente.
          El secreto se genera al crear o regenerar y se muestra una sola vez.
        </p>
        <button
          type="button"
          onClick={() => setForm({ mode: "create" })}
          className="inline-flex shrink-0 items-center gap-2 rounded-full px-5 py-2.5 text-sm font-semibold text-white shadow-[0_10px_22px_rgba(79,116,201,0.22)] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2"
          style={{ background: "linear-gradient(135deg, var(--color-flit-brand) 0%, var(--color-flit-tech) 100%)" }}
        >
          <PlugZap className="h-4 w-4" aria-hidden="true" />
          Nuevo cliente
        </button>
      </div>

      {revealed && (
        <section
          aria-labelledby="external-client-secret-title"
          className="rounded-[18px] border border-flit-brand/35 bg-white p-4 shadow-[0_8px_24px_rgba(22,39,68,0.08)] dark:border-white/15 dark:bg-[#162744]"
        >
          <div className="flex flex-wrap items-start justify-between gap-3">
            <div className="flex min-w-0 flex-col gap-2">
              <h3 id="external-client-secret-title" className="flex items-center gap-2 text-sm font-bold text-flit-primary dark:text-white">
                <KeyRound className="h-4 w-4 text-flit-brand" aria-hidden="true" />
                Secreto de «{revealed.clientId}»
              </h3>
              <p className="text-xs font-semibold text-[#C2410C]">
                Cópielo ahora: no se volverá a mostrar ni se puede recuperar. Entrégueselo al sistema externo por un
                canal seguro.
              </p>
              <code className="break-all rounded-[10px] border border-[#DFE5ED] bg-[#EEF5FF] px-3 py-2 font-mono text-sm text-flit-primary dark:border-white/15 dark:bg-white/5 dark:text-white">
                {revealed.secret}
              </code>
            </div>
            <div className="flex items-center gap-2">
              <button
                type="button"
                onClick={() => void copySecret()}
                className="inline-flex items-center gap-1.5 rounded-full border border-flit-brand px-4 py-2 text-xs font-semibold text-[color:var(--flit-brand-ink)] hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 dark:hover:bg-white/5"
              >
                {copied ? <Check className="h-4 w-4" aria-hidden="true" /> : <Copy className="h-4 w-4" aria-hidden="true" />}
                {copied ? "Copiado" : "Copiar secreto"}
              </button>
              <button
                type="button"
                aria-label="Cerrar: el secreto no se volverá a mostrar"
                onClick={() => setRevealed(null)}
                className="inline-flex min-h-[40px] min-w-[40px] items-center justify-center rounded-full text-flit-primary hover:bg-[#F4F8FF] focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand focus-visible:ring-offset-2 dark:text-white dark:hover:bg-white/5"
              >
                <X className="h-4 w-4" aria-hidden="true" />
              </button>
            </div>
          </div>
        </section>
      )}

      <UiStateBoundary
        status={status}
        emptyMessage="No hay clientes de integración. Cree el primero con «Nuevo cliente»."
        errorMessage="No se pudieron cargar los clientes de integración."
        onRetry={() => void load()}
        skeletonRows={3}
      >
        <DataTable<ExternalClient>
          ariaLabel="Clientes de integración externos"
          columns={columns}
          rows={pg.paginar(clients)}
          getRowKey={(c) => c.id}
          pagination={{
            page: pg.page,
            pageSize: pg.pageSize,
            totalCount: clients.length,
            onPageChange: pg.setPage,
            onPageSizeChange: pg.setPageSize,
          }}
        />
      </UiStateBoundary>

      {form && (
        <ExternalClientFormModal
          client={form.mode === "edit" ? form.client : undefined}
          onClose={() => setForm(null)}
          onCreate={async (body) => {
            const res = await createExternalClient(body);
            setClients((prev) => [res.client, ...prev]);
            pg.setPage(1); // el cliente nuevo queda arriba: se muestra la primera página
            setStatus("ready");
            setForm(null);
            reveal(res.client.clientId, res.clientSecret);
          }}
          onUpdate={async (id, body) => {
            replace(await updateExternalClient(id, body));
            setForm(null);
          }}
        />
      )}

      {pending && (
        <ExternalClientActionDialog
          action={pending.action}
          client={pending.client}
          onClose={() => setPending(null)}
          onConfirm={({ revocarAnterior }) => runAction(pending.action, pending.client, revocarAnterior)}
        />
      )}
    </div>
  );
}
