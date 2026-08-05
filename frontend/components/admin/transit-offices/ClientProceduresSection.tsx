"use client";

import { useCallback, useEffect, useRef, useState } from "react";
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
import type {
  OtBandejaHealth,
  OtClientProcedure,
  OtProfile,
  RejectionReason,
} from "@/lib/api/types-ot";
import { fetchRejectionReasons } from "@/lib/api/ot-metrics";
import { fetchMandateSigners, type MandateSigner } from "@/lib/api/admin-mandate-signers";
import { ApiError } from "@/lib/api/types";
import { getToken } from "@/lib/api/client";
import { downloadFile } from "@/lib/api/download";
import { decodeJwtPayload, isSuperAdmin } from "@/lib/auth/jwt";
import { Modal } from "@/components/atom/Modal";
import { DocumentPreviewModal } from "@/components/shared/DocumentPreviewModal";
import { ChevronDown, ChevronUp, FolderOpen } from "lucide-react";
import { ClientProceduresTable } from "./ClientProceduresTable";
import { ClientProcedureDetailPanel } from "./ClientProcedureDetailPanel";
import {
  assignPlateToProcedure,
  listPlateDetails,
  revokeProcedurePlate,
  type PlateDetail,
} from "@/lib/api/admin-plate-ranges";
import { OtDocumentosTab } from "./OtDocumentosTab";
import { OT_FILTER_FORM_CLS, OT_INPUT_CLS } from "./ot-form-styles";
import { formatDocumentWithType } from "@/lib/display/document-number";

const PAGE_SIZE = 20;

