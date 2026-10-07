"use client";

import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { MensajesMuertosPanel } from "@/components/admin/plataforma/mensajes-muertos/MensajesMuertosPanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin — Plataforma → Mensajes muertos (Epic #13316, HU #13358). Correos y webhooks de Notificaciones que agotaron
 * sus reintentos: reintentar o descartar sin entrar al broker. Solo SuperAdmin.
 */
export default function AdminMensajesMuertosPage() {
  const router = useRouter();

  return (
    <ToastProvider>
      <div className="flex min-h-screen flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
        <button
          type="button"
          onClick={() => router.push("/")}
          className="flex w-fit items-center gap-1.5 text-xs font-semibold"
          style={{ color: "#557EFF" }}
        >
          <ArrowLeft className="h-3.5 w-3.5" aria-hidden="true" />
          Volver al inicio
        </button>

        <ModuleTitle
          title="Mensajes muertos"
          subtitle="Correos y webhooks que no salieron tras sus reintentos. Reintentar los devuelve a su cola; descartar queda en la auditoría. Solo SuperAdmin."
        />

        <MensajesMuertosPanel />
      </div>
    </ToastProvider>
  );
}
