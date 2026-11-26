"use client";

import { Icono } from "@flit/ui/Icono";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { RondasIdentidadPanel } from "@/components/admin/plataforma/identidad/RondasIdentidadPanel";
import { ToastProvider } from "@/components/admin/Toast";

/**
 * SuperAdmin de FLIT — Plataforma → Rondas de identidad.
 * Configuración GLOBAL de rondas máximas de validación de identidad y de la alerta a operación.
 * Solo SuperAdmin (la ruta /admin/* ya lo exige y el menú no declara la entrada sin el rol).
 */
export default function AdminRondasIdentidadPage() {
  const router = useRouter();

  return (
    <ToastProvider>
    <div className="flex flex-col gap-4 px-4 pt-6 pb-10 md:px-6">
      <button
        type="button"
        onClick={() => router.push("/")}
        className="flex w-fit items-center gap-1.5 rounded-md text-xs font-semibold text-flit-brand-ink focus-visible:outline-none focus-visible:ring-2 focus-visible:ring-flit-brand"
      >
        <Icono nombre="izquierda" className="h-3.5 w-3.5" />
        Volver al inicio
      </button>

      <ModuleTitle
        title="Rondas de identidad"
        subtitle="Cuántas rondas de validación de identidad se permiten y a quién se avisa cuando se rechazan. Aplica a todas las compañías. Solo SuperAdmin."
      />

      <RondasIdentidadPanel />
    </div>
    </ToastProvider>
  );
}
