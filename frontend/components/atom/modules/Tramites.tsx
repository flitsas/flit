import { useState } from "react";
import { GripVertical, Settings2, List } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";
import { ProcedureTypeList } from "@/components/superadmin/ProcedureTypeList";
import { ParametrizationWizard } from "@/components/superadmin/ParametrizationWizard";
import { OperacionView } from "@/components/operacion/OperacionView";
import { useProcedureTypes } from "@/hooks/useProcedureTypes";

export function Tramites() {
  const [view, setView] = useState<"operacion" | "parametrizacion">("operacion");

  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle title="Gestión Integral de Trámites" subtitle="Embudo de tus trámites vehiculares" />

      <div className="flex items-center gap-2 shrink-0">
        {[
          { id: "operacion" as const, label: "Operación", icon: List },
          { id: "parametrizacion" as const, label: "Parametrización", icon: Settings2 },
        ].map((v) => {
          const Icon = v.icon;
          const active = view === v.id;
          return (
            <button
              key={v.id}
              onClick={() => setView(v.id)}
              className="flex items-center gap-2 px-4 py-2 rounded-xl text-xs font-semibold border transition"
              style={{
                borderColor: active ? "#557EFF" : "#DFE5ED",
                background: active ? "rgba(85,126,255,0.10)" : "transparent",
                color: active ? "#557EFF" : undefined,
              }}
            >
              <Icon className="h-3.5 w-3.5" />
              {v.label}
            </button>
          );
        })}
      </div>

      {view === "parametrizacion" ? <ParametrizacionView /> : <OperacionView />}
    </div>
  );
}

