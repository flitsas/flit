"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, Ban, Eye, Loader2, Pencil } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { Modal } from "@/components/atom/Modal";
import { Pagination } from "@/components/atom/Pagination";
import { RowActions } from "@/components/atom/RowActions";
import { usePaginacion } from "@/components/atom/usePaginacion";
import {
  createSignatureVaultEntry,
  fetchSignatureVault,
  revokeSignatureVaultEntry,
  type SignatureVaultInput,
  type SignatureVaultItem,
} from "@/lib/api/admin-signature-vault";
import { SignatureVaultFormPanel } from "./SignatureVaultFormPanel";
import { SignatureVaultEditPanel } from "./SignatureVaultEditPanel";
import { SignatureVaultDetailModal } from "./SignatureVaultDetailModal";
import { ESTADO_BADGE, ESTADO_LABELS, formatDate } from "./signatureVaultDisplay";
import { formatDocumentNumber } from "@/lib/display/document-number";
import {
  TABLA_HEADER_BG,
  TABLA_HEADER_CELL_CLS,
  TABLA_HEADER_FG,
  TABLA_ROW_HOVER_CLS,
} from "@/components/atom/table-styles";

/**
 * Pestaña "Baúl de Firmas" (HU #10644): registra, lista, consulta, CORRIGE y anula firmas de
 * apoderados de la compañía. Visible solo cuando `baulFirmasActivo` está activo (lo
 * controla el contenedor). 4 estados de UI + WCAG 2.1 AA. El artefacto de firma nunca
 * se descarga al cliente.
 */
