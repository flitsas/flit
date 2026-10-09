"use client";

import { InlineAlert } from "@flit/ui";
import { useSesion } from "@/components/ComparendosShell";

// Inicio provisional mientras llega la bandeja (HU #13499): confirma la sesión y la empresa.
export default function Inicio() {
  const { sesion, cargando, error } = useSesion();
  if (cargando) return <p className="text-sm text-slate-500">Abriendo Comparendos…</p>;
  if (error) return <InlineAlert tone="error">{error}</InlineAlert>;
  return (
    <section className="flex flex-col gap-2">
      <h1 className="text-2xl font-semibold">Gestión de Comparendos</h1>
      <p className="text-sm text-slate-600 dark:text-slate-300">
        {sesion?.usuario.esSuperAdmin ? "Administración FLIT: elige una gestora en cada pantalla." : sesion?.empresa?.nombre}
      </p>
    </section>
  );
}
