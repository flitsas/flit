"use client";

import { useCallback, useEffect, useState } from "react";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { useToast } from "@/components/admin/Toast";
import { tramitesClient } from "@/lib/api/tramites-client";
import type { ProcedureTypeSummary } from "@/lib/api/types/procedure-parametrization";
import {
  adjuntarOtLicenciaTransito,
  approveOtClientProcedure,
  fetchOtAttachmentPreviewUrl,
  fetchOtBandejaHealth,
  fetchOtClientProcedures,
  fetchOtDocuments,
  fetchOtProfile,
  generarOtConsolidadoMaestro,
  rejectOtClientProcedure,
} from "@/lib/api/admin-ot";
import type { OtBandejaHealth, OtClientProcedure, OtProfile } from "@/lib/api/types-ot";
import { getToken } from "@/lib/api/client";
import { downloadFile } from "@/lib/api/download";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { Modal } from "@/components/atom/Modal";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import { FolderOpen } from "lucide-react";
import { ClientProceduresTable } from "./ClientProceduresTable";
import { assignPlateToProcedure, revokeProcedurePlate } from "@/lib/api/admin-plate-ranges";
import { OtDocumentosTab } from "./OtDocumentosTab";
import { OT_FILTER_FORM_CLS, OT_INPUT_CLS } from "./ot-form-styles";

const PAGE_SIZE = 20;

/**
 * Vista tenant admin — trámites de clientes OT (HU #10220).
 *
 * `transitOfficeId` (ruta /admin/transit-offices/[id]) scope-a la consulta para el
 * SuperAdmin: sin él, el backend resuelve el OT desde el tenant del token, que para
 * SuperAdmin no tiene perfil OT y la lista queda vacía (los trámites `entregado`
 * "desaparecen"). Para ot_admin el backend ignora el override (seguridad) y sigue
 * resolviendo por su propio tenant.
 */