export function SignatureVaultTab({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const pg = usePaginacion();
  const [items, setItems] = useState<SignatureVaultItem[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [detail, setDetail] = useState<SignatureVaultItem | null>(null);
  const [toEdit, setToEdit] = useState<SignatureVaultItem | null>(null);
  const [toRevoke, setToRevoke] = useState<SignatureVaultItem | null>(null);
  const [revoking, setRevoking] = useState(false);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const list = await fetchSignatureVault(tenantId, signal);
        if (signal?.aborted) return;
        setItems(list);
        setStatus(list.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [tenantId],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleSubmit = (input: SignatureVaultInput) => createSignatureVaultEntry(tenantId, input);

  const confirmRevoke = async () => {
    if (!toRevoke) return;
    setRevoking(true);
    try {
      await revokeSignatureVaultEntry(tenantId, toRevoke.id);
      show(`Firma de ${toRevoke.fullName} anulada.`, "success");
      setToRevoke(null);
      await load();
    } catch {
      show("No se pudo anular la firma.", "error");
    } finally {
      setRevoking(false);
    }
  };

  const emptyCta = (
    <button
      type="button"
      className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
      style={{ background: "#557EFF" }}
      onClick={() => setFormOpen(true)}
    >
      Registrar primera firma
    </button>
  );

  return (
    <div className="space-y-4">
      <div className="flex items-center justify-between gap-3">
        <p className="max-w-xl text-xs opacity-60">
          Registra las firmas de los apoderados de la compañía para reutilizarlas al firmar los
          documentos de cada trámite. El archivo de firma se guarda de forma segura y no se descarga
          desde esta consola.
        </p>
        <button
          type="button"
          className="shrink-0 rounded-xl px-4 py-2 text-xs font-semibold text-white"
          style={{ background: "#557EFF" }}
          onClick={() => setFormOpen(true)}
        >
          Nueva firma
        </button>
      </div>

      {status === "loading" ? (
        <CarLoaderModal label="Cargando firmas del baúl…" />
      ) : (
      <UiStateBoundary
        status={status}
        emptyMessage="Esta compañía aún no tiene firmas registradas en el baúl."
        emptyCta={emptyCta}
        errorMessage="No se pudieron cargar las firmas del baúl."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        {/* Bug #13055 — tabla homologada con el modelo de trámites */}
        <div className="overflow-x-auto">
          <table className="min-w-[820px] text-xs" style={{ width: "100%", borderCollapse: "separate", borderSpacing: "0 8px" }}>
            <caption className="sr-only">Firmas de apoderados registradas en el baúl</caption>
            <thead>
              <tr>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-l-xl`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Registro
                </th>
                <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Documento
                </th>
                <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Código hash
                </th>
                <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Apoderado
                </th>
                <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Vigencia
                </th>
                <th scope="col" className={TABLA_HEADER_CELL_CLS} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Estado
                </th>
                <th scope="col" className={`${TABLA_HEADER_CELL_CLS} rounded-r-xl text-right`} style={{ background: TABLA_HEADER_BG, color: TABLA_HEADER_FG }}>
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {pg.paginar(items).map((item) => {
                const registro = item.fechaRegistro ?? item.createdAt ?? null;
                const revoked = item.estado === "revocada";
                const badge = ESTADO_BADGE[item.estado] ?? ESTADO_BADGE.vencida;
                return (
                  <tr key={item.id} className={`bg-white dark:bg-[#0B0F14] ${TABLA_ROW_HOVER_CLS}`}>
                    <td className={`rounded-l-xl border-y border-l px-4 py-3 ${revoked ? "opacity-60" : ""}`} style={{ borderColor: "#DFE5ED" }}>
                      {formatDate(registro)}
                    </td>
                    <td className={`border-y px-4 py-3 ${revoked ? "opacity-60" : ""}`} style={{ borderColor: "#DFE5ED" }}>
                      <span className="font-mono">
                        {item.documentType} {formatDocumentNumber(item.documentNumber)}
                      </span>
                    </td>
                    <td className={`border-y px-4 py-3 font-mono ${revoked ? "opacity-60" : ""}`} style={{ borderColor: "#DFE5ED" }}>
                      {item.codigoHash ?? "—"}
                    </td>
                    <td className={`border-y px-4 py-3 font-semibold ${revoked ? "opacity-60" : ""}`} style={{ borderColor: "#DFE5ED" }}>
                      {item.fullName}
                    </td>
                    <td className={`border-y px-4 py-3 ${revoked ? "opacity-60" : ""}`} style={{ borderColor: "#DFE5ED" }}>
                      {formatDate(item.vigenciaDesde)} — {formatDate(item.vigenciaHasta)}
                    </td>
                    <td className="border-y px-4 py-3" style={{ borderColor: "#DFE5ED" }}>
                      <span
                        className="inline-block rounded-full border px-2 py-0.5 text-xs font-semibold"
                        style={{ color: badge.color, borderColor: badge.border, background: badge.bg }}
                      >
                        {ESTADO_LABELS[item.estado] ?? item.estado}
                      </span>
                    </td>
                    <td className="rounded-r-xl border-y border-r px-4 py-3 text-right" style={{ borderColor: "#DFE5ED" }}>
                      {/* Corregir/Anular solo sobre firmas activas: el contenido de una revocada es
                          histórico. Sin Corregir, arreglar un código hash mal digitado obligaba a
                          anular la firma y volver a registrarla. */}
                      <RowActions
                        actions={[
                          {
                            icon: Eye,
                            label: `Ver detalle de la firma de ${item.fullName}`,
                            onClick: () => setDetail(item),
                            tone: "primary",
                          },
                          ...(!revoked
                            ? [
                                {
                                  icon: Pencil,
                                  label: `Corregir la firma de ${item.fullName}`,
                                  onClick: () => setToEdit(item),
                                  tone: "primary" as const,
                                },
                                {
                                  icon: Ban,
                                  label: `Anular la firma de ${item.fullName}`,
                                  onClick: () => setToRevoke(item),
                                  tone: "danger" as const,
                                },
                              ]
                            : []),
                        ]}
                      />
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>

        {/* Bug #13055 — paginación en cliente con filas por página, como el listado de trámites */}
        <Pagination
          page={pg.page}
          pageSize={pg.pageSize}
          totalCount={items.length}
          onPageChange={pg.setPage}
          onPageSizeChange={pg.setPageSize}
          noun="firmas"
        />
      </UiStateBoundary>
      )}

      <SignatureVaultFormPanel
        open={formOpen}
        onClose={() => setFormOpen(false)}
        onSubmit={handleSubmit}
        onSaved={() => {
          setFormOpen(false);
          show("Firma registrada correctamente.", "success");
          void load();
        }}
        onError={(message) => show(message, "error")}
      />

      <SignatureVaultDetailModal item={detail} onClose={() => setDetail(null)} />

      <SignatureVaultEditPanel
        tenantId={tenantId}
        item={toEdit}
        onClose={() => setToEdit(null)}
        onSaved={() => {
          show("Firma corregida.", "success");
          void load();
        }}
      />

      {toRevoke && (
        <Modal
          open
          onClose={() => setToRevoke(null)}
          busy={revoking}
          size="sm"
          icon={AlertTriangle}
          iconBg="#FF4E00"
          title="Anular firma"
        >
          <p className="mt-2 text-sm opacity-80">
            Vas a anular la firma de <strong>{toRevoke.fullName}</strong>. Quedará como revocada y ya
            no podrá reutilizarse en nuevos trámites. Esta acción no se puede deshacer.
          </p>
          <div className="mt-5 flex gap-3">
            <button
              type="button"
              onClick={() => setToRevoke(null)}
              disabled={revoking}
              className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
            >
              Cancelar
            </button>
            <button
              type="button"
              onClick={() => void confirmRevoke()}
              disabled={revoking}
              className="flex flex-1 items-center justify-center gap-2 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
              style={{ background: "#FF4E00" }}
            >
              {revoking && <Loader2 className="h-4 w-4 animate-spin" />}
              Anular firma
            </button>
          </div>
        </Modal>
      )}
    </div>
  );
}