/** Extrae el motivo de fallo al asignar placa (ProblemDetails.detail o fallback legible). */
export function readAssignPlateError(err: unknown): string {
  if (err instanceof ApiError) {
    const body = err.body as { detail?: unknown; title?: unknown } | null | undefined;
    if (typeof body?.detail === "string" && body.detail.trim()) return body.detail.trim();
    if (typeof body?.title === "string" && body.title.trim() && body.title !== "Conflict") {
      return body.title.trim();
    }
    // apiFetch (422 ProblemDetails) ya pone el detail en message.
    if (err.message && !/^Error \d+ al llamar /.test(err.message)) return err.message;
  }
  if (err instanceof Error && err.message && err.message !== "Validación fallida") {
    // Errores técnicos de red/fetch no ayudan al operador OT.
    if (!/^(network|failed to fetch|load failed|aborted?)$/i.test(err.message.trim())) {
      return err.message;
    }
  }
  return "No se pudo asignar la placa.";
}

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
  // Borradores del formulario; se aplican al listado solo con "Aplicar filtros".
  const [vinFilter, setVinFilter] = useState("");
  const [placaFilter, setPlacaFilter] = useState("");
  const [vendedorFilter, setVendedorFilter] = useState("");
  const [compradorFilter, setCompradorFilter] = useState("");
  const [gestorFilter, setGestorFilter] = useState("");
  const [appliedVin, setAppliedVin] = useState("");
  const [appliedPlaca, setAppliedPlaca] = useState("");
  const [appliedVendedor, setAppliedVendedor] = useState("");
  const [appliedComprador, setAppliedComprador] = useState("");
  const [appliedGestor, setAppliedGestor] = useState("");
  const [sortBy, setSortBy] = useState("createdAt");
  const [sortDir, setSortDir] = useState<"asc" | "desc">("desc");
  /** Panel de filtros colapsado por defecto para no saturar la bandeja. */
  const [filtersOpen, setFiltersOpen] = useState(false);
  const [procedureTypes, setProcedureTypes] = useState<ProcedureTypeSummary[]>([]);
  const [approveTarget, setApproveTarget] = useState<OtClientProcedure | null>(null);
  const [rejectTarget, setRejectTarget] = useState<OtClientProcedure | null>(null);
  // ADR-0036 §D9 (HU #10916) — cuando la aprobación devuelve 409 mandatario_requerido, se elige el
  // mandatario que firma el mandato y se reintenta la aprobación con él.
  const [mandatarioTarget, setMandatarioTarget] = useState<OtClientProcedure | null>(null);
  const [mandatarioOptions, setMandatarioOptions] = useState<MandateSigner[]>([]);
  const [mandatarioChoice, setMandatarioChoice] = useState("");
  // Feature #10587 — asignar placa (preasignado) / revocar preasignación.
  const [assignTarget, setAssignTarget] = useState<OtClientProcedure | null>(null);
  const [plateInput, setPlateInput] = useState("");
  // HU #10800 — placas disponibles del rango de la compañía (para el select) y modo de asignación.
  const [availablePlates, setAvailablePlates] = useState<PlateDetail[]>([]);
  const [assignMode, setAssignMode] = useState<"range" | "out">("range");
  const [revokeTarget, setRevokeTarget] = useState<OtClientProcedure | null>(null);
  const [revokePlateReason, setRevokePlateReason] = useState("");
  const [rejectReason, setRejectReason] = useState("");
  // Causales del catálogo para el modal de rechazo. Se cargan según la modalidad del trámite: las
  // causales no son intercambiables entre matrícula y traspaso.
  const [rejectReasonCatalog, setRejectReasonCatalog] = useState<RejectionReason[]>([]);
  const [rejectReasonIds, setRejectReasonIds] = useState<string[]>([]);
  const [rejectCatalogError, setRejectCatalogError] = useState<string | null>(null);
  /** Trámite cuya carga de causales es la vigente; descarta respuestas de aperturas anteriores. */
  const rejectCatalogRequestRef = useRef<string | null>(null);
  // Licencia de Tránsito opcional al aprobar; también adjuntable después (fila aprobada).
  const [ltFile, setLtFile] = useState<File | null>(null);
  const [ltTarget, setLtTarget] = useState<OtClientProcedure | null>(null);
  const [consolidadoActingId, setConsolidadoActingId] = useState<string | null>(null);
  const [acting, setActing] = useState(false);
  const [profile, setProfile] = useState<OtProfile | null>(null);
  // HU #10705 — panel de documentos del expediente
  const [documentosProcedure, setDocumentosProcedure] = useState<OtClientProcedure | null>(null);
  // Panel lateral derecho — detalle del trámite
  const [detailProcedure, setDetailProcedure] = useState<OtClientProcedure | null>(null);
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
  // (los endpoints approve/reject YA soportan el override de organismo del SuperAdmin vía
  // ?transitOfficeId=; en esta bandeja OT nativa el SuperAdmin no decide, solo supervisa).
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

  // Deep-link desde el drill-down de reportes OT (?placa=/?vin=/?status=): abrir la lista de un
  // bloque del panel debe aterrizar ya filtrado en el trámite, no en la bandeja completa. Se lee
  // window.location directo (en vez de useSearchParams) para no requerir un boundary de router en
  // este componente ni afectar los tests que lo montan fuera de una app real.
  useEffect(() => {
    if (typeof window === "undefined") return;
    const params = new URLSearchParams(window.location.search);
    const placaParam = params.get("placa")?.trim();
    const vinParam = params.get("vin")?.trim();
    const statusParam = params.get("status")?.trim();
    if (!placaParam && !vinParam && !statusParam) return;
    /* eslint-disable react-hooks/set-state-in-effect -- siembra desde la URL al montar: no hay otro momento para leerla */
    if (placaParam) {
      setPlacaFilter(placaParam);
      setAppliedPlaca(placaParam);
    }
    if (vinParam) {
      setVinFilter(vinParam);
      setAppliedVin(vinParam);
    }
    if (statusParam) setStatusFilter(statusParam);
    setFiltersOpen(true);
    setPage(1);
    /* eslint-enable react-hooks/set-state-in-effect */
    // Solo al montar: es una precarga desde la URL de entrada, no una sincronización continua.
    // eslint-disable-next-line react-hooks/exhaustive-deps
  }, []);

  const load = useCallback(
    async (signal?: AbortSignal, targetPage = page) => {
      setStatus("loading");
      try {
        const result = await fetchOtClientProcedures(
          {
            status: statusFilter || undefined,
            procedureTypeId: typeFilter || undefined,
            vin: appliedVin.trim() || undefined,
            placa: appliedPlaca.trim() || undefined,
            vendedor: appliedVendedor.trim() || undefined,
            comprador: appliedComprador.trim() || undefined,
            gestor: appliedGestor.trim() || undefined,
            sortBy: sortBy || undefined,
            sortDir,
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
    [statusFilter, typeFilter, appliedVin, appliedPlaca, appliedVendedor, appliedComprador, appliedGestor, sortBy, sortDir, page, transitOfficeId],
  );

  useEffect(() => {
    const c = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial vía API con AbortController
    void load(c.signal, page);
    return () => c.abort();
  }, [load, page]);

  const applyFilters = () => {
    setAppliedVin(vinFilter);
    setAppliedPlaca(placaFilter);
    setAppliedVendedor(vendedorFilter);
    setAppliedComprador(compradorFilter);
    setAppliedGestor(gestorFilter);
    setPage(1);
  };

  const hasAdvancedFilters =
    appliedVin.trim() !== "" ||
    appliedPlaca.trim() !== "" ||
    appliedVendedor.trim() !== "" ||
    appliedComprador.trim() !== "" ||
    appliedGestor.trim() !== "" ||
    typeFilter !== "" ||
    statusFilter !== "entregado";

  const clearFilters = () => {
    setStatusFilter("entregado");
    setTypeFilter("");
    setVinFilter("");
    setPlacaFilter("");
    setVendedorFilter("");
    setCompradorFilter("");
    setGestorFilter("");
    setAppliedVin("");
    setAppliedPlaca("");
    setAppliedVendedor("");
    setAppliedComprador("");
    setAppliedGestor("");
    setSortBy("createdAt");
    setSortDir("desc");
    setPage(1);
  };

  const handleSortChange = (nextSortBy: string, nextSortDir: "asc" | "desc") => {
    setSortBy(nextSortBy);
    setSortDir(nextSortDir);
    setPage(1);
  };

  // OT sobre el que se listan los mandatarios (SuperAdmin: prop de la ruta; ot_admin: su perfil).
  const otIdForSigners = transitOfficeId ?? profile?.transitOfficeId ?? null;

  /**
   * Aprueba el trámite (opcionalmente con el mandatario elegido) y, si se adjuntó, sube la LT.
   * ADR-0036 §D9 (HU #10916): un 409 `mandatario_requerido` abre el diálogo de selección de mandatario
   * (varios candidatos sin cotejo automático) para reintentar con el firmante elegido.
   */
  const runApprove = async (target: OtClientProcedure, mandateSignerId?: string) => {
    setActing(true);
    try {
      // Se aprueba PRIMERO y luego se adjunta la LT: el gate de la LT exige el trámite en
      // entregado/aprobado. En la ruta de placa (Feature #10587) el trámite llega a la aprobación
      // en 'asignado', así que adjuntar antes fallaba con estado_invalido; tras aprobar queda
      // 'aprobado' (válido para la LT). El consolidado se genera on-demand y toma la LT vigente.
      const updated = await approveOtClientProcedure(target.id, mandateSignerId);
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));

      if (ltFile) {
        try {
          await adjuntarOtLicenciaTransito(target.id, ltFile, scope);
        } catch {
          // La aprobación YA quedó firme; solo falló el adjunto. Se puede reintentar con la
          // acción dedicada de Licencia de Tránsito.
          setApproveTarget(null);
          setMandatarioTarget(null);
          setLtFile(null);
          show(
            "Trámite aprobado, pero no se pudo adjuntar la Licencia de Tránsito. Reintenta la carga.",
            "error",
          );
          return;
        }
      }

      setApproveTarget(null);
      setMandatarioTarget(null);
      setLtFile(null);
      show(ltFile ? "Trámite aprobado con Licencia de Tránsito adjunta." : "Trámite aprobado.", "success");
    } catch (err) {
      const errorCode =
        err instanceof ApiError && err.status === 409
          ? (err.body as { error?: string } | undefined)?.error
          : undefined;

      // ADR-0036 §D9 (HU #10911) — el mandatario resuelto no tiene identidad validada vigente.
      if (errorCode === "mandatario_identidad_requerida") {
        setApproveTarget(null);
        setMandatarioTarget(null);
        show(
          "El mandatario debe validar su identidad (vigente) antes de firmar el mandato. " +
            "La compañía se la envía desde la pestaña «Mandatarios» de su configuración.",
          "error",
        );
        return;
      }

      // ADR-0036 §D9 — hay varios mandatarios y ninguno cotejó: pedir que el OT elija uno.
      const needsMandatario = errorCode === "mandatario_requerido";
      const otId = target.transitOfficeId ?? otIdForSigners;
      if (needsMandatario && otId) {
        try {
          const signers = await fetchMandateSigners(otId);
          const options = signers.filter(
            (s) => s.isActive && s.companyTenantIds.includes(target.clientTenantId),
          );
          setMandatarioOptions(options);
          setMandatarioChoice(options[0]?.id ?? "");
          setApproveTarget(null);
          setMandatarioTarget(target);
          return;
        } catch {
          // cae al mensaje genérico
        }
      }
      show("No se pudo aprobar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  const confirmApprove = () => {
    if (approveTarget) void runApprove(approveTarget);
  };

  const confirmMandatario = () => {
    if (mandatarioTarget && mandatarioChoice) void runApprove(mandatarioTarget, mandatarioChoice);
  };

  // HU #10800 — abre el modal de asignar placa y carga las placas disponibles del rango de la compañía;
  // si no hay, arranca en modo "fuera de rango".
  const openAssignPlate = (row: OtClientProcedure) => {
    setPlateInput("");
    setAssignMode("range");
    setAvailablePlates([]);
    setAssignTarget(row);
    listPlateDetails(row.clientTenantId, { state: "disponible", scope: { transitOfficeId } })
      .then((plates) => {
        setAvailablePlates(plates);
        setAssignMode(plates.length > 0 ? "range" : "out");
      })
      .catch(() => setAssignMode("out"));
  };

  const confirmAssignPlate = async () => {
    if (!assignTarget || !plateInput.trim()) return;
    setActing(true);
    try {
      // HU #10800 — del rango (outOfRange=false) o fuera de rango (outOfRange=true).
      await assignPlateToProcedure(assignTarget.id, plateInput.trim().toUpperCase(), assignMode === "out");
      // HU #10785 — el status global permanece 'entregado'; avanza el sub-estado interno de placa.
      setRows((prev) =>
        prev.map((r) => (r.id === assignTarget.id ? { ...r, plateFlowStatus: "asignado" } : r)),
      );
      setAssignTarget(null);
      setPlateInput("");
      show("Placa asignada al trámite.", "success");
    } catch (err) {
      // El backend explica la causa en el `detail` del ProblemDetails (placa ya asignada, fuera de
      // los rangos, trámite en otro estado…). Antes se descartaba y el operador solo veía un toast
      // genérico sin saber por qué no avanzaba el formulario.
      show(readAssignPlateError(err), "error");
      // El modal queda abierto a propósito: la corrección es escribir otra placa.
    } finally {
      setActing(false);
    }
  };

  const confirmRevokePlate = async () => {
    if (!revokeTarget || !revokePlateReason.trim()) return;
    setActing(true);
    try {
      await revokeProcedurePlate(revokeTarget.id, revokePlateReason.trim());
      // HU #10785 — el status global permanece 'entregado'; el sub-estado vuelve a 'preasignado'.
      setRows((prev) =>
        prev.map((r) => (r.id === revokeTarget.id ? { ...r, plateFlowStatus: "preasignado" } : r)),
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

  // Abre el modal y trae las causales activas de la modalidad del trámite. Si el catálogo falla, el
  // rechazo NO se bloquea: la observación en texto libre basta para radicar la decisión, y dejar al
  // revisor sin poder rechazar por un catálogo caído sería peor que perder el dato del reporte.
  const openReject = async (procedure: OtClientProcedure) => {
    setRejectTarget(procedure);
    setRejectReason("");
    setRejectReasonIds([]);
    setRejectCatalogError(null);
    setRejectReasonCatalog([]);
    // El modal pudo cerrarse o reabrirse con otro trámite mientras esperábamos el catálogo: se
    // marca cuál es la carga vigente para descartar la respuesta de una anterior, que pintaría
    // las causales del trámite equivocado.
    rejectCatalogRequestRef.current = procedure.id;
    try {
      const catalog = await fetchRejectionReasons({ modalidad: procedure.modalidadEntrada });
      if (rejectCatalogRequestRef.current !== procedure.id) return;
      setRejectReasonCatalog(catalog);
    } catch {
      if (rejectCatalogRequestRef.current !== procedure.id) return;
      setRejectCatalogError(
        "No se pudieron cargar las causales. Puedes rechazar describiendo el motivo.",
      );
    }
  };

  const toggleRejectReason = (id: string) => {
    setRejectReasonIds((prev) =>
      prev.includes(id) ? prev.filter((x) => x !== id) : [...prev, id],
    );
  };

  const confirmReject = async () => {
    if (!rejectTarget || !rejectReason.trim()) return;
    setActing(true);
    try {
      const updated = await rejectOtClientProcedure(rejectTarget.id, {
        reason: rejectReason.trim(),
        rejectionReasonIds: rejectReasonIds.length > 0 ? rejectReasonIds : undefined,
      });
      setRows((prev) => prev.map((r) => (r.id === updated.id ? updated : r)));
      setRejectTarget(null);
      setRejectReason("");
      setRejectReasonIds([]);
      show("Trámite rechazado.", "success");
    } catch {
      show("No se pudo rechazar el trámite.", "error");
    } finally {
      setActing(false);
    }
  };

  // HU #10805 — dígito de preferencia del trámite en asignación (solo guía). Las placas del rango que
  // terminan en ese dígito se ordenan primero y se marcan; el OT puede elegir esa u otra cualquiera.
  const preferredDigit = assignTarget?.platePreferredLastDigit?.trim() ?? "";
  const orderedPlates = preferredDigit
    ? [...availablePlates].sort(
        (a, b) =>
          Number(b.plate.endsWith(preferredDigit)) - Number(a.plate.endsWith(preferredDigit)),
      )
    : availablePlates;

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
            Este OT opera en Quipux — no se pueden aprobar ni rechazar trámites desde FLIT.
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
      <div className="rounded-2xl border bg-white dark:bg-[#0B0F14]">
        <div className="flex flex-wrap items-center gap-2 px-4 py-2.5">
          <button
            type="button"
            onClick={() => setFiltersOpen((o) => !o)}
            aria-expanded={filtersOpen}
            aria-controls="ot-filtros-panel"
            className="inline-flex items-center gap-1.5 rounded-xl px-3 py-1.5 text-xs font-semibold text-foreground transition hover:bg-[#557EFF]/10"
          >
            {filtersOpen ? (
              <ChevronUp className="h-3.5 w-3.5" aria-hidden="true" />
            ) : (
              <ChevronDown className="h-3.5 w-3.5" aria-hidden="true" />
            )}
            Filtros
            {hasAdvancedFilters ? (
              <span className="ml-0.5 rounded-full bg-[#557EFF]/15 px-1.5 py-0.5 text-[10px] font-bold text-[#557EFF]">
                activos
              </span>
            ) : null}
          </button>
          <button
            type="button"
            onClick={clearFilters}
            disabled={!hasAdvancedFilters && sortBy === "createdAt" && sortDir === "desc"}
            className="rounded-xl border px-3 py-1.5 text-xs font-semibold disabled:cursor-not-allowed disabled:opacity-40"
            style={{ borderColor: "#557EFF", color: "#557EFF" }}
            aria-label="Limpiar filtros de trámites OT"
          >
            Limpiar filtros
          </button>
        </div>
        {filtersOpen ? (
      <form
        id="ot-filtros-panel"
        className={`${OT_FILTER_FORM_CLS} border-0 border-t rounded-none rounded-b-2xl`}
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
        <label className="text-xs font-semibold text-foreground">
          VIN
          <input
            type="search"
            aria-label="Filtrar por VIN"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={vinFilter}
            onChange={(e) => setVinFilter(e.target.value)}
            placeholder="Buscar VIN"
          />
        </label>
        <label className="text-xs font-semibold text-foreground">
          Placa
          <input
            type="search"
            aria-label="Filtrar por placa"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={placaFilter}
            onChange={(e) => setPlacaFilter(e.target.value)}
            placeholder="Buscar placa"
          />
        </label>
        <label className="text-xs font-semibold text-foreground">
          Propietario / vendedor
          <input
            type="search"
            aria-label="Filtrar por propietario o vendedor"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={vendedorFilter}
            onChange={(e) => setVendedorFilter(e.target.value)}
            placeholder="Buscar propietario"
          />
        </label>
        <label className="text-xs font-semibold text-foreground">
          Comprador
          <input
            type="search"
            aria-label="Filtrar por comprador"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={compradorFilter}
            onChange={(e) => setCompradorFilter(e.target.value)}
            placeholder="Buscar comprador"
          />
        </label>
        <label className="text-xs font-semibold text-foreground">
          Gestor
          <input
            type="search"
            aria-label="Filtrar por gestor"
            className={`mt-1 ${OT_INPUT_CLS}`}
            value={gestorFilter}
            onChange={(e) => setGestorFilter(e.target.value)}
            placeholder="Buscar gestor"
          />
        </label>
        <div className="flex items-end gap-2">
          <button
            type="submit"
            className="rounded-xl px-4 py-2 text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
          >
            Aplicar filtros
          </button>
        </div>
      </form>
        ) : null}
      </div>

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
          sortBy={sortBy}
          sortDir={sortDir}
          onSortChange={handleSortChange}
          onApprove={(row) => {
            setLtFile(null);
            setApproveTarget(row);
          }}
          onReject={(p) => void openReject(p)}
          onAssignPlate={!isReadOnly && !superAdmin ? openAssignPlate : undefined}
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
          onVerDetalle={setDetailProcedure}
        />
      </UiStateBoundary>

      {approveTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Confirmar aprobación"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
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

      {mandatarioTarget && (
        <div
          className="fixed inset-0 z-[90] flex items-center justify-center bg-slate-900/40 px-4 backdrop-blur-sm"
          role="dialog"
          aria-modal="true"
          aria-label="Elegir mandatario del mandato"
        >
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">Elige el mandatario que firma</h2>
            <p className="mt-2 text-sm opacity-80">
              Este trámite requiere contrato de mandato y la compañía tiene varios mandatarios. Elige quién
              firma para aprobar.
            </p>
            <p className="mt-1 text-xs opacity-60">{mandatarioTarget.referenceNumber}</p>

            {mandatarioOptions.length === 0 ? (
              <p className="mt-4 rounded-xl border p-3 text-center text-xs opacity-70">
                No hay mandatarios activos para esta compañía en el organismo. Ahora los registra la
                propia compañía, desde la pestaña «Mandatarios» de su configuración, marcando en qué
                organismos aplican.
              </p>
            ) : (
              <fieldset className="mt-4 space-y-2" data-testid="mandatario-options">
                <legend className="sr-only">Mandatarios disponibles</legend>
                {mandatarioOptions.map((s) => (
                  <label
                    key={s.id}
                    className="flex cursor-pointer items-center gap-3 rounded-xl border px-3 py-2 text-sm"
                  >
                    <input
                      type="radio"
                      name="mandatario"
                      value={s.id}
                      checked={mandatarioChoice === s.id}
                      onChange={() => setMandatarioChoice(s.id)}
                      className="h-4 w-4 accent-[#557EFF]"
                    />
                    <span className="flex-1">
                      <span className="font-semibold">{s.fullName}</span>
                      <span className="ml-2 font-mono text-xs opacity-60">
                        {formatDocumentWithType(s.documentType, s.documentNumber)}
                      </span>
                    </span>
                  </label>
                ))}
              </fieldset>
            )}

            <div className="mt-5 flex gap-3">
              <button
                type="button"
                className="flex-1 rounded-xl border py-2.5 text-sm font-medium disabled:opacity-60"
                onClick={() => setMandatarioTarget(null)}
                disabled={acting}
              >
                Cancelar
              </button>
              <button
                type="button"
                className="flex-1 rounded-xl py-2.5 text-sm font-semibold text-white disabled:opacity-60"
                style={{ background: "#557EFF" }}
                disabled={acting || !mandatarioChoice}
                onClick={() => void confirmMandatario()}
              >
                {acting ? "Procesando…" : "Aprobar con este mandatario"}
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
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
            <h2 className="text-lg font-semibold" style={{ color: "#162744" }}>Asignar placa al trámite</h2>
            <p className="mt-2 text-sm opacity-80">{assignTarget.referenceNumber}</p>
            {/* HU #10805 — dígito de preferencia del gestor: SOLO guía. El OT puede asignar una placa
                que termine en ese dígito o cualquier otra. */}
            {preferredDigit && (
              <div
                className="mt-3 rounded-lg px-3 py-2 text-xs"
                style={{ background: "#EEF3FF", color: "#1E3A8A", border: "1px solid #C7D7FE" }}
              >
                Dígito de preferencia: <b>termina en {preferredDigit}</b> — solo guía. Las placas ★ del
                rango terminan en ese dígito; puedes asignar esa u otra cualquiera.
              </div>
            )}
            {/* HU #10800 — elegir del rango (select) o registrar una placa fuera de rango (input). */}
            <div className="mt-4 flex gap-2 text-xs font-semibold">
              <button
                type="button"
                disabled={availablePlates.length === 0}
                onClick={() => { setAssignMode("range"); setPlateInput(""); }}
                className={`rounded-lg border px-3 py-1.5 disabled:opacity-40 ${assignMode === "range" ? "text-white" : ""}`}
                style={assignMode === "range" ? { background: "#557EFF", borderColor: "#557EFF" } : undefined}
              >
                Del rango{availablePlates.length > 0 ? ` (${availablePlates.length})` : ""}
              </button>
              <button
                type="button"
                onClick={() => { setAssignMode("out"); setPlateInput(""); }}
                className={`rounded-lg border px-3 py-1.5 ${assignMode === "out" ? "text-white" : ""}`}
                style={assignMode === "out" ? { background: "#557EFF", borderColor: "#557EFF" } : undefined}
              >
                Fuera de rango
              </button>
            </div>
            {assignMode === "range" ? (
              <label className="mt-4 block text-xs font-semibold" style={{ color: "#162744" }}>
                Placa del rango
                <select
                  value={plateInput}
                  onChange={(e) => setPlateInput(e.target.value)}
                  aria-label="Placa del rango"
                  className={`mt-1 ${OT_INPUT_CLS}`}
                >
                  <option value="">
                    {availablePlates.length === 0 ? "No hay placas disponibles" : "Selecciona una placa"}
                  </option>
                  {orderedPlates.map((p) => (
                    <option key={p.id} value={p.plate}>
                      {p.plate}
                      {preferredDigit && p.plate.endsWith(preferredDigit) ? " ★" : ""}
                    </option>
                  ))}
                </select>
              </label>
            ) : (
              <label className="mt-4 block text-xs font-semibold" style={{ color: "#162744" }}>
                Placa (fuera de rango)
                <input
                  type="text"
                  value={plateInput}
                  onChange={(e) => setPlateInput(e.target.value)}
                  placeholder="ABC123"
                  aria-label="Placa fuera de rango"
                  className={`mt-1 uppercase ${OT_INPUT_CLS}`}
                />
                <span className="mt-1 block text-[11px] font-normal opacity-70">
                  Formato ABC123. Se validará que no esté registrada y quedará en el inventario de la compañía.
                </span>
              </label>
            )}
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
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-white p-6 shadow-2xl dark:bg-[#0B0F14]" style={{ border: "1px solid #DFE5ED" }}>
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
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
            <h2 className="text-lg font-semibold text-foreground">
              Motivo del rechazo
            </h2>

            {/* Causales del catálogo: varias son válidas y esperadas. Un expediente puede llegar
                con improntas borrosas, sin impronta y sin pago de impuestos a la vez, y el gestor
                necesita saberlo todo para subsanar. */}
            {rejectReasonCatalog.length > 0 && (
              <fieldset className="mt-4" data-testid="reject-reason-catalog">
                <legend className="text-xs font-semibold text-foreground">
                  ¿Qué falló? Marca todo lo que aplique
                </legend>
                <div className="mt-2 flex flex-col gap-1.5">
                  {rejectReasonCatalog.map((reason) => (
                    <label
                      key={reason.id}
                      className="flex items-start gap-2 text-xs text-foreground"
                    >
                      <input
                        type="checkbox"
                        className="mt-0.5"
                        checked={rejectReasonIds.includes(reason.id)}
                        onChange={() => toggleRejectReason(reason.id)}
                      />
                      <span>{reason.description}</span>
                    </label>
                  ))}
                </div>
              </fieldset>
            )}
            {rejectCatalogError && (
              <p className="mt-3 text-[11px] text-amber-700 dark:text-amber-400">
                {rejectCatalogError}
              </p>
            )}

            <label className="mt-4 block text-xs font-semibold text-foreground">
              Observación para quien va a subsanar
            </label>
            {/* El texto libre NO lo sustituyen las causales: la causal dice QUÉ falló y esto dice
                CÓMO corregirlo — qué documento exactamente, qué dato no cuadra. */}
            <textarea
              className={`mt-2 ${OT_INPUT_CLS}`}
              rows={3}
              placeholder="Indica qué debe corregirse y con qué detalle"
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
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto rounded-2xl bg-card p-6 shadow-2xl border">
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

      {/* Panel lateral — detalle del trámite */}
      <ClientProcedureDetailPanel
        open={!!detailProcedure}
        procedure={detailProcedure}
        onClose={() => setDetailProcedure(null)}
        scope={scope}
        onVerDocumentos={(row) => {
          setDocumentosProcedure(row);
        }}
        onVerConsolidado={handleConsolidado}
        consolidadoActing={
          !!detailProcedure && consolidadoActingId === detailProcedure.id
        }
      />

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
