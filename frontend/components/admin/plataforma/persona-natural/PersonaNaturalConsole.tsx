"use client";

// HU #14074 a #14076 (Épica #14021) — Consola de Plataforma > Persona Natural. Arriba, el estado de la configuración
// (qué falta para que el portal ofrezca un trámite, con un botón directo a cada paso); debajo, las pestañas dentro de
// una sola ruta (`/admin/plataforma/persona-natural`). Cada pestaña se monta la primera vez que se abre (no pide su API
// hasta entonces) y se conserva montada, así una tabla recuerda su búsqueda y su página al volver a ella.
import { useCallback, useState } from "react";
import { SectionTabs, type SectionTab } from "@/components/atom/SectionTabs";
import { ContenidoTab } from "./contenido/ContenidoTab";
import { OrganismosYTiposTab } from "./OrganismosYTiposTab";
import { ParametrosTab } from "./ParametrosTab";
import { PnEstadoConfiguracion, usePnResumen, type PasoConfiguracion } from "./PnEstadoConfiguracion";
import { TarifasTab } from "./TarifasTab";

type TabId = "organismos" | "tarifas" | "parametros" | "contenido";

const PASO_A_PESTANA: Record<PasoConfiguracion, { tab: TabId; ancla: string }> = {
  tipos: { tab: "organismos", ancla: "pn-tipos-titulo" },
  organismos: { tab: "organismos", ancla: "pn-organismos-titulo" },
  tarifas: { tab: "tarifas", ancla: "pn-tarifas-titulo" },
};

export function PersonaNaturalConsole() {
  const [active, setActive] = useState<TabId>("organismos");
  const [visited, setVisited] = useState<ReadonlySet<TabId>>(() => new Set<TabId>(["organismos"]));
  const { resumen, error, refrescar } = usePnResumen();

  const onChange = useCallback((id: TabId) => {
    setActive(id);
    setVisited((prev) => (prev.has(id) ? prev : new Set(prev).add(id)));
  }, []);

  const irAlPaso = useCallback(
    (paso: PasoConfiguracion) => {
      const { tab, ancla } = PASO_A_PESTANA[paso];
      onChange(tab);
      // El panel destino se monta o se muestra en el siguiente cuadro: ahí se lleva el foco al título de la sección.
      requestAnimationFrame(() => {
        const titulo = document.getElementById(ancla);
        if (!titulo) return;
        titulo.setAttribute("tabindex", "-1");
        titulo.scrollIntoView?.({ block: "start" });
        titulo.focus({ preventScroll: true });
      });
    },
    [onChange],
  );

  const pestanas: { id: TabId; label: string; title: string; render: () => React.ReactNode }[] = [
    {
      id: "organismos",
      label: "Organismos y trámites",
      title: "Dónde y qué trámites ofrece el portal",
      render: () => <OrganismosYTiposTab onIrATarifas={() => irAlPaso("tarifas")} onCambio={refrescar} />,
    },
    {
      id: "tarifas",
      label: "Tarifas",
      title: "Cuánto cuesta cada trámite",
      render: () => <TarifasTab onCambio={refrescar} />,
    },
    {
      id: "parametros",
      label: "Ajustes del portal",
      title: "Tiempos, límites y contacto de soporte",
      render: () => <ParametrosTab />,
    },
    {
      id: "contenido",
      label: "Contenido",
      title: "Preguntas, banners y enlaces que lee el ciudadano",
      render: () => <ContenidoTab />,
    },
  ];

  const tabs: SectionTab<TabId>[] = pestanas.map((t) => ({
    id: t.id,
    label: t.label,
    title: t.title,
    content: visited.has(t.id) ? t.render() : null,
  }));

  return (
    <div className="flex flex-col gap-6">
      <PnEstadoConfiguracion resumen={resumen} error={error} onReintentar={refrescar} onIr={irAlPaso} />
      <SectionTabs tabs={tabs} active={active} onChange={onChange} ariaLabel="Secciones de Persona Natural" />
    </div>
  );
}