export function ClientProceduresSection({ transitOfficeId }: { transitOfficeId?: string }) {
  const { show } = useToast();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [rows, setRows] = useState<OtClientProcedure[]>([]);
  const [totalCount, setTotalCount] = useState(0);
  const [page, setPage] = useState(1);
  // N 03 — `entregado` reemplaza a pending_ot como estado en cola de decisión OT.
  const [statusFilter, setStatusFilter] = useState("entregado");
  const [typeFilter, setTypeFilter] = useState("");
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeSummary[]>([]);
  const [approveTarget, setApproveTarget] = useState<OtClientProcedure | null>(null);
  const [rejectTarget, setRejectTarget] = useState<OtClientProcedure | null>(null);
  // Feature #10587 — asignar placa (preasignado) / revocar preasignación.
  const [assignTarget, setAssignTarget] = useState<OtClientProcedure | null>(null);
  const [plateInput, setPlateInput] = useState("");
  const [revokeTarget, setRevokeTarget] = useState<OtClientProcedure | null>(null);
  const [revokePlateReason, setRevokePlateReason] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  // Licencia de Tránsito opcional al aprobar; también adjuntable después (fila aprobada).
  const [ltFile, setLtFile] = useState<File | null>(null);
  const [ltTarget, setLtTarget] = useState<OtClientProcedure | null>(null);
  const [consolidadoActingId, setConsolidadoActingId] = useState<string | null>(null);
  const [acting, setActing] = useState(false);
  const [profile, setProfile] = useState<OtProfile | null>(null);
  // HU #10705 — panel de documentos del expediente
  const [documentosProcedure, setDocumentosProcedure] = useState<OtClientProcedure | null>(null);
  // Previsualización inline del consolidado (sin forzar descarga).
  const [preview, setPreview] = useState<{
    open: boolean;
    title: string;
    mimetype: string | null;
    url: string | null;
    loading: boolean;
    error: string | null;
    download: { procId: string; attId: string; filename: string } | null;
  }>({ open: false, title: "Consolidado", mimetype: null, url: null, loading: false, error: null, download: null });
  // Diagnóstico de bandeja (R09): entregados hacia el OT que no aparecen por falta de grant.
  const [health, setHealth] = useState<OtBandejaHealth | null>(null);

  const scope = transitOfficeId ? { transitOfficeId } : undefined;

  // El SuperAdmin supervisa la cola pero la decisión aprobar/rechazar es del OT admin
  // (los endpoints approve/reject no soportan el override de organismo del SuperAdmin).
  const [superAdmin] = useState(() => isSuperAdmin(decodeJwtPayload(getToken())));

  const isReadOnly = Boolean(
    profile?.operationMode === "quipux" && profile?.quipuxReadOnly,
  );

  useEffect(() => {
    const controller = new AbortController();
    fetchOtProfile(controller.signal, transitOfficeId ? { transitOfficeId } : undefined)
      .then(setProfile)
      .catch(() => setProfile(null));
    return () => controller.abort();
  }, [transitOfficeId]);

  useEffect(() => {
    tramitesClient
      .listPublishedProcedureTypes()
      .then(setProcedureTypes)
      .catch(() => setProcedureTypes([]));
  }, []);

  const load = useCallback(
    async (signal?: AbortSignal, targetPage = page) => {
      setStatus("loading");
      try {
        const result = await fetchOtClientProcedures(
          {
            status: statusFilter || undefined,
            procedureTypeId: typeFilter || undefined,
            page: targetPage,
            pageSize: PAGE_SIZE,
          },
          signal,
          transitOfficeId ? { transitOfficeId } : undefined,
        );
        if (signal?.aborted) return;
        setRows(result.data);
        setTotalCount(result.totalCount);
        setPage(result.page);
        setStatus(result.data.length === 0 ? "empty" : "ready");
        // Diagnóstico de bandeja (R09) — se refresca junto con la lista; nunca la bloquea.
        fetchOtBandejaHealth(signal, transitOfficeId ? { transitOfficeId } : undefined)
          .then((h) => {
            if (!signal?.aborted) setHealth(h);
          })
          .catch(() => {
            /* el diagnóstico es informativo: su fallo no afecta la bandeja */
          });
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [statusFilter, typeFilter, page, transitOfficeId],
  );

  useEffect(() => {
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(c.signal, page);
    return () => c.abort();
  }, [load, page]);

  const applyFilters = () => {
    setPage(1);
    void load(undefined, 1);
  };

  const confirmApprove = async () => {
    if (!approveTarget) return;
    setActing(true);
    try {
      // Se aprueba PRIMERO y luego se adjunta la LT: el gate de la LT exige el trámite en
      // entregado/aprobado. En la ruta de placa (Feature #10587) el trámite llega a la aprobación
      // en 'asignado', así que adjuntar antes fallaba con estado_invalido; tras aprobar queda
      // 'aprobado' (válido para la LT). El consolidado se genera on-demand y toma la LT vigente.
      const updated = await approveOtClientProcedure(approveTarget.id);
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));

      if (ltFile) {
        try {
          await adjuntarOtLicenciaTransito(approveTarget.id, ltFile, scope);
        } catch {
          // La aprobación YA quedó firme; solo falló el adjunto. Se puede reintentar con la
          // acción dedicada de Licencia de Tránsito.
          setApproveTarget(null);
          setLtFile(null);
          show(
            "Trámite aprobado, pero no se pudo adjuntar la Licencia de Tránsito. Reintenta la carga.",
            "error",
          );
          return;
        }
      }

      setApproveTarget(null);
      setLtFile(null);
      show(ltFile ? "Trámite aprobado con Licencia de Tránsito adjunta." : "Trámite aprobado.", "success");
    } catch {
      show("No se pudo aprobar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmAssignPlate = async () => {
    if (!assignTarget || !plateInput.trim()) return;
    setActing(true);
    try {
      await assignPlateToProcedure(assignTarget.id, plateInput.trim().toUpperCase());
      setRows((prev) =>
        prev.map((r) => (r.id === assignTarget.id ? { ...r, status: "asignado" } : r)),
      );
      setAssignTarget(null);
      setPlateInput("");
      show("Placa asignada al trámite.", "success");
    } catch {
      show("No se pudo asignar la placa.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmRevokePlate = async () => {
    if (!revokeTarget || !revokePlateReason.trim()) return;
    setActing(true);
    try {
      await revokeProcedurePlate(revokeTarget.id, revokePlateReason.trim());
      setRows((prev) =>
        prev.map((r) => (r.id === revokeTarget.id ? { ...r, status: "preasignado" } : r)),
      );
      setRevokeTarget(null);
      setRevokePlateReason("");
      show("Preasignación revocada.", "success");
    } catch {
      show("No se pudo revocar la preasignación.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmAdjuntarLt = async () => {
    if (!ltTarget || !ltFile) return;
    setActing(true);
    try {
      await adjuntarOtLicenciaTransito(ltTarget.id, ltFile, scope);
      setLtTarget(null);
      setLtFile(null);
      show("Licencia de Tránsito adjuntada.", "success");
    } catch {
      show("No se pudo adjuntar la Licencia de Tránsito.", "error");
    } finally {
      setActing(false);
    }
  };

  const closePreview = () => {
    setPreview((p) => {
      if (p.url) URL.revokeObjectURL(p.url);
      return { ...p, open: false, url: null, error: null, download: null };
    });
  };

  const handleConsolidado = async (row: OtClientProcedure) => {
    // Botón único (Feature #10701): abre el consolidado del expediente INLINE. Si el OT puede
    // generar, "asegura" el vigente — el backend regenera solo si la marca lo pide (nunca generado
    // o invalidado por un cambio de estado / LT) y reutiliza si ya está vigente. En modo QX
    // read-only no se puede generar: solo se muestra el consolidado existente.
    setConsolidadoActingId(row.id);
    setPreview((p) => {
      if (p.url) URL.revokeObjectURL(p.url);
      return {
        open: true,
        title: `Consolidado — ${row.referenceNumber}`,
        mimetype: "application/pdf",
        url: null,
        loading: true,
        error: null,
        download: null,
      };
    });
    try {
      let attId: string;
      let filename: string;
      let mimetype = "application/pdf";
      if (!isReadOnly) {
        const res = await generarOtConsolidadoMaestro(row.id, scope);
        attId = res.document.attachmentId;
        filename = res.document.filename;
        if (res.regenerado) show("Consolidado generado.", "success");
      } else {
        const docs = await fetchOtDocuments(row.id, scope);
        const consol =
          docs.data.find((a) => a.tipo === "consolidado_maestro") ??
          docs.data.find((a) => a.tipo === "consolidado");
        if (!consol) {
          setPreview((p) => ({
            ...p,
            loading: false,
            error: "El trámite aún no tiene consolidado generado.",
          }));
          return;
        }
        attId = consol.id;
        filename = consol.filename;
        mimetype = consol.mimetype || "application/pdf";
      }
      const { url } = await fetchOtAttachmentPreviewUrl(row.id, attId, scope);
      // El file-manager sirve el objeto como binary/octet-stream: re-empaquetamos como Blob con el
      // mimetype real para forzar el render inline (S3 permite CORS GET).
      const blob = await fetch(url).then((r) => {
        if (!r.ok) throw new Error(String(r.status));
        return r.blob();
      });
      const objectUrl = URL.createObjectURL(new Blob([blob], { type: mimetype }));
      setPreview((p) => ({
        ...p,
        loading: false,
        url: objectUrl,
        mimetype,
        download: { procId: row.id, attId, filename },
      }));
    } catch {
      setPreview((p) => ({
        ...p,
        loading: false,
        error: "No se pudo abrir el consolidado. Intenta de nuevo.",
      }));
    } finally {
      setConsolidadoActingId(null);
    }
  };

  const handlePreviewDownload = async () => {
    const dl = preview.download;
    if (!dl) return;
    try {
      await downloadFile(
        `/api/v1/admin/ot/client-procedures/${dl.procId}/documents/${dl.attId}/download`,
        {
          query: scope?.transitOfficeId ? { transitOfficeId: scope.transitOfficeId } : undefined,
          fallbackFilename: dl.filename,
        },
      );
    } catch {
      show("No se pudo descargar el consolidado.", "error");
    }
  };

  const confirmReject = async () => {
    if (!rejectTarget || !rejectReason.trim()) return;
    setActing(true);
    try {
      const updated = await rejectOtClientProcedure(rejectTarget.id, {
        reason: rejectReason.trim(),
      });
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setRejectTarget(null);
      setRejectReason("");
      show("Trámite rechazado.", "success");
    } catch {
      show("No se pudo rechazar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  return (
    <div className="space-y-4">
      {isReadOnly && (
        <div className="flex items-center gap-2">
          <span
            className="rounded-full px-2 py-0.5 text-[10px] font-semibold uppercase tracking-wide"
            style={{ background: "#FFF4EC", color: "#FF4E00" }}
          >
            Solo lectura
          </span>
          <span className="text-[11px] opacity-60">
            Modo QX activo — no se pueden aprobar ni rechazar trámites.
          </span>
        </div>
      )}
      {health?.hasDeliveredWithoutGrant && (
        <div
          role="alert"
          className="rounded-xl px-4 py-3 text-xs"
          style={{ background: "#FFF4EC", color: "#7A2E00", border: "1px solid #FFD9C2" }}
        >
          <span className="font-semibold">
            {health.deliveredWithoutGrant}{" "}
            {health.deliveredWithoutGrant === 1
              ? "trámite entregado sin grant vigente"
              : "trámites entregados sin grant vigente"}
          </span>{" "}
          no {health.deliveredWithoutGrant === 1 ? "aparece" : "aparecen"} en esta bandeja.
          Habilita el grant OT↔empresa correspondiente para que el organismo pueda recibirlos y
          aprobarlos.
        </div>
      )}
      <form
        className={OT_FILTER_FORM_CLS}
        onSubmit={(e) => {
          e.preventDefault();
          applyFilters();
        }}
        aria-label="Filtros de trámites de clientes"
      >
        <label className="text-xs font-semibold text-foreground">
          Estado
          <select
            aria-label="Filtrar por estado"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={statusFilter}
            onChange={(e) => setStatusFilter(e.target.value)}
          >
            <option value="entregado">Pendiente OT</option>
            <option value="aprobado">Aprobado OT</option>
            <option value="rechazado">Rechazado OT</option>
            <option value="">Todos</option>
          </select>
        </label>
        <label className="text-xs font-semibold text-foreground">
          Tipo de trámite
          <select
            aria-label="Filtrar por tipo de trámite"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={typeFilter}
            onChange={(e) => setTypeFilter(e.target.value)}
          >
            <option value="">Todos</option>
            {procedureTypes.map((pt) => (
              <option key={pt.id} value={pt.id}>
                {pt.name}
              </option>
            ))}
          </select>
        </label>
        <div className="flex items-end">
          <button
            type="submit"
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            Aplicar filtros
          </button>
        </div>
      </form>

      <UiStateBoundary
        status={status}
        emptyMessage="No hay trámites pendientes de tus clientes."
        errorMessage="Error al cargar trámites de clientes."
        onRetry={() => void load()}
        skeletonRows={5}
      >
        <ClientProceduresTable
          rows={rows}
          totalCount={totalCount}
          page={page}
          pageSize={PAGE_SIZE}
          onPageChange={setPage}
          onApprove={(row) => {
            setLtFile(null);
            setApproveTarget(row);
          }}
          onReject={setRejectTarget}
          onAssignPlate={!isReadOnly && !superAdmin ? (row) => { setPlateInput(""); setAssignTarget(row); } : undefined}
          onRevoke={!isReadOnly && !superAdmin ? (row) => { setRevokePlateReason(""); setRevokeTarget(row); } : undefined}
          showApprovalActions={!isReadOnly && !superAdmin}
          onConsolidado={handleConsolidado}
          onAdjuntarLt={
            !isReadOnly && !superAdmin
              ? (row) => {
                  setLtFile(null);
                  setLtTarget(row);
                }
              : undefined
          }
          consolidadoActingId={consolidadoActingId}
          onVerDocumentos={setDocumentosProcedure}
        />
      </UiStateBoundary>

      {approveTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Confirmar aprobación"
        >
          <div className="w-full max-w-md rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              ¿Aprobar este trámite?
            </h2>
            <p className="mt-2 text-sm opacity-80">{approveTarget.referenceNumber}</p>
            <label className="mt-4 block text-xs font-semibold text-foreground">
              Licencia de Tránsito (LT) — opcional
              <input
                type="file"
                accept="application/pdf,image/jpeg,image/png,image/webp"
                aria-label="Licencia de Tránsito (LT)"
                className={`mt-1 ${OT_INPUT_CLS}`}
                onChange={(e) => setLtFile(e.target.files?.[0] ?? null)}
              />
              <span className="mt-1 block text-[11px] font-normal opacity-60">
                Se adjunta al expediente del trámite y entra al consolidado al generarlo o regenerarlo.
              </span>
            </label>
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setApproveTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting}
                onClick={() => void confirmApprove()}
              >
                {acting ? "Procesando…" : "Confirmar"}
              </button>
            </div>
          </div>
        </div>
      )}

      {assignTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Asignar placa"
        >
          <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>Asignar placa al trámite</h2>
            <p className="mt-2 text-sm opacity-80">{assignTarget.referenceNumber}</p>
            <label className="mt-4 block text-xs font-semibold" style={{ color: "#162744" }}>
              Placa
              <input
                type="text"
                value={plateInput}
                onChange={(e) => setPlateInput(e.target.value)}
                placeholder="ABC123"
                aria-label="Placa"
                className={`mt-1 uppercase ${OT_INPUT_CLS}`}
              />
            </label>
            <div className="mt-5 flex gap-3">
              <button type="button" className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60" onClick={() => setAssignTarget(null)} disabled={acting}>Cancelar</button>
              <button type="button" className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60" style={{ background: "#557EFF" }} disabled={acting || !plateInput.trim()} onClick={() => void confirmAssignPlate()}>{acting ? "Procesando…" : "Asignar"}</button>
            </div>
          </div>
        </div>
      )}

      {revokeTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Revocar preasignación"
        >
          <div className="w-full max-w-md rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>Revocar preasignación</h2>
            <p className="mt-2 text-sm opacity-80">{revokeTarget.referenceNumber}</p>
            <textarea
              className={`mt-3 ${OT_INPUT_CLS}`}
              rows={3}
              value={revokePlateReason}
              onChange={(e) => setRevokePlateReason(e.target.value)}
              placeholder="Motivo de la revocación…"
              aria-label="Motivo de la revocación"
            />
            <div className="mt-5 flex gap-3">
              <button type="button" className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60" onClick={() => setRevokeTarget(null)} disabled={acting}>Cancelar</button>
              <button type="button" className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60" style={{ background: "#dc2626" }} disabled={acting || !revokePlateReason.trim()} onClick={() => void confirmRevokePlate()}>{acting ? "Procesando…" : "Revocar"}</button>
            </div>
          </div>
        </div>
      )}

      {rejectTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Rechazar trámite"
        >
          <div className="w-full max-w-md rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              Motivo del rechazo
            </h2>
            <textarea
              className={`mt-3 ${OT_INPUT_CLS}`}
              rows={3}
              value={rejectReason}
              onChange={(e) => setRejectReason(e.target.value)}
            />
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setRejectTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#FF4E00" }}
                disabled={acting || !rejectReason.trim()}
                onClick={() => void confirmReject()}
              >
                Confirmar rechazo
              </button>
            </div>
          </div>
        </div>
      )}

      {ltTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Adjuntar Licencia de Tránsito"
        >
          <div className="w-full max-w-md rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              Adjuntar Licencia de Tránsito (LT)
            </h2>
            <p className="mt-2 text-sm opacity-80">{ltTarget.referenceNumber}</p>
            <input
              type="file"
              accept="application/pdf,image/jpeg,image/png,image/webp"
              aria-label="Archivo de la Licencia de Tránsito"
              className={`mt-4 ${OT_INPUT_CLS}`}
              onChange={(e) => setLtFile(e.target.files?.[0] ?? null)}
            />
            <p className="mt-1 text-[11px] opacity-60">
              Reemplaza la LT previa si existe; regenera el consolidado para incluirla.
            </p>
            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => {
                  setLtTarget(null);
                  setLtFile(null);
                }}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting || !ltFile}
                onClick={() => void confirmAdjuntarLt()}
              >
                {acting ? "Adjuntando…" : "Adjuntar LT"}
              </button>
            </div>
          </div>
        </div>
      )}

      {/* HU #10705 — Modal de documentos del expediente */}
      {documentosProcedure && (
        <Modal
          open
          onClose={() => setDocumentosProcedure(null)}
          title={`Documentos — ${documentosProcedure.referenceNumber}`}
          icon={FolderOpen}
          size="xl"
          zClassName="z-[90]"
        >
          <OtDocumentosTab
            procedureId={documentosProcedure.id}
            referenceNumber={documentosProcedure.referenceNumber}
            scope={transitOfficeId ? { transitOfficeId } : undefined}
            readOnly={isReadOnly}
          />
        </Modal>
      )}

      {/* Previsualización inline del consolidado (botón "Ver consolidado" de la tabla) */}
      <DocumentPreviewModal
        open={preview.open}
        onClose={closePreview}
        title={preview.title}
        mimetype={preview.mimetype}
        url={preview.url}
        loading={preview.loading}
        error={preview.error}
        onDownload={preview.download ? () => void handlePreviewDownload() : undefined}
      />
    </div>
  );
}
