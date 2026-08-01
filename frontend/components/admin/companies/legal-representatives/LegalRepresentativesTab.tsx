"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, ChevronLeft, ChevronRight, Loader2, MailCheck, Vault } from "lucide-react";
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
import { useCompanyTabsNav } from "../CompanyConfigTabs";
import {
  LegalRepresentativesFormPanel,
  type PanelMode,
} from "./LegalRepresentativesFormPanel";
import { fullName, maskDocument, procedureTypeLabels, signatureStatus } from "./legalRepresentativesDisplay";

const PAGE_SIZE = 20;

/**
 * Pestaña "Representantes legales" (HU #10904, Feature #10852): CRUD paginado del directorio de
 * representantes por compañía. HU #11178: una sola superficie — el panel unificado `view`/`create`/`edit`
 * reemplaza la ventana de detalle separada (`LegalRepresentativeDetailModal`, retirada en esta HU).
 * El número de documento (PII, Ley 1581) se enmascara en la tabla.
 */
export function LegalRepresentativesTab({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const nav = useCompanyTabsNav();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<LegalRepresentativeItem[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  // Catálogo de tipos de trámite asignables (activos + publicados), cargado del backend con sus IDs
  // reales; alimenta el multiselect del formulario y las etiquetas de la tabla.
  const [procedureTypes, setProcedureTypes] = useState<AssignableProcedureType[]>([]);

  // HU #11178 — panel unificado: modo + id del representante seleccionado.
  const [panelOpen, setPanelOpen] = useState(false);
  const [panelMode, setPanelMode] = useState<PanelMode>("create");
  const [panelRepresentativeId, setPanelRepresentativeId] = useState<string | null>(null);

  const [toDelete, setToDelete] = useState<LegalRepresentativeItem | null>(null);
  const [deleting, setDeleting] = useState(false);
  // Id del representante recién guardado sin firma ni identidad (banner con acciones).
  const [pendingSignatureId, setPendingSignatureId] = useState<string | null>(null);
  const [sendingIdentityId, setSendingIdentityId] = useState<string | null>(null);

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
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  // Carga del catálogo de tipos de trámite asignables (una vez por tenant).
  useEffect(() => {
    const controller = new AbortController();
    fetchAssignableProcedureTypes(tenantId, controller.signal)
      .then((types) => {
        if (!controller.signal.aborted) setProcedureTypes(types);
      })
      .catch(() => {
        /* silencioso: el aviso de "sin tipos habilitados" cubre el caso */
      });
    return () => controller.abort();
  }, [tenantId]);

  // ── Apertura del panel en cada modo ─────────────────────────────────────────

  /** AC4: «Nuevo representante» → panel en alta (formulario en blanco). */
  const openCreate = () => {
    setPanelMode("create");
    setPanelRepresentativeId(null);
    setPanelOpen(true);
  };

  /** AC1: «Ver» → panel en consulta con toda la información del representante. */
  const openView = (item: LegalRepresentativeItem) => {
    setPanelMode("view");
    setPanelRepresentativeId(item.id);
    setPanelOpen(true);
  };

  /** «Editar» desde la tabla → panel en edición directamente. */
  const openEdit = (item: LegalRepresentativeItem) => {
    setPanelMode("edit");
    setPanelRepresentativeId(item.id);
    setPanelOpen(true);
  };

  /** AC2: el usuario pulsó «Editar» DENTRO del panel en consulta → cambia modo sin cerrar. */
  const handleSwitchToEdit = () => {
    setPanelMode("edit");
  };

  const closePanel = () => {
    setPanelOpen(false);
    setPanelRepresentativeId(null);
  };

  // ── Submit / saved ───────────────────────────────────────────────────────────

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
      // AC5: el panel NO se cierra; pasa a modo edición sobre el representante recién creado.
      show("Representante registrado. Puedes editar sus detalles.", "success");
      setPanelMode("edit");
      setPanelRepresentativeId(saved.id);
    } else {
      show("Representante actualizado.", "success");
      closePanel();
    }
  };

  // ── Eliminación ──────────────────────────────────────────────────────────────

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

  // ── Envío de identidad ───────────────────────────────────────────────────────

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

  const goToVault = () => {
    if (nav.baulVisible) {
      nav.goToBaul();
    } else {
      show(
        "Activa el Baúl de Firmas en la configuración de la empresa para registrar firmas.",
        "error",
      );
    }
  };

  const totalPages = Math.max(1, Math.ceil(totalCount / PAGE_SIZE));
  const pendingItem = pendingSignatureId
    ? items.find((i) => i.id === pendingSignatureId) ?? null
    : null;

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={openCreate}
    >
      Registrar primer representante
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="max-w-xl text-[11px] opacity-60">
          Gestiona los representantes legales de las compañías que la gestora representa, con sus
          datos, los tipos de trámite que pueden firmar y su estado de firma o validación de identidad.
        </p>
        <button
          type="button"
          className="shrink-0 rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          onClick={openCreate}
        >
          Nuevo representante
        </button>
      </div>

      {pendingItem && (
        <div
          role="status"
          className="flex flex-col gap-3 rounded-xl border px-4 py-3 sm:flex-row sm:items-center sm:justify-between"
          style={{ borderColor: "#F9AC00", background: "rgba(249,172,0,0.08)" }}
        >
          <p className="text-[11px] font-medium" style={{ color: "#8a6000" }}>
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
            <SignatureAction icon={Vault} label="Registrar en baúl" onClick={goToVault} />
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
                  style={{ color: "#162744" }}
                >
                  <th
                    scope="col"
                    className="rounded-l-xl px-4 py-2.5"
                    style={{ background: "#DFE5ED" }}
                  >
                    Representante
                  </th>
                  <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Documento
                  </th>
                  <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Trámites
                  </th>
                  <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                    Firma / Identidad
                  </th>
                  <th
                    scope="col"
                    className="rounded-r-xl px-4 py-2.5 text-right"
                    style={{ background: "#DFE5ED" }}
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
                    <tr key={item.id} className="bg-white dark:bg-[#0B0F14]">
                      <td className="rounded-l-xl border-y border-l px-4 py-3 font-semibold">
                        {fullName(item)}
                      </td>
                      <td className="border-y px-4 py-3 font-mono">
                        {item.documentType} {maskDocument(item.documentNumber)}
                      </td>
                      <td className="border-y px-4 py-3">
                        <div className="flex flex-wrap gap-1">
                          {tramites.length === 0 ? (
                            <span className="opacity-50">—</span>
                          ) : (
                            tramites.map((t, i) => (
                              <StatusBadge key={`${item.id}-${i}`} tone="info" label={t} />
                            ))
                          )}
                        </div>
                      </td>
                      <td className="border-y px-4 py-3">
                        <StatusBadge tone={st.tone} label={st.label} />
                      </td>
                      <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                        <div className="flex flex-wrap justify-end gap-1.5">
                          {!item.hasSignatureOrIdentity && (
                            <>
                              <RowButton
                                label="Validar identidad"
                                busy={sendingIdentityId === item.id}
                                onClick={() => void handleSendIdentity(item)}
                              />
                              <RowButton label="Baúl" onClick={goToVault} />
                            </>
                          )}
                          <RowButton
                            label="Ver"
                            onClick={() => openView(item)}
                            ariaLabel={`Ver detalle de ${fullName(item)}`}
                          />
                          <RowButton
                            label="Editar"
                            onClick={() => openEdit(item)}
                            ariaLabel={`Editar ${fullName(item)}`}
                          />
                          <RowButton
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
            <p className="opacity-60">{totalCount} representantes</p>
            <div className="flex items-center gap-2">
              <button
                type="button"
                aria-label="Página anterior"
                disabled={page <= 1}
                onClick={() => setPage((p) => Math.max(1, p - 1))}
                className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
              >
                <ChevronLeft className="h-3.5 w-3.5" /> Anterior
              </button>
              <span className="font-semibold" style={{ color: "#557EFF" }}>
                {page} / {totalPages}
              </span>
              <button
                type="button"
                aria-label="Página siguiente"
                disabled={page >= totalPages}
                onClick={() => setPage((p) => Math.min(totalPages, p + 1))}
                className="flex items-center gap-1 rounded-lg border px-2.5 py-1.5 font-medium disabled:opacity-40"
              >
                Siguiente <ChevronRight className="h-3.5 w-3.5" />
              </button>
            </div>
          </div>
        </div>
      </UiStateBoundary>

      {/* HU #11178 — panel unificado (view / create / edit). Reemplaza LegalRepresentativeDetailModal. */}
      <LegalRepresentativesFormPanel
        open={panelOpen}
        mode={panelMode}
        representativeId={panelRepresentativeId}
        tenantId={tenantId}
        procedureTypes={procedureTypes}
        onClose={closePanel}
        onSubmit={handleSubmit}
        onSaved={handleSaved}
        onError={(message) => show(message, "error")}
        onSwitchToEdit={handleSwitchToEdit}
      />

      {toDelete && (
        <Modal
          open
          onClose={() => setToDelete(null)}
          busy={deleting}
          size="sm"
          icon={AlertTriangle}
          iconBg="#FF4E00"
          title="Eliminar representante"
        >
          <p className="mt-2 text-sm opacity-80">
            Vas a eliminar a <strong>{fullName(toDelete)}</strong> del directorio de representantes de
            esta compañía. Esta acción no se puede deshacer.
          </p>
          <div className="mt-5 flex gap-3">
            <button
              type="button"
              onClick={() => setToDelete(null)}
              disabled={deleting}
              className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void confirmDelete()}
              disabled={deleting}
              className="flex flex-1 items-center justify-center gap-2 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
              style={{ background: "#FF4E00" }}
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
  icon: typeof MailCheck;
  label: string;
  busy?: boolean;
  onClick: () => void;
}) {
  return (
    <button
      type="button"
      onClick={onClick}
      disabled={busy}
      className="flex items-center gap-1.5 rounded-lg px-3 py-1.5 text-[11px] font-semibold text-white disabled:opacity-60"
      style={{ background: "#557EFF" }}
    >
      {busy ? <Loader2 className="h-3.5 w-3.5 animate-spin" /> : <Icon className="h-3.5 w-3.5" />}
      {label}
    </button>
  );
}

function RowButton({
  label,
  onClick,
  ariaLabel,
  danger = false,
  busy = false,
}: {
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
      className="flex items-center gap-1 rounded-lg border px-3 py-1.5 text-[11px] font-semibold disabled:opacity-60"
      style={danger ? { color: "#FF4E00", borderColor: "#f0c38e" } : undefined}
    >
      {busy && <Loader2 className="h-3 w-3 animate-spin" />}
      {label}
    </button>
  );
}
