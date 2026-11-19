"use client";

import { Icono } from "@flit/ui/Icono";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { NotificacionesBankPanel } from "@/components/admin/plataforma/NotificacionesBankPanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin — Plataforma → Notificaciones (HU #11370, Feature #11349).
 * Banco de pruebas: canal, compañía, catálogo de plantillas FLIT (+ Kyverum) y
 * remitente resuelto. Solo SuperAdmin · lectura / envío de prueba.
 */
export default function AdminNotificacionesPage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/")}
          className="flex w-fit items-center gap-1.5 text-xs font-semibold"
          style={{ color: "var(--color-flit-brand)" }}
        >
          <Icono nombre="izquierda" className="h-3.5 w-3.5" />
          Volver al inicio
        </button>

        <ModuleTitle
          title="Notificaciones"
          subtitle="Banco de pruebas de plantillas de correo por canal y compañía. Solo SuperAdmin · lectura."
        />

        <div className="flex flex-1 flex-col rounded-2xl border border-border bg-card/60 p-4">
          <NotificacionesBankPanel />
        </div>
      </div>
    </ToastProvider>
  );
}
