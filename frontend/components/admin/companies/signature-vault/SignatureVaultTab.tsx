"use client";

import { useCallback, useEffect, useState } from "react";
import { AlertTriangle, Loader2 } from "lucide-react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { Modal } from "@/components/atom/Modal";
import {
  createSignatureVaultEntry,
  fetchSignatureVault,
  revokeSignatureVaultEntry,
  type SignatureVaultInput,
  type SignatureVaultItem,
} from "@/lib/api/admin-signature-vault";
import { SignatureVaultFormPanel } from "./SignatureVaultFormPanel";
import { SignatureVaultDetailModal } from "./SignatureVaultDetailModal";
import { ESTADO_BADGE, ESTADO_LABELS, formatDate } from "./signatureVaultDisplay";

/**
 * Pestaña "Baúl de Firmas" (HU #10644): registra, lista, consulta y anula firmas de
 * apoderados de la compañía. Visible solo cuando `baulFirmasActivo` está activo (lo
 * controla el contenedor). 4 estados de UI + WCAG 2.1 AA. El artefacto de firma nunca
 * se descarga al cliente.
 */
export function SignatureVaultTab({ tenantId }: { tenantId: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [items, setItems] = useState<SignatureVaultItem[]>([]);
  const [formOpen, setFormOpen] = useState(false);
  const [detail, setDetail] = useState<SignatureVaultItem | null>(null);
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
        <p className="max-w-xl text-[11px] opacity-60">
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

      <UiStateBoundary
        status={status}
        emptyMessage="Esta compañía aún no tiene firmas registradas en el baúl."
        emptyCta={emptyCta}
        errorMessage="No se pudieron cargar las firmas del baúl."
        onRetry={() => void load()}
        skeletonRows={4}
      >
        <div className="overflow-x-auto">
          <table className="w-full border-separate border-spacing-y-2 text-xs">
            <caption className="sr-only">Firmas de apoderados registradas en el baúl</caption>
            <thead>
              <tr className="text-left text-[10px] font-semibold uppercase" style={{ color: "#162744" }}>
                <th scope="col" className="rounded-l-xl px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Registro
                </th>
                <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  NIT empresa
                </th>
                <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Documento
                </th>
                <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Apoderado
                </th>
                <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Vigencia
                </th>
                <th scope="col" className="px-4 py-2.5" style={{ background: "#DFE5ED" }}>
                  Estado
                </th>
                <th scope="col" className="rounded-r-xl px-4 py-2.5 text-right" style={{ background: "#DFE5ED" }}>
                  Acciones
                </th>
              </tr>
            </thead>
            <tbody>
              {items.map((item) => {
                const registro = item.fechaRegistro ?? item.createdAt ?? null;
                const revoked = item.estado === "revocada";
                const badge = ESTADO_BADGE[item.estado] ?? ESTADO_BADGE.vencida;
                return (
                  <tr key={item.id} className="bg-white dark:bg-[#0B0F14]">
                    <td className={`rounded-l-xl border-y border-l px-4 py-3 ${revoked ? "opacity-60" : ""}`}>
                      {formatDate(registro)}
                    </td>
                    <td className={`border-y px-4 py-3 font-mono ${revoked ? "opacity-60" : ""}`}>
                      {item.nitEmpresa}
                    </td>
                    <td className={`border-y px-4 py-3 ${revoked ? "opacity-60" : ""}`}>
                      <span className="font-mono">
                        {item.documentType} {maskDocument(item.documentNumber)}
                      </span>
                    </td>
                    <td className={`border-y px-4 py-3 font-semibold ${revoked ? "opacity-60" : ""}`}>
                      {item.fullName}
                    </td>
                    <td className={`border-y px-4 py-3 ${revoked ? "opacity-60" : ""}`}>
                      {formatDate(item.vigenciaDesde)} — {formatDate(item.vigenciaHasta)}
                    </td>
                    <td className="border-y px-4 py-3">
                      <span
                        className="inline-block rounded-full border px-2 py-0.5 text-[10px] font-semibold"
                        style={{ color: badge.color, borderColor: badge.border, background: badge.bg }}
                      >
                        {ESTADO_LABELS[item.estado] ?? item.estado}
                      </span>
                    </td>
                    <td className="rounded-r-xl border-y border-r px-4 py-3 text-right">
                      <div className="flex justify-end gap-2">
                        <button
                          type="button"
                          className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold"
                          onClick={() => setDetail(item)}
                          aria-label={`Ver detalle de la firma de ${item.fullName}`}
                        >
                          Ver detalle
                        </button>
                        {!revoked && (
                          <button
                            type="button"
                            className="rounded-lg border px-3 py-1.5 text-[11px] font-semibold"
                            style={{ color: "#FF4E00", borderColor: "#f0c38e" }}
                            onClick={() => setToRevoke(item)}
                            aria-label={`Anular la firma de ${item.fullName}`}
                          >
                            Anular
                          </button>
                        )}
                      </div>
                    </td>
                  </tr>
                );
              })}
            </tbody>
          </table>
        </div>
      </UiStateBoundary>

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

/** Enmascara el número de documento (PII, Ley 1581): solo los últimos 4 dígitos. */
function maskDocument(documentNumber: string): string {
  if (documentNumber.length <= 4) return "••••";
  return `••••${documentNumber.slice(-4)}`;
}
