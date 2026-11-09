import { useState } from "react";
import { MoreVertical, Ban, RefreshCw, Eraser, Eye } from "lucide-react";

const ROWS = [
  { id: "TRASP-02_FLIT-8841", tipo: "Traspaso (Bilateral)", prop: "Carlos Mendoza Díaz", tenant: "Renting Colombia S.A.", fecha: "14/05/2026", ot: "ST de Sabaneta", estado: "Borrador" },
  { id: "TRASP-02_FLIT-9012", tipo: "Traspaso (Múltiple)", prop: "Inversiones Bogotá SAS", tenant: "Logística Express", fecha: "15/05/2026", ot: "ST de Medellín", estado: "Aprobado" },
  { id: "TRASP-02_FLIT-9134", tipo: "Leasing (Unilateral)", prop: "Banco Davivienda S.A.", tenant: "Flota Pacífico", fecha: "16/05/2026", ot: "ST de Cali", estado: "Enviado" },
  { id: "TRASP-02_FLIT-9201", tipo: "Traspaso (Bilateral)", prop: "María Restrepo G.", tenant: "Renting Colombia S.A.", fecha: "17/05/2026", ot: "ST de Bogotá", estado: "Preparado" },
];

const estadoColor = (e: string) => {
  if (e === "Aprobado") return { bg: "var(--badge-success-bg)", color: "var(--badge-success-fg)", border: "var(--badge-success-border)" };
  if (e === "Borrador") return { bg: "var(--badge-neutral-bg)", color: "var(--badge-neutral-fg)", border: "var(--badge-neutral-border)" };
  if (e === "Enviado") return { bg: "var(--badge-info-bg)", color: "var(--badge-info-fg)", border: "var(--badge-info-border)" };
  return { bg: "var(--badge-warning-bg)", color: "var(--badge-warning-fg)", border: "var(--badge-warning-border)" };
};

