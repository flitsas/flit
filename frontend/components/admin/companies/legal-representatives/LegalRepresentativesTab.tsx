"use client";

import { useCallback, useEffect, useState } from "react";
import {
  AlertTriangle,
  Building2,
  Loader2,
  MailCheck,
  Pencil,
  Trash2,
  Vault,
  type LucideIcon,
} from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Modal } from "@/components/atom/Modal";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import { StatusBadge } from "@/components/atom/StatusBadge";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";
import { IDENTITY_MODULE_HREF } from "@/lib/admin/identity-vigencia";
import {
  createLegalRepresentative,
  deleteLegalRepresentative,
  fetchAssignableProcedureTypes,
  fetchLegalRepresentatives,
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
  rlPrimaryCtaClass,
  rlPrimaryCtaStyle,
} from "./rl-flit-styles";

const PAGE_SIZE = 20;

/**
 * Directorio de representantes legales.
 * Acciones por fila (iconos lineales FLIT): Editar, Empresas, Eliminar.
 * Bug #13055 — tabla homologada con la de Trámites (table-styles, RowActions, Pagination y el loader
 * del carrito), sin tarjeta blanca envolvente.
 * La ficha completa (modo view) queda disponible en código pero sin entrada en el grid.
 */
export function LegalRepresentativesTab({
  tenantId,
  networkHeadId,
}: {
  tenantId: string;
  networkHeadId?: string | null;
}) {
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
  const [pendingSignatureId, setPendingSignatureId] = useState<string | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const result = await fetchLegalRepresentatives(
          tenantId,
          page,
          PAGE_SIZE,
          signal,
          networkHeadId,
        );
        if (signal?.aborted) return;
        setItems(result.data);
        setTotalCount(result.totalCount);
        setStatus(result.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId, page, networkHeadId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  useEffect(() => {
    const controller = new AbortController();
    fetchAssignableProcedureTypes(tenantId, controller.signal, networkHeadId)
      .then((types) => {
        if (!controller.signal.aborted) setProcedureTypes(types);
      })
      .catch(() => {
        /* el aviso de "sin tipos habilitados" cubre el caso */
      });
    return () => controller.abort();
  }, [tenantId, networkHeadId]);

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
      ? createLegalRepresentative(tenantId, input, networkHeadId)
      : updateLegalRepresentative(tenantId, panelRepresentativeId, input, networkHeadId);

  const handleSaved = (saved: LegalRepresentativeSaved) => {
    const wasCreate = panelMode === "create";
    setPendingSignatureId(
      saved.signals.includes(SIGNAL_SIN_FIRMA_NI_IDENTIDAD) ? saved.id : null,
    );
    void load();

    if (wasCreate) {
      show(
        "Representante registrado. Usa el ícono de empresas (el edificio) de su fila para asociar NITs y escrituras.",
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
      await deleteLegalRepresentative(tenantId, toDelete.id, networkHeadId);
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
            identidad vigente. Valida su identidad desde el módulo Identidad o vincula una firma
            para que pueda firmar sus trámites.
          </p>
          <div className="flex shrink-0 gap-2">
            <a
              href={IDENTITY_MODULE_HREF}
              className={rlPrimaryCtaClass}
              style={rlPrimaryCtaStyle}
            >
              <MailCheck className="h-3.5 w-3.5" />
              Ir al módulo Identidad
            </a>
            <SignatureAction
              icon={Vault}
              label="Asociar firma"
              onClick={() => openEdit(pendingItem)}
            />
          </div>
        </div>
      )}

      {status === "loading" ? (
        <CarLoaderModal label="Cargando representantes legales…" />
      ) : (
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
            <table
              className="min-w-[820px] text-xs"
              style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}
            >
              <caption className="sr-only">Representantes legales de la compañía</caption>
              <thead>
                <tr>
                  <th
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    Representante
                  </th>
                  <th
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS}`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    Documento
                  </th>
                  <th
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS}`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    Trámites
                  </th>
                  <th
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS}`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
                  >
                    Firma / Identidad
                  </th>
                  <th
                    scope="col"
                    className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`}
                    style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}
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
                    <tr key={item.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
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
                        <RowActions
                          actions={[
                            {
                              icon: Pencil,
                              label: `Editar persona y firma de ${fullName(item)}`,
                              onClick: () => openEdit(item),
                              tone: "primary",
                            },
                            {
                              icon: Building2,
                              label: `Asociar empresas de ${fullName(item)}`,
                              onClick: () => openCompanies(item),
                            },
                            {
                              icon: Trash2,
                              label: `Eliminar ${fullName(item)}`,
                              onClick: () => setToDelete(item),
                              tone: "danger",
                            },
                          ]}
                        />
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>

          <Pagination page={page} pageSize={PAGE_SIZE} totalCount={totalCount} onPageChange={setPage} />
        </div>
      </UiStateBoundary>
      )}

      <LegalRepresentativesFormPanel
        open={panelOpen}
        mode={panelMode}
        tenantId={tenantId}
        networkHeadId={networkHeadId}
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
            Vas a eliminar a <strong>{fullName(toDelete)}</strong> del directorio. No aparecerá en
            esta pantalla ni se precargará en trámites nuevos. Los trámites ya radicados conservan
            la relación.
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
