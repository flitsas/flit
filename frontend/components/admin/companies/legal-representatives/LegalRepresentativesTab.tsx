"use client";

import { useCallback, useEffect, useState } from "react";
import {
  AlertTriangle,
  Building2,
  ChevronLeft,
  ChevronRight,
  Loader2,
  MailCheck,
  Pencil,
  Trash2,
  Vault,
  type LucideIcon,
} from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { Modal } from "@/components/atom/Modal";
import { StatusBadge } from "@/components/atom/StatusBadge";
import { ApiValidationError } from "@/lib/api/types";
import {
  createLegalRepresentative,
  deleteLegalRepresentative,
  fetchAssignableProcedureTypes,
  fetchLegalRepresentatives,
  sendLegalRepresentativeIdentity,
  updateLegalRepresentative,
  SIGNAL_SIN_FIRMA_NI_IDENTIDAD,
  type AssignableProcedureType,
  type LegalRepresentativeInput,
  type LegalRepresentativeItem,
  type LegalRepresentativeSaved,
} from "@/lib/api/admin-legal-representatives";
import {
  LegalRepresentativesFormPanel,
  type PanelMode,
} from "./LegalRepresentativesFormPanel";
import {
  formatDocumentNumber,
  fullName,
  procedureTypeLabels,
  signatureStatus,
} from "./legalRepresentativesDisplay";
import {
  RL_COLOR,
  rlDangerCtaClass,
  rlDangerCtaStyle,
  rlDangerGhostStyle,
  rlIconActionClass,
  rlPrimaryCtaClass,
  rlPrimaryCtaStyle,
} from "./rl-flit-styles";

const PAGE_SIZE = 20;

/**
 * Directorio de representantes legales.
 * Acciones por fila (iconos lineales FLIT): Editar, Empresas, Eliminar.
 * La ficha completa (modo view) queda disponible en código pero sin entrada en el grid.
 */