export function DashboardGrid({ onNewTramite }: { onNewTramite: () => void }) {
  const [hover, setHover] = useState<string | null>(null);
  return (
    <div className="space-y-6">
      {/* KPIs */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {[
          { l: "Trámites Activos", v: "248", d: "+12 hoy", c: "var(--color-flit-brand)" },
          { l: "Aprobados (mes)", v: "1,842", d: "+8.4%", c: "var(--color-flit-tech)" },
          { l: "Bloqueos RUNT", v: "37", d: "Atención", c: "var(--color-flit-alert)" },
          { l: "Tenants Conectados", v: "16", d: "Multi-tenant OK", c: "var(--color-flit-brand)" },
        ].map((k) => (
          <div key={k.l} className="glass rounded-2xl p-4">
            <p className="text-xs text-muted-foreground">{k.l}</p>
            <p className="text-2xl font-bold font-mono mt-1 text-foreground">{k.v}</p>
            <p className="text-[11px] mt-1" style={{ color: k.c }}>{k.d}</p>
          </div>
        ))}
      </div>

      {/* Header + CTA */}
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h2 className="text-xl font-bold text-foreground">Grilla Maestra de Trámites</h2>
          <p className="text-xs text-muted-foreground mt-1">Control de transacciones vehiculares · Permiso: <span className="font-mono">tramites.admin.maestro</span></p>
        </div>
        <button
          onClick={onNewTramite}
          className="rounded-2xl px-5 py-3 text-sm font-semibold text-white flex items-center gap-2"
          style={{ background: "var(--gradient-action)", boxShadow: "0 10px 30px -10px color-mix(in srgb, var(--color-flit-brand) 60%, transparent)" }}
        >
          Iniciar Nuevo Traspaso Vehicular Avanzado
        </button>
      </div>

      {/* Table — card rows on solid header */}
      <div className="rounded-2xl">
        <div className="overflow-x-auto">
          <div className="min-w-[920px]">
            {/* Header bar */}
            <div
              className="grid items-center text-[11px] uppercase tracking-wider font-semibold rounded-xl px-4 py-3"
              style={{ background: "var(--table-header-bg)", color: "var(--table-header-fg)", gridTemplateColumns: "1.4fr 1.2fr 1.3fr 1.2fr 0.9fr 1fr 0.9fr 0.8fr" }}
            >
              <div>ID Compuesto</div>
              <div>Trámite</div>
              <div>Propietario</div>
              <div>Tenant</div>
              <div>Fecha</div>
              <div>OT</div>
              <div>Estado</div>
              <div className="text-right">Acciones</div>
            </div>
            {/* Card rows */}
            <div className="space-y-2 mt-2">
              {ROWS.map((r) => {
                const c = estadoColor(r.estado);
                return (
                  <div
                    key={r.id}
                    onMouseEnter={() => setHover(r.id)}
                    onMouseLeave={() => setHover(null)}
                    className="grid items-center bg-card dark:bg-secondary rounded-xl px-4 py-3 text-sm shadow-[0_2px_8px_rgba(22,39,68,0.05)] hover:shadow-[0_8px_24px_color-mix(in_srgb,var(--color-flit-brand)_18%,transparent)] transition relative"
                    style={{ gridTemplateColumns: "1.4fr 1.2fr 1.3fr 1.2fr 0.9fr 1fr 0.9fr 0.8fr" }}
                  >
                    <div className="font-mono text-xs text-flit-primary dark:text-foreground truncate">{r.id}</div>
                    <div className="text-flit-primary dark:text-foreground truncate">{r.tipo}</div>
                    <div className="text-flit-primary dark:text-foreground truncate">{r.prop}</div>
                    <div className="text-flit-primary/80 dark:text-muted-foreground truncate">{r.tenant}</div>
                    <div className="font-mono text-xs text-flit-primary/70 dark:text-muted-foreground">{r.fecha}</div>
                    <div className="text-flit-primary/80 dark:text-muted-foreground truncate">{r.ot}</div>
                    <div>
                      <span className="text-[11px] font-semibold px-2.5 py-1 rounded-full border whitespace-nowrap" style={{ background: c.bg, color: c.color, borderColor: c.border }}>{r.estado}</span>
                    </div>
                    <div className="flex items-center justify-end gap-1">
                      <button className="p-1.5 rounded-lg hover:bg-secondary dark:hover:bg-white/10"><Eye className="h-4 w-4" style={{ color: "var(--color-flit-brand)" }} /></button>
                      <button className="p-1.5 rounded-lg hover:bg-secondary dark:hover:bg-white/10"><MoreVertical className="h-4 w-4 text-flit-primary/60 dark:text-muted-foreground" /></button>
                    </div>
                    {hover === r.id && (
                      <div className="absolute right-4 top-12 z-20 bg-card dark:bg-secondary border border-border dark:border-border rounded-xl p-1 w-56 max-w-[80vw] shadow-xl">
                        <button className="w-full flex items-center gap-2 px-3 py-2 text-xs rounded-lg hover:bg-secondary dark:hover:bg-white/10 text-flit-primary dark:text-foreground"><Ban className="h-3.5 w-3.5" style={{ color: "var(--color-flit-alert)" }} /> Solicitud de anulación</button>
                        <button className="w-full flex items-center gap-2 px-3 py-2 text-xs rounded-lg hover:bg-secondary dark:hover:bg-white/10 text-flit-primary dark:text-foreground"><RefreshCw className="h-3.5 w-3.5" style={{ color: "var(--color-flit-brand)" }} /> Cambio de estado manual</button>
                        <button className="w-full flex items-center gap-2 px-3 py-2 text-xs rounded-lg hover:bg-secondary dark:hover:bg-white/10 text-flit-primary dark:text-foreground"><Eraser className="h-3.5 w-3.5" style={{ color: "var(--color-flit-tech)" }} /> Limpiar consolidado</button>
                        <div className="px-3 py-1.5 text-[10px] font-mono text-flit-primary/50 dark:text-muted-foreground border-t border-border dark:border-border mt-1">tramites.admin.maestro ✓</div>
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        </div>
      </div>
    </div>
  );
}