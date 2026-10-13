"use client";

import { useState } from "react";
import { Modal, StatusBadge } from "@flit/ui";
import { Encabezado, Requiere } from "@/components/Pantalla";
import { ListaPlantillas } from "@/components/plantillas/ListaPlantillas";
import { VistaPrevia } from "@/components/plantillas/VistaPrevia";
import { controlCls } from "@/components/ui/estilos";
import { apiFetch } from "@/lib/api";
import { TIPO_PLANTILLA } from "@/lib/etiquetas";
import { formatoFecha } from "@/lib/format/date";
import { P } from "@/lib/nav";
import type { PlantillaDetalle, PlantillaResumen, VersionPlantilla } from "@/lib/tipos";
import { useCarga } from "@/lib/useCarga";

const TONO_VERSION: Record<string, "success" | "info" | "neutral"> = { Publicada: "success", Borrador: "info", Retirada: "neutral" };

function Visor({ plantilla, onCerrar }: { plantilla: PlantillaResumen; onCerrar: () => void }) {
  const detalle = useCarga(`plantilla:${plantilla.id}`, (signal) => apiFetch<PlantillaDetalle>(`/plantillas/${plantilla.id}`, { signal }));
  const [versionId, setVersionId] = useState<string | null>(plantilla.publicada?.id ?? null);
  const version = useCarga(versionId ? `version:${versionId}` : null, (signal) => apiFetch<VersionPlantilla>(`/plantillas/versiones/${versionId}`, { signal }));

  return (
    <Modal open onClose={onCerrar} size="2xl" title={TIPO_PLANTILLA[plantilla.tipo] ?? plantilla.tipo} description={`${plantilla.conjunto === "Renting" ? "Renting" : "FLIT"}${plantilla.tipologia ? ` · ${plantilla.tipologia}` : ""}`}>
      <div className="grid grid-cols-1 gap-4 lg:grid-cols-[220px_1fr]">
        <aside className="flex flex-col gap-2">
          <h3 className="text-xs font-semibold">Versiones</h3>
          {(detalle.datos?.versiones ?? []).map((v) => (
            <button key={v.id} type="button" className={`${controlCls(v.id === versionId)} justify-between`} onClick={() => setVersionId(v.id)}>
              <span>v{v.numeroVersion}</span>
              <StatusBadge label={v.estado} tone={TONO_VERSION[v.estado] ?? "neutral"} />
            </button>
          ))}
          {version.datos && (
            <p className="text-[11px] opacity-70">
              {version.datos.notaCambio ?? "Sin nota"} {version.datos.publicadaEn ? `· ${formatoFecha(version.datos.publicadaEn)}` : ""}
            </p>
          )}
        </aside>
        <VistaPrevia contenido={version.datos ? { tipo: plantilla.tipo, conjunto: plantilla.conjunto, asunto: version.datos.asunto, cuerpoHtml: version.datos.cuerpoHtml } : null} />
      </div>
    </Modal>
  );
}

/** Visor de plantillas (HU #13486, §7.16): FLIT y el conjunto de la empresa, con su historial de versiones [Excel J3]. */
export default function Plantillas() {
  const [elegida, setElegida] = useState<PlantillaResumen | null>(null);
  return (
    <Requiere permiso={P.plantillasVer}>
      <section className="flex flex-col gap-4">
        <Encabezado titulo="Plantillas" descripcion="Los textos de los derechos de petición, formatos y correos que envía Comparendos, con datos de ejemplo." />
        <ListaPlantillas onElegir={setElegida} />
        {elegida && <Visor key={elegida.id} plantilla={elegida} onCerrar={() => setElegida(null)} />}
      </section>
    </Requiere>
  );
}
