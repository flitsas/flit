"use client";

import { useCallback, useEffect, useState } from "react";
import { useRouter } from "next/navigation";
import { ArrowLeft, Image as ImageIcon, ShieldAlert } from "lucide-react";
import { CreateButton } from "@/components/atom/CreateButton";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { CarLoaderModal } from "@/components/atom/CarLoader";
import { UiStateBoundary, type UiStatus } from "@/components/admin/UiStateBoundary";
import { ToastProvider, useToast } from "@/components/admin/Toast";
import { usePermissions } from "@/hooks/usePermissions";
import { BannerListTable } from "@/components/admin/banners/BannerListTable";
import { BannerFormPanel } from "@/components/admin/banners/BannerFormPanel";
import { BannerDeleteDialog } from "@/components/admin/banners/BannerDeleteDialog";
import { createBanner, fetchBanners, updateBanner, type Banner, type BannerPagedResult } from "@/lib/api/admin-banners";
import { usePaginacion } from "@/components/atom/usePaginacion";
import { ADMIN_BACK_LINK_CLS } from "@/components/admin/admin-ui-styles";

/**
 * Consola admin de banners promocionales (HU #12241, Feature #12236): listado administrable
 * (AC1), alta/edición con vista previa en vivo (AC2), eliminación con confirmación (AC3) y guía
 * de tamaño recomendado en el formulario (AC4). El módulo es exclusivo del Super Admin FLIT
 * (HU #13439, Épica #12750) — el borde (middleware, `evaluateAdminAccess`) ya redirige a /403 a
 * quien no lo es; aquí se repite la comprobación como defensa en profundidad del lado del componente.
 */
export default function AdminBannersPage() {
  const { isSuperAdmin } = usePermissions();

  if (!isSuperAdmin) return <NoAccess />;

  return (
    <ToastProvider>
      <BannersList />
    </ToastProvider>
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
  // Bug #13055 — tabla homologada con el modelo de trámites: filas por página elegibles.
  const { page, pageSize, setPage, setPageSize } = usePaginacion();
  const [status, setStatus] = useState<UiStatus>("loading");
  const [result, setResult] = useState<BannerPagedResult | null>(null);
  const [formTarget, setFormTarget] = useState<Banner | null | "new">(null);
  const [deleteTarget, setDeleteTarget] = useState<Banner | null>(null);

  const load = useCallback(
    async (signal?: AbortSignal) => {
      setStatus("loading");
      try {
        const data = await fetchBanners({ page, pageSize }, signal);
        if (signal?.aborted) return;
        setResult(data);
        setStatus(data.data.length === 0 ? "empty" : "ready");
      } catch {
        if (!signal?.aborted) setStatus("error");
      }
    },
    [page, pageSize],
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
      <button type="button" onClick={() => router.push("/")} className={ADMIN_BACK_LINK_CLS}>
        <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" /> Volver al inicio
      </button>

      <ModuleTitle
        title="Banners promocionales"
        subtitle="Configura y programa el contenido informativo del carrusel de banners, sin intervención técnica."
        action={<CreateButton label="Nuevo banner" icon={ImageIcon} onClick={() => setFormTarget("new")} />}
      />

      {/* Sin tarjeta blanca envolvente: mismo lenguaje visual que TramitesTable — la tabla es una
          pila de filas-tarjeta directamente sobre el fondo de la app, no un bloque encapsulado.
          Carga: el mismo loader del carrito que usa el módulo de trámites (`CarLoaderModal`), no
          el esqueleto genérico de `UiStateBoundary` — error/vacío sí siguen ese componente
          compartido, que ya es el patrón correcto para esos dos estados. */}
      {status === "loading" ? (
        <CarLoaderModal label="Cargando banners…" />
      ) : (
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
              onPageSizeChange={setPageSize}
              onEdit={setFormTarget}
              onDelete={setDeleteTarget}
            />
          )}
        </UiStateBoundary>
      )}

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
