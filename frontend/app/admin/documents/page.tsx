"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Layers, Plus } from "lucide-react";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { DocumentTypeListTable } from "@/components/admin/documents/DocumentTypeListTable";
import { CreateDocumentTypeDialog } from "@/components/admin/documents/CreateDocumentTypeDialog";
import {
  DocumentInUseDialog,
  type DocumentInUseProcedureType,
} from "@/components/admin/documents/DocumentInUseDialog";
import {
  createDocumentType,
  deactivateDocumentType,
  fetchDocumentTypes,
  reactivateDocumentType,
  updateDocumentType,
} from "@/lib/api/admin-document-types";
import { ApiError } from "@/lib/api/types";
import type { DocumentType, DocumentTypePagedResult } from "@/lib/api/types-documents";

const PAGE_SIZE = 20;

// Consola documental — catálogo de tipos de documento (HU #10198, AC1/AC7) con alta,
// edición y baja lógica. Paginación server-side; 4 estados UI vía UiStateBoundary.
export default function AdminDocumentsPage() {
  return (
    <ToastProvider>
      <DocumentsCatalog />
    </ToastProvider>
  );
}

function DocumentsCatalog() {
  const router = useRouter();
  const { show } = useToast();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<DocumentTypePagedResult | null>(null);
  const [dialogOpen, setDialogOpen] = useState(false);
  const [editing, setEditing] = useState<DocumentType | null>(null);
  // Documento que no se pudo desactivar por estar en uso (abre el modal emergente 409).
  const [inUse, setInUse] = useState<{
    documentName: string;
    procedureTypes: DocumentInUseProcedureType[];
  } | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        // includeInactive: el catálogo lista activos e inactivos; los desactivados
        // siguen visibles (badge «Inactivo», acción Desactivar deshabilitada) en lugar
        // de desaparecer. La asociación a trámites sigue ofreciendo solo activos.
        const data = await fetchDocumentTypes(
          { page, pageSize: PAGE_SIZE, includeInactive: true },
          signal,
        );
        if (signal?.aborted) {
          return;
        }
        setResult(data);
        setStatus(data.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) {
          setStatus("error");
        }
      }
    },
    [page],
  );

  useEffect(() => {
    const controller = new AbortController();
    // Carga inicial al montar: el skeleton (setStatus loading) es intencional.
    // eslint-disable-next-line react-hooks/set-state-in-effect
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const openCreate = () => {
    setEditing(null);
    setDialogOpen(true);
  };

  const openEdit = (documentType: DocumentType) => {
    setEditing(documentType);
    setDialogOpen(true);
  };

  const handleSaved = (documentType: DocumentType, mode: "create" | "edit") => {
    setDialogOpen(false);
    setEditing(null);
    show(
      mode === "create"
        ? `Documento «${documentType.nombre}» creado.`
        : `Documento «${documentType.nombre}» actualizado.`,
      "success",
    );
    if (mode === "create") {
      setPage(1);
    }
    void load();
  };

  const handleDeactivate = async (documentType: DocumentType) => {
    try {
      await deactivateDocumentType(documentType.id);
      show(`Documento «${documentType.nombre}» desactivado.`, "success");
      void load();
    } catch (error) {
      if (error instanceof ApiError && error.status === 409) {
        // 409 accionable: el backend indica en qué trámites se usa. Se muestra en un modal
        // persistente (no un toast efímero) para que el usuario no lo pase por alto.
        const body = error.body as { procedureTypes?: DocumentInUseProcedureType[] } | null;
        const procedureTypes = (body?.procedureTypes ?? []).filter(
          (p): p is DocumentInUseProcedureType => Boolean(p?.codigo && p?.nombre),
        );
        setInUse({ documentName: documentType.nombre, procedureTypes });
      } else {
        show("No se pudo desactivar el documento.", "error");
      }
    }
  };

  const handleReactivate = async (documentType: DocumentType) => {
    try {
      await reactivateDocumentType(documentType.id);
      show(`Documento «${documentType.nombre}» reactivado.`, "success");
      void load();
    } catch {
      show("No se pudo reactivar el documento.", "error");
    }
  };

  return (
    <div className="flex min-h-screen flex-col gap-4 px-6 pt-6 pb-10">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" /> Volver al inicio
      </button>

      <div className="flex flex-wrap items-start justify-between gap-3">
        <ModuleTitle
          title="Gestión documental"
          subtitle="Administra el catálogo de documentos y la configuración documental por trámite."
        />
        <div className="flex items-center gap-2">
          <button
            type="button"
            onClick={() => router.push("/admin/documents/procedures")}
            className="inline-flex items-center gap-1.5 rounded-xl border px-4 py-2 text-xs font-semibold"
            style={{ borderColor: "#557EFF", color: "#557EFF" }}
          >
            <Layers className="h-4 w-4" /> Configurar por trámite
          </button>
          <button
            type="button"
            onClick={openCreate}
            className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white shadow-sm"
            style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
          >
            <Plus className="h-4 w-4" /> Crear documento
          </button>
        </div>
      </div>

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          emptyMessage="No hay tipos de documento en el catálogo."
          emptyCta={
            <button
              type="button"
              onClick={openCreate}
              className="inline-flex items-center gap-1.5 rounded-xl px-4 py-2 text-xs font-semibold text-white"
              style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
            >
              <Plus className="h-4 w-4" /> Crear documento
            </button>
          }
          errorMessage="No se pudo cargar el catálogo de documentos."
        >
          {result && (
            <DocumentTypeListTable
              items={result.data}
              totalCount={result.totalCount}
              page={result.page}
              pageSize={result.pageSize}
              onPageChange={setPage}
              onEdit={openEdit}
              onDeactivate={handleDeactivate}
              onReactivate={handleReactivate}
            />
          )}
        </UiStateBoundary>
      </div>

      <CreateDocumentTypeDialog
        open={dialogOpen}
        editing={editing}
        onClose={() => {
          setDialogOpen(false);
          setEditing(null);
        }}
        onSubmit={(body) =>
          editing ? updateDocumentType(editing.id, body) : createDocumentType(body)
        }
        onSaved={handleSaved}
      />

      {inUse && (
        <DocumentInUseDialog
          documentName={inUse.documentName}
          procedureTypes={inUse.procedureTypes}
          onClose={() => setInUse(null)}
        />
      )}
    </div>
  );
}
