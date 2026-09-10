"use client";

import { useState } from "react";
import { ArrowLeft } from "lucide-react";
import { useRouter } from "next/navigation";
import { ModuleTitle } from "@/components/atom/modules/ModuleTitle";
import { CargueMasivo } from "@/components/admin/migracion/CargueMasivo";
import { ComoSeUsa } from "@/components/admin/migracion/ComoSeUsa";
import { MigrarUno } from "@/components/admin/migracion/MigrarUno";

/**
 * Consola de migración V1 → V2.
 *
 * **Esta ruta es intencionalmente invisible**: no aparece en el menú del dashboard ni enlazada
 * desde ninguna parte, y se entra escribiendo la URL. No es seguridad —el gate real es el
 * middleware, que reserva todo `/admin/*` a SuperAdmin— sino alcance: es una herramienta temporal
 * de una migración concreta, y un elemento de menú sugeriría que es parte del producto. Cuando V1
 * se apague, esto se borra sin haber tocado la navegación.
 *
 * El botón que de verdad protege no está aquí sino en el ambiente: `MigracionApi:Enabled` viene
 * apagado por defecto, así que en un ambiente que no esté en plena ola la consola carga y dice que
 * el migrador no está disponible.
 */
export default function AdminMigracionPage() {
  const router = useRouter();
  const [pestana, setPestana] = useState<"uno" | "masivo">("uno");

  return (
    <div className="flex min-h-screen flex-col gap-4 px-4 pb-10 pt-6 md:px-6">
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
        title="Migración V1 → V2"
        subtitle="Trae trámites del sistema anterior. Reintentar es seguro: un trámite que ya está migrado no se duplica."
      />

      <ComoSeUsa />

      <div className="flex flex-1 flex-col rounded-2xl border border-[#DFE5ED] bg-white/60 p-4 dark:border-white/10 dark:bg-[#0B0F14]/60">
        <div
          role="tablist"
          aria-label="Modo de migración"
          className="flex gap-1 border-b border-[#DFE5ED] dark:border-white/10"
        >
          <Pestana
            activa={pestana === "uno"}
            onClick={() => setPestana("uno")}
            etiqueta="Un trámite"
          />
          <Pestana
            activa={pestana === "masivo"}
            onClick={() => setPestana("masivo")}
            etiqueta="Carga masiva"
          />
        </div>

        <div className="mt-4">{pestana === "uno" ? <MigrarUno /> : <CargueMasivo />}</div>
      </div>
    </div>
  );
}

function Pestana({
  activa,
  onClick,
  etiqueta,
}: {
  activa: boolean;
  onClick: () => void;
  etiqueta: string;
}) {
  return (
    <button
      type="button"
      role="tab"
      aria-selected={activa}
      onClick={onClick}
      className={`-mb-px border-b-2 px-4 py-2 text-sm font-semibold transition-colors ${
        activa ? "border-[#557EFF] text-[#557EFF]" : "border-transparent opacity-60"
      }`}
    >
      {etiqueta}
    </button>
  );
}
