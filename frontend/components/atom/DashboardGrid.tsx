import { useState } from "react";
import { Plus, MoreVertical, Ban, RefreshCw, Eraser, Eye } from "lucide-react";

const ROWS = [
  { id: "TRASP-02_FLIT-8841", tipo: "Traspaso (Bilateral)", prop: "Carlos Mendoza Díaz", tenant: "Renting Colombia S.A.", fecha: "14/05/2026", ot: "ST de Sabaneta", estado: "Borrador" },
  { id: "TRASP-02_FLIT-9012", tipo: "Traspaso (Múltiple)", prop: "Inversiones Bogotá SAS", tenant: "Logística Express", fecha: "15/05/2026", ot: "ST de Medellín", estado: "Aprobado" },
  { id: "TRASP-02_FLIT-9134", tipo: "Leasing (Unilateral)", prop: "Banco Davivienda S.A.", tenant: "Flota Pacífico", fecha: "16/05/2026", ot: "ST de Cali", estado: "Enviado" },
  { id: "TRASP-02_FLIT-9201", tipo: "Traspaso (Bilateral)", prop: "María Restrepo G.", tenant: "Renting Colombia S.A.", fecha: "17/05/2026", ot: "ST de Bogotá", estado: "Preparado" },
];

const estadoColor = (e: string) => {
  if (e === "Aprobado") return { bg: "rgba(0,219,213,0.12)", color: "#00a39f", border: "rgba(0,219,213,0.3)" };
  if (e === "Borrador") return { bg: "rgba(100,116,139,0.12)", color: "#475569", border: "rgba(100,116,139,0.3)" };
  if (e === "Enviado") return { bg: "rgba(85,126,255,0.12)", color: "#557eff", border: "rgba(85,126,255,0.3)" };
  return { bg: "rgba(245,158,11,0.12)", color: "#b45309", border: "rgba(245,158,11,0.3)" };
};

export function DashboardGrid({ onNewTramite }: { onNewTramite: () => void }) {
  const [hover, setHover] = useState<string | null>(null);
  return (
    <div className="space-y-6">
      {/* KPIs */}
      <div className="grid grid-cols-2 lg:grid-cols-4 gap-4">
        {[
          { l: "Trámites Activos", v: "248", d: "+12 hoy", c: "#557eff" },
          { l: "Aprobados (mes)", v: "1,842", d: "+8.4%", c: "#00dbd5" },
          { l: "Bloqueos RUNT", v: "37", d: "Atención", c: "#ff4e00" },
          { l: "Tenants Conectados", v: "16", d: "Multi-tenant OK", c: "#557eff" },
        ].map((k) => (
          <div key={k.l} className="glass rounded-2xl p-4">
            <p className="text-xs text-slate-500">{k.l}</p>
            <p className="text-2xl font-bold font-mono mt-1 text-slate-800">{k.v}</p>
            <p className="text-[11px] mt-1" style={{ color: k.c }}>{k.d}</p>
          </div>
        ))}
      </div>

      {/* Header + CTA */}
      <div className="flex items-center justify-between gap-3 flex-wrap">
        <div>
          <h2 className="text-xl font-bold text-slate-800">Grilla Maestra de Trámites</h2>
          <p className="text-xs text-slate-500 mt-1">Control de transacciones vehiculares · Permiso: <span className="font-mono">tramites.admin.maestro</span></p>
        </div>
        <button
          onClick={onNewTramite}
          className="rounded-2xl px-5 py-3 text-sm font-semibold text-white flex items-center gap-2 transition hover:scale-[1.02]"
          style={{ background: "linear-gradient(135deg,#557eff,#3b5bdb)", boxShadow: "0 10px 30px -10px rgba(85,126,255,0.6)" }}
        >
          <Plus className="h-4 w-4" /> Iniciar Nuevo Traspaso Vehicular Avanzado
        </button>
      </div>

      {/* Table — card rows on solid header */}
      <div className="rounded-2xl">
        <div className="overflow-x-auto">
          <div className="min-w-[920px]">
            {/* Header bar */}
            <div
              className="grid items-center text-[11px] uppercase tracking-wider font-semibold rounded-xl px-4 py-3"
              style={{ background: "#dfe5ed", color: "#162744", gridTemplateColumns: "1.4fr 1.2fr 1.3fr 1.2fr 0.9fr 1fr 0.9fr 0.8fr" }}
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
                    className="grid items-center bg-white dark:bg-[#162744] rounded-xl px-4 py-3 text-sm shadow-[0_2px_8px_rgba(22,39,68,0.05)] hover:shadow-[0_8px_24px_rgba(85,126,255,0.18)] transition relative"
                    style={{ gridTemplateColumns: "1.4fr 1.2fr 1.3fr 1.2fr 0.9fr 1fr 0.9fr 0.8fr" }}
                  >
                    <div className="font-mono text-xs text-[#162744] dark:text-white truncate">{r.id}</div>
                    <div className="text-[#162744] dark:text-white/90 truncate">{r.tipo}</div>
                    <div className="text-[#162744] dark:text-white/90 truncate">{r.prop}</div>
                    <div className="text-[#162744]/80 dark:text-white/70 truncate">{r.tenant}</div>
                    <div className="font-mono text-xs text-[#162744]/70 dark:text-white/60">{r.fecha}</div>
                    <div className="text-[#162744]/80 dark:text-white/70 truncate">{r.ot}</div>
                    <div>
                      <span className="text-[11px] font-semibold px-2.5 py-1 rounded-full border whitespace-nowrap" style={{ background: c.bg, color: c.color, borderColor: c.border }}>{r.estado}</span>
                    </div>
                    <div className="flex items-center justify-end gap-1">
                      <button className="p-1.5 rounded-lg hover:bg-[#eef5ff] dark:hover:bg-white/10"><Eye className="h-4 w-4" style={{ color: "#557eff" }} /></button>
                      <button className="p-1.5 rounded-lg hover:bg-[#eef5ff] dark:hover:bg-white/10"><MoreVertical className="h-4 w-4 text-[#162744]/60 dark:text-white/60" /></button>
                    </div>
                    {hover === r.id && (
                      <div className="absolute right-4 top-12 z-20 bg-white dark:bg-[#162744] border border-[#dfe5ed] dark:border-white/10 rounded-xl p-1 w-56 shadow-xl">
                        <button className="w-full flex items-center gap-2 px-3 py-2 text-xs rounded-lg hover:bg-[#eef5ff] dark:hover:bg-white/10 text-[#162744] dark:text-white"><Ban className="h-3.5 w-3.5" style={{ color: "#ff4e00" }} /> Solicitud de anulación</button>
                        <button className="w-full flex items-center gap-2 px-3 py-2 text-xs rounded-lg hover:bg-[#eef5ff] dark:hover:bg-white/10 text-[#162744] dark:text-white"><RefreshCw className="h-3.5 w-3.5" style={{ color: "#557eff" }} /> Cambio de estado manual</button>
                        <button className="w-full flex items-center gap-2 px-3 py-2 text-xs rounded-lg hover:bg-[#eef5ff] dark:hover:bg-white/10 text-[#162744] dark:text-white"><Eraser className="h-3.5 w-3.5" style={{ color: "#00dbd5" }} /> Limpiar consolidado</button>
                        <div className="px-3 py-1.5 text-[10px] font-mono text-[#162744]/50 dark:text-white/50 border-t border-[#dfe5ed] dark:border-white/10 mt-1">tramites.admin.maestro ✓</div>
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