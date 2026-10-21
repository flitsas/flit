"use client";

import { ClipboardList } from "lucide-react";
import { useSesion } from "@/components/DiagnosticoShell";

/**
 * Pantalla base de Diagnóstico (HU #13732): la tarjeta título con la compañía de la sesión y el estado vacío del
 * producto. El shell solo la muestra con la sesión cargada; el loader, el error y la 403 los resuelve él.
 */
export default function Inicio() {
  const { sesion } = useSesion();
  const empresa = sesion?.empresa.nombre;

  return (
    <>
      <section
        aria-labelledby="diagnostico-titulo"
        className="rounded-2xl border border-[var(--color-flit-gray)] bg-white px-6 py-5 shadow-sm dark:border-white/[0.08] dark:bg-white/[0.04]"
      >
        <h1 id="diagnostico-titulo" className="text-2xl font-semibold text-flit-primary dark:text-white">
          Diagnóstico vehicular
        </h1>
        {empresa && <p className="mt-1 text-sm font-medium text-[var(--nav-texto)] dark:text-slate-300">{empresa}</p>}
      </section>

      <section
        aria-labelledby="diagnostico-vacio"
        className="flex flex-col items-center gap-3 rounded-2xl border border-[var(--color-flit-gray)] bg-white px-6 py-12 text-center shadow-sm dark:border-white/[0.08] dark:bg-white/[0.04]"
      >
        <ClipboardList className="h-10 w-10 text-flit-brand" aria-hidden="true" strokeWidth={1.5} />
        <h2 id="diagnostico-vacio" className="text-lg font-semibold">
          Todavía no hay diagnósticos
        </h2>
        <p className="max-w-md text-sm text-[var(--nav-texto)] dark:text-slate-300">
          Aquí verás los diagnósticos vehiculares de tu compañía cuando empiecen a registrarse.
        </p>
      </section>
    </>
  );
}