function ParametrizacionView() {
  const [wizardOpen, setWizardOpen] = useState(false);
  const [editingId, setEditingId] = useState<string | null>(null);
  const [selectedId, setSelectedId] = useState<string | null>(null);
  const [publishSuccess, setPublishSuccess] = useState<string | null>(null);
  const [toolbarPublishError, setToolbarPublishError] = useState<string | null>(null);
  const [toolbarPublishing, setToolbarPublishing] = useState(false);
  const { items, status, error, reload, publish } = useProcedureTypes();

  const selectedDraft = items.find(
    (item) => item.id === selectedId && item.publicationStatus === "draft",
  );

  const handleNew = () => {
    setEditingId(null);
    setWizardOpen(true);
  };

  const handleEdit = (id: string) => {
    setEditingId(id);
    setWizardOpen(true);
  };

  const handleWizardExit = (saved?: boolean) => {
    setWizardOpen(false);
    setEditingId(null);
    if (saved) void reload();
  };

  const handleToolbarPublish = async () => {
    if (!selectedDraft) return;
    setToolbarPublishing(true);
    setPublishSuccess(null);
    setToolbarPublishError(null);
    try {
      await publish(selectedDraft.id);
      setPublishSuccess(`Versión "${selectedDraft.code}" publicada correctamente.`);
      setSelectedId(null);
    } catch (err) {
      setToolbarPublishError(err instanceof Error ? err.message : 'Error al publicar');
    } finally {
      setToolbarPublishing(false);
    }
  };

  const handleListPublish = async (id: string) => {
    const updated = await publish(id);
    setPublishSuccess(`Versión "${updated.code}" publicada correctamente.`);
    if (selectedId === id) setSelectedId(null);
    return updated;
  };

  if (wizardOpen) {
    return (
      <ParametrizationWizard
        editingId={editingId}
        onExit={handleWizardExit}
      />
    );
  }

  return (
    <div className="flex-1 min-h-0 flex flex-col gap-3 overflow-hidden">
      {/* Action bar — preservar layout del mockup */}
      <div className="flex flex-wrap items-center justify-between gap-2 shrink-0">
        <div className="flex flex-wrap gap-2">
          <button
            onClick={handleNew}
            className="px-4 py-2 rounded-xl text-xs font-semibold text-white"
            style={{ background: "#557EFF" }}
            aria-label="Crear nuevo flujo de parametrización"
          >
            Nuevo flujo
          </button>
          <button
            onClick={() => void handleToolbarPublish()}
            className="px-4 py-2 rounded-xl text-xs font-semibold border disabled:opacity-50 disabled:cursor-not-allowed"
            style={{
              borderColor: "#00DBD5",
              color: "#00DBD5",
              opacity: selectedDraft ? 1 : 0.5,
            }}
            disabled={!selectedDraft || toolbarPublishing}
            aria-label={
              selectedDraft
                ? `Publicar versión de ${selectedDraft.code}`
                : "Publicar versión (selecciona un borrador en la tabla)"
            }
            title={
              selectedDraft
                ? `Publicar ${selectedDraft.code}`
                : "Selecciona un borrador en la tabla para publicar"
            }
          >
            {toolbarPublishing ? "Publicando…" : "Publicar versión"}
          </button>
          <button
            className="px-4 py-2 rounded-xl text-xs font-semibold border relative"
            style={{ borderColor: "#557EFF", color: "#557EFF" }}
            aria-label="Nueva regla — próximamente disponible"
          >
            Nueva regla
            <span
              className="absolute -top-2 -right-1 px-1.5 py-0.5 rounded text-[8px] font-bold text-white"
              style={{ background: "#557EFF" }}
              aria-hidden="true"
            >
              Próx.
            </span>
          </button>
        </div>
        <select
          className="px-3 py-2 rounded-xl border text-xs bg-white dark:bg-[#0B0F14] outline-none"
          style={{ borderColor: "#DFE5ED" }}
          defaultValue="sabaneta"
          aria-label="Filtrar por organismo de tránsito"
        >
          <option value="sabaneta">OT · Sabaneta</option>
          <option value="medellin">OT · Medellín</option>
          <option value="envigado">OT · Envigado</option>
        </select>
      </div>

      {toolbarPublishError && (
        <div
          className="rounded-xl p-3 text-xs border shrink-0"
          style={{ borderColor: '#FF4E00', background: 'rgba(255,78,0,0.06)', color: '#FF4E00' }}
          role="alert"
          aria-live="polite"
        >
          {toolbarPublishError}
        </div>
      )}

      {/* Lista real conectada a API */}
      <ProcedureTypeList
        items={items}
        status={status}
        error={error}
        onNew={handleNew}
        onEdit={handleEdit}
        onReload={reload}
        onPublish={handleListPublish}
        selectedId={selectedId}
        onSelect={setSelectedId}
        publishSuccessMessage={publishSuccess}
      />

      {/* Cards visuales del mockup — stubs para scopes futuros */}
      <div className="grid grid-cols-1 xl:grid-cols-2 gap-3 shrink-0">
        {/* Constructor de reglas — scope #10120 */}
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-sm font-bold">Constructor de reglas</h2>
            <span
              className="px-2 py-0.5 rounded text-[9px] font-bold text-white"
              style={{ background: "#557EFF" }}
            >
              Próximamente
            </span>
          </div>
          <div className="space-y-2">
            {[
              ["Si el vehículo tiene prenda", "Solicitar documento adicional"],
              ["Si la validación OCR falla", "Enviar a revisión manual"],
              ["Si el actor es jurídico", "Habilitar representante"],
            ].map(([rule, action]) => (
              <div key={rule as string} className="rounded-xl p-3 border opacity-50" style={{ borderColor: "#DFE5ED" }}>
                <div className="flex items-center gap-2">
                  <span className="px-2 py-0.5 rounded text-[9px] font-bold text-white" style={{ background: "#FF4E00" }}>SI</span>
                  <p className="text-[11px] font-semibold">{rule}</p>
                </div>
                <div className="flex items-center gap-2 mt-2 pl-1">
                  <span className="px-2 py-0.5 rounded text-[9px] font-bold text-white" style={{ background: "#557EFF" }}>ENTONCES</span>
                  <p className="text-xs opacity-75">{action}</p>
                </div>
              </div>
            ))}
          </div>
          <button disabled className="w-full mt-3 py-2 rounded-xl text-xs font-semibold border border-dashed opacity-40 cursor-not-allowed" style={{ borderColor: "#557EFF", color: "#557EFF" }}>
            + Agregar condición
          </button>
        </section>

        {/* Orden documental — scope #10138 */}
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <div className="flex items-center justify-between mb-3">
            <h2 className="text-sm font-bold">Orden documental</h2>
            <span
              className="px-2 py-0.5 rounded text-[9px] font-bold text-white"
              style={{ background: "#557EFF" }}
            >
              Próximamente
            </span>
          </div>
          <div className="space-y-2 opacity-50">
            {[
              ["Licencia de tránsito", "Obligatorio"],
              ["SOAT vigente", "Obligatorio"],
              ["Paz y salvo", "Condicional"],
              ["Mandato autenticado", "Opcional"],
            ].map(([doc, level]) => (
              <div key={doc as string} className="flex items-center gap-3 rounded-xl p-3 border bg-[rgba(249,172,0,0.06)] dark:bg-white/5" style={{ borderColor: "#DFE5ED" }}>
                <GripVertical className="h-4 w-4 opacity-30 shrink-0" aria-hidden="true" />
                <span className="text-xs font-medium flex-1">{doc}</span>
                <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full" style={{ background: level === "Obligatorio" ? "rgba(249,172,0,0.2)" : level === "Condicional" ? "rgba(85,126,255,0.15)" : "rgba(0,219,213,0.15)", color: level === "Obligatorio" ? "#F9AC00" : level === "Condicional" ? "#557EFF" : "#00DBD5" }}>
                  {level}
                </span>
              </div>
            ))}
          </div>
        </section>
      </div>

      {/* Configuración por OT — stub MVP */}
      <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border shrink-0" style={{ borderColor: "#DFE5ED" }}>
        <div className="flex items-center justify-between mb-3">
          <h2 className="text-sm font-bold">Configuración por OT</h2>
          <span
            className="px-2 py-0.5 rounded text-[9px] font-bold text-white"
            style={{ background: "#557EFF" }}
          >
            Próximamente
          </span>
        </div>
        <div className="grid grid-cols-12 px-3 py-2 text-[10px] font-semibold uppercase rounded-t-lg mb-2" style={{ background: "#DFE5ED", color: "#162744" }}>
          <div className="col-span-5">Documento</div>
          <div className="col-span-3">Validación IA</div>
          <div className="col-span-2">Orden</div>
          <div className="col-span-2 text-right">Activo</div>
        </div>
        <div className="space-y-2 opacity-50">
          {[
            ["Cédula comprador", "OCR + biometría", 1],
            ["Formulario traspaso", "Plantilla PDF", 2],
            ["Certificado tradición", "Sin IA", 3],
          ].map(([doc, validation, order]) => (
            <div key={doc as string} className="grid grid-cols-12 items-center px-3 py-2.5 rounded-xl border text-xs" style={{ borderColor: "#DFE5ED" }}>
              <div className="col-span-5 font-medium">{doc}</div>
              <div className="col-span-3 opacity-70">{validation}</div>
              <div className="col-span-2 font-bold" style={{ color: "#557EFF" }}>{order}</div>
              <div className="col-span-2 flex justify-end">
                <span className="h-5 w-9 rounded-full relative" style={{ background: "#00DBD5" }} aria-hidden="true">
                  <span className="absolute top-0.5 h-4 w-4 rounded-full bg-white" style={{ left: "calc(100% - 18px)" }} />
                </span>
              </div>
            </div>
          ))}
        </div>
      </section>
    </div>
  );
}
