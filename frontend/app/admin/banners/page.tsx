"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Image as ImageIcon, ShieldAlert } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { PermissionGate } from "@/components/auth/PermissionGate";
import { BANNERS_MANAGE_PERMISSION } from "@/lib/auth/jwt";
import { BannerListTable } from "@/components/admin/banners/BannerListTable";
import { BannerFormPanel } from "@/components/admin/banners/BannerFormPanel";
import { BannerDeleteDialog } from "@/components/admin/banners/BannerDeleteDialog";
import { createBanner, fetchBanners, updateBanner, type Banner, type BannerPagedResult } from "@/lib/api/admin-banners";

const PAGE_SIZE = 20;

/**
 * Consola admin de banners promocionales (HU #12241, Feature #12236): listado administrable
 * (AC1), alta/edición con vista previa en vivo (AC2), eliminación con confirmación (AC3) y guía
 * de tamaño recomendado en el formulario (AC4). El módulo completo está gateado por el permiso
 * `banners.manage` — el borde (middleware, `evaluateAdminAccess`) ya redirige a /403 a quien no
 * lo tiene; `PermissionGate` es la defensa en profundidad del lado del componente.
 */
export default function AdminBannersPage() {
  return (
    <PermissionGate permission={BANNERS_MANAGE_PERMISSION} fallback={<NoAccess />}>
      <ToastProvider>
        <BannersList />
      </ToastProvider>
    </PermissionGate>
  );
}

function NoAccess() {
  return (
    <div className="flex min-h-screen flex-col items-center justify-center gap-3 px-4 text-center">
      <ShieldAlert className="h-8 w-8" style={{ color: "#FF4E00" }} aria-hidden="true" />
      <p className="text-sm font-medium" style={{ color: "#162744" }}>
        No tienes permiso para administrar banners promocionales.
      </p>
    </div>
  );
}

function BannersList() {
  const router = useRouter();
  const { show } = useToast();
  const [page, setPage] = useState(1);
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<BannerPagedResult | null>(null);
  const [formTarget, setFormTarget] = useState<Banner | null | "new">(null);
  const [deleteTarget, setDeleteTarget] = useState<Banner | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchBanners({ page, pageSize: PAGE_SIZE }, signal);
        if (signal?.aborted) return;
        setResult(data);
        setStatus(data.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [page],
  );

  useEffect(() => {
    const controller = new AbortController();
    // eslint-disable-next-line react-hooks/set-state-in-effect -- carga inicial: skeleton intencional
    void load(controller.signal);
    return () => controller.abort();
  }, [load]);

  const handleSaved = (saved: Banner, wasCreate: boolean) => {
    setFormTarget(null);
    show(`Banner «${saved.name}» ${wasCreate ? "creado" : "actualizado"}.`, "success");
    if (wasCreate) {
      // El nuevo banner puede caer en otra página según el orden del backend: recarga desde la 1.
      setPage(1);
      void load();
    } else {
      // Update optimista local: evita recargar todo el listado por una edición puntual.
      setResult((prev) =>
        prev ? { ...prev, data: prev.data.map((b) => (b.id === saved.id ? saved : b)) } : prev,
      );
    }
  };

  const handleDeleted = (id: string) => {
    setDeleteTarget(null);
    show("Banner eliminado.", "success");
    setResult((prev) =>
      prev
        ? { ...prev, data: prev.data.filter((b) => b.id !== id), totalCount: Math.max(0, prev.totalCount - 1) }
        : prev,
    );
    if (result && result.data.length === 1 && page > 1) {
      setPage(page - 1);
    } else {
      void load();
    }
  };

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 md:px-6 pt-6 pb-10">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 text-xs font-semibold"
        style={{ color: "#557EFF" }}
      >
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" /> Volver al inicio
      </button>

      <div className="flex flex-wrap items-start justify-between gap-3">
        <ModuleTitle
          title="Banners promocionales"
          subtitle="Configura y programa el contenido informativo del carrusel de banners, sin intervención técnica."
        />
        <CreateButton label="Nuevo banner" icon={ImageIcon} onClick={() => setFormTarget("new")} />
      </div>

      <div className="flex flex-1 flex-col rounded-2xl border bg-white/60 p-4 dark:bg-[#0B0F14]/60">
        <UiStateBoundary
          status={status}
          onRetry={() => void load()}
          emptyMessage="Aún no hay banners configurados. Crea el primero para que aparezca en el carrusel público."
          errorMessage="No se pudo cargar el listado de banners."
        >
          {result && (
            <BannerListTable
              items={result.data}
              totalCount={result.totalCount}
              page={result.page}
              pageSize={result.pageSize}
              onPageChange={setPage}
              onEdit={setFormTarget}
              onDelete={setDeleteTarget}
            />
          )}
        </UiStateBoundary>
      </div>

      {formTarget !== null && (
        <BannerFormPanel
          open
          editing={formTarget === "new" ? null : formTarget}
          onClose={() => setFormTarget(null)}
          onSubmit={(input) =>
            formTarget === "new" ? createBanner(input) : updateBanner((formTarget as Banner).id, input)
          }
          onSaved={(saved) => handleSaved(saved, formTarget === "new")}
        />
      )}

      {deleteTarget && (
        <BannerDeleteDialog
          banner={deleteTarget}
          onClose={() => setDeleteTarget(null)}
          onDeleted={handleDeleted}
        />
      )}
    </div>
  );
}