export function LegalRepresentativesTab({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<LegalRepresentativeItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [procedureTypes, setProcedureTypes] = useState<AssignableProcedureType[]>([]);

  const [panelOpen, setPanelOpen] = useState(false);
  const [panelMode, setPanelMode] = useState<PanelMode>("create");
  const [panelRepresentativeId, setPanelRepresentativeId] = useState<string | null>(null);

  const [toDelete, setToDelete] = useState<LegalRepresentativeItem | null>(null);
  const [deleting, setDeleting] = useState(false);
  const [sendingIdentityId, setSendingIdentityId] = useState<string | null>(null);
  const [pendingSignatureId, setPendingSignatureId] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchLegalRepresentatives(tenantId, page, PAGE_SIZE, signal);
        if (signal?.aborted) return;
        setItems(result.data);
        setTotalCount(result.totalCount);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId, page],
  );

  useEffect(() => {
    const controller = new AbortController();
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    const controller = new AbortController();
    fetchAssignableProcedureTypes(tenantId, controller.signal)
      .then((types) => {
        if (!controller.signal.aborted) setProcedureTypes(types);
      })
      .catch(() => {
        /* el aviso de "sin tipos habilitados" cubre el caso */
      });
    return () => controller.abort();
  }, [tenantId]);

  const openCreate = () => {
    setPanelMode("create");
    setPanelRepresentativeId(null);
    setPanelOpen(true);
  };

  const openEdit = (item: LegalRepresentativeItem) => {
    setPanelMode("edit");
    setPanelRepresentativeId(item.id);
    setPanelOpen(true);
  };

  const openCompanies = (item: LegalRepresentativeItem) => {
    setPanelMode("companies");
    setPanelRepresentativeId(item.id);
    setPanelOpen(true);
  };

  const handleSwitchToEdit = () => {
    setPanelMode("edit");
  };

  const handleSwitchToCompanies = () => {
    setPanelMode("companies");
  };

  const closePanel = () => {
    setPanelOpen(false);
    setPanelRepresentativeId(null);
  };

  const handleSubmit = (input: LegalRepresentativeInput): Promise<LegalRepresentativeSaved> =>
    panelMode === "create" || !panelRepresentativeId
      ? createLegalRepresentative(tenantId, input)
      : updateLegalRepresentative(tenantId, panelRepresentativeId, input);

  const handleSaved = (saved: LegalRepresentativeSaved) => {
    const wasCreate = panelMode === "create";
    setPendingSignatureId(
      saved.signals.includes(SIGNAL_SIN_FIRMA_NI_IDENTIDAD) ? saved.id : null,
    );
    void load();

    if (wasCreate) {
      show(
        "Representante registrado. Usa «Empresas» en el listado para asociar NITs y escrituras.",
        "success",
      );
      closePanel();
    } else if (panelMode === "companies") {
      show("Empresas actualizadas.", "success");
      closePanel();
    } else {
      show("Representante actualizado.", "success");
      closePanel();
    }
  };

  const confirmDelete = async () => {
    if (!toDelete) return;
    setDeleting(true);
    try {
      await deleteLegalRepresentative(tenantId, toDelete.id);
      show(`Representante ${fullName(toDelete)} eliminado.`, "success");
      if (pendingSignatureId === toDelete.id) setPendingSignatureId(null);
      setToDelete(null);
      await load();
    } catch {
      show("No se pudo eliminar el representante.", "error");
    } finally {
      setDeleting(false);
    }
  };

  const handleSendIdentity = async (item: LegalRepresentativeItem) => {
    setSendingIdentityId(item.id);
    try {
      await sendLegalRepresentativeIdentity(tenantId, item.id);
      show(`Correo de validación de identidad enviado a ${fullName(item)}.`, "success");
    } catch (err) {
      if (err instanceof ApiValidationError) {
        const emailErr = err.errors.find((e) => e.field === "email");
        show(
          emailErr?.message ??
            "No se pudo enviar el correo: revisa el correo del representante.",
          "error",
        );
      } else {
        show("No se pudo enviar el correo de validación de identidad. Intenta de nuevo.", "error");
      }
    } finally {
      setSendingIdentityId(null);
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const pendingItem = pendingSignatureId
    ? items.find((i) => i.id === pendingSignatureId) ?? null
    : null;

  const emptyCta = (
    <button
      type="button"
      onClick={openCreate}
      className={rlPrimaryCtaClass}
      style={rlPrimaryCtaStyle}
    >
      Registrar primer representante
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="max-w-xl text-[11px]" style={{ color: RL_COLOR.secondary }}>
          Gestiona los representantes legales de las compañías que la gestora representa, con sus
          datos, los tipos de trámite que pueden firmar y su estado de firma o validación de identidad.
        </p>
        <button
          type="button"
          className={`shrink-0 ${rlPrimaryCtaClass}`}
          style={rlPrimaryCtaStyle}
          onClick={openCreate}
        >
          Nuevo representante
        </button>
      </div>

      {pendingItem && (
        <div
          role="status"
          className="flex flex-col gap-3 rounded-xl border px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
          style={{
            borderColor: RL_COLOR.pending,
            background: RL_COLOR.pendingBg,
          }}
        >
          <p className="text-[11px] font-medium" style={{ color: RL_COLOR.pendingText }}>
            <strong>{fullName(pendingItem)}</strong> quedó guardado sin firma ni validación de
            identidad vigente. Vincula una para que pueda firmar sus trámites.
          </p>
          <div className="flex shrink-0 gap-2">
            <SignatureAction
              icon={MailCheck}
              label="Enviar correo de validación"
              busy={sendingIdentityId === pendingItem.id}
              onClick={() => void handleSendIdentity(pendingItem)}
            />
            <SignatureAction
              icon={Vault}
              label="Asociar firma"
              onClick={() => openEdit(pendingItem)}
            />
          </div>
        </div>
      )}

      <UiStateBoundary
        status={status}
        emptyMessage="Esta compañía aún no tiene representantes legales registrados."
        emptyCta={emptyCta}
        errorMessage="No se pudieron cargar los representantes legales."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <div className="flex flex-col">
          <div className="overflow-x-auto">
            <table className="w-full min-w-[820px] border-separate border-spacing-y-2 text-xs">
              <caption className="sr-only">Representantes legales de la compañía</caption>
              <thead>
                <tr
                  className="text-left text-[10px] font-semibold uppercase"
                  style={{ color: RL_COLOR.navy }}
                >
                  <th
                    scope="col"
                    className="rounded-l-xl px-4 py-2.5"
                    style={{ background: RL_COLOR.tableHeader }}
                  >
                    Representante
                  </th>
                  <th
                    scope="col"
                    className="px-4 py-2.5"
                    style={{ background: RL_COLOR.tableHeader }}
                  >
                    Documento
                  </th>
                  <th
                    scope="col"
                    className="px-4 py-2.5"
                    style={{ background: RL_COLOR.tableHeader }}
                  >
                    Trámites
                  </th>
                  <th
                    scope="col"
                    className="px-4 py-2.5"
                    style={{ background: RL_COLOR.tableHeader }}
                  >
                    Firma / Identidad
                  </th>
                  <th
                    scope="col"
                    className="rounded-r-xl px-4 py-2.5 text-right"
                    style={{ background: RL_COLOR.tableHeader }}
                  >
                    Acciones
                  </th>
                </tr>
              </thead>
              <tbody>
                {items.map((item) => {
                  const st = signatureStatus(item.hasSignatureOrIdentity);
                  const tramites = procedureTypeLabels(item.procedureTypeIds, procedureTypes);
                  return (
                    <tr key={item.id} className="bg-white">
                      <td
                        className="rounded-l-xl border-y border-l px-4 py-3 font-semibold"
                        style={{ borderColor: RL_COLOR.border, color: RL_COLOR.navy }}
                      >
                        {fullName(item)}
                      </td>
                      <td
                        className="border-y px-4 py-3 font-mono"
                        style={{ borderColor: RL_COLOR.border }}
                      >
                        {item.documentType} {formatDocumentNumber(item.documentNumber)}
                      </td>
                      <td className="border-y px-4 py-3" style={{ borderColor: RL_COLOR.border }}>
                        <div className="flex flex-wrap gap-1">
                          {tramites.length === 0 ? (
                            <span style={{ color: RL_COLOR.muted }}>—</span>
                          ) : (
                            tramites.map((t, i) => (
                              <StatusBadge key={`${item.id}-${i}`} tone="info" label={t} />
                            ))
                          )}
                        </div>
                      </td>
                      <td className="border-y px-4 py-3" style={{ borderColor: RL_COLOR.border }}>
                        <StatusBadge tone={st.tone} label={st.label} />
                      </td>
                      <td
                        className="rounded-r-xl border-y border-r px-4 py-3 text-right"
                        style={{ borderColor: RL_COLOR.border }}
                      >
                        <div className="flex flex-wrap justify-end gap-1.5">
                          <RowButton
                            icon={Pencil}
                            label="Editar"
                            onClick={() => openEdit(item)}
                            ariaLabel={`Editar persona y firma de ${fullName(item)}`}
                          />
                          <RowButton
                            icon={Building2}
                            label="Empresas"
                            onClick={() => openCompanies(item)}
                            ariaLabel={`Asociar empresas de ${fullName(item)}`}
                          />
                          <RowButton
                            icon={Trash2}
                            label="Eliminar"
                            danger
                            onClick={() => setToDelete(item)}
                            ariaLabel={`Eliminar ${fullName(item)}`}
                          />
                        </div>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <div className="mt-2 flex items-center justify-between pt-1 text-[11px]">
            <p style={{ color: RL_COLOR.secondary }}>{totalCount} representantes</p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                aria-label="Página anterior"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
                style={{ borderColor: RL_COLOR.border, color: RL_COLOR.navy }}
              >
                <ChevronLeft className="h-3.5 w-3.5" /> Anterior
              </button>
              <span className="font-semibold" style={{ color: RL_COLOR.brand }}>
                {page} / {totalPages}
              </span>
              <button
                type="button"
                aria-label="Página siguiente"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
                style={{ borderColor: RL_COLOR.border, color: RL_COLOR.navy }}
              >
                Siguiente <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        </div>
      </UiStateBoundary>

      <LegalRepresentativesFormPanel
        open={panelOpen}
        mode={panelMode}
        tenantId={tenantId}
        representativeId={panelRepresentativeId}
        procedureTypes={procedureTypes}
        onClose={closePanel}
        onSubmit={handleSubmit}
        onSaved={handleSaved}
        onError={(msg) => show(msg, "error")}
        onSwitchToEdit={handleSwitchToEdit}
        onSwitchToCompanies={handleSwitchToCompanies}
      />

      {toDelete && (
        <Modal
          open
          onClose={() => setToDelete(null)}
          busy={deleting}
          size="sm"
          icon={AlertTriangle}
          iconBg={RL_COLOR.danger}
          title="Eliminar representante"
        >
          <p className="mt-2 text-sm" style={{ color: RL_COLOR.secondary }}>
            Vas a eliminar a <strong>{fullName(toDelete)}</strong> del directorio de representantes de
            esta compañía. Esta acción no se puede deshacer.
          </p>
          <div className="mt-5 flex gap-3">
            <button
              type="button"
              onClick={() => setToDelete(null)}
              disabled={deleting}
              className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
              style={{ borderColor: RL_COLOR.border, color: RL_COLOR.navy }}
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void confirmDelete()}
              disabled={deleting}
              className={`flex-1 ${rlDangerCtaClass}`}
              style={rlDangerCtaStyle}
            >
              {deleting && <Loader2 className="h-4 w-4 animate-spin" />}
              Eliminar
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}

function SignatureAction({
  icon: Icon,
  label,
  busy = false,
  onClick,
}: {
  icon: LucideIcon;
  label: string;
  busy?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={busy}
      className={rlPrimaryCtaClass}
      style={rlPrimaryCtaStyle}
    >
      {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Icon className="h-3.5 w-3.5" />}
      {label}
    </button>
  );
}

function RowButton({
  icon: Icon,
  label,
  onClick,
  ariaLabel,
  danger = false,
  busy = false,
}: {
  icon: LucideIcon;
  label: string;
  onClick: () => void;
  ariaLabel?: string;
  danger?: boolean;
  busy?: boolean;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={busy}
      aria-label={ariaLabel ?? label}
      title={label}
      className={rlIconActionClass}
      style={
        danger
          ? rlDangerGhostStyle
          : { color: RL_COLOR.navy, borderColor: RL_COLOR.border }
      }
    >
      {busy ? (
        <Loader2 className="h-3.5 w-3.5 animate-spin" aria-hidden="true" />
      ) : (
        <Icon className="h-3.5 w-3.5" aria-hidden="true" />
      )}
      <span className="hidden sm:inline">{label}</span>
    </button>
  );
}
