import { BarChart3, TrendingUp, FileSpreadsheet, Download, Bell, Clock } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";

export function Reportes() {
  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle title="Reportes y Analíticas" subtitle="Monitorea el desempeño operativo de tu flota." />
      <div className="grid grid-cols-2 xl:grid-cols-6 gap-3 shrink-0">
        {[
          { l: "Trámites del mes", v: "238", d: "+12% vs ayer", i: TrendingUp, c: "#557EFF" },
          { l: "Tasa aprobación", v: "94%", d: "+3% vs mes anterior", i: BarChart3, c: "#557EFF" },
          { l: "Reportes generados", v: "47", d: "Este mes", i: FileSpreadsheet, c: "#557EFF" },
          { l: "Tiempo medio", v: "12 min", d: "Radicación", i: Clock, c: "#00DBD5" },
          { l: "Alertas activas", v: "07", d: "Requieren acción", i: Bell, c: "#FF4E00" },
          { l: "Exportes hoy", v: "19", d: "PDF / Excel", i: Download, c: "#00DBD5" },
        ].map((k) => {
          const Icon = k.i;
          return (
            <div key={k.l} className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border flex flex-col justify-between min-h-[110px]" style={{ borderColor: "#DFE5ED" }}>
              <div className="flex items-start justify-between gap-2">
                <div>
                  <p className="text-[11px] opacity-70 font-medium">{k.l}</p>
                  <p className="text-2xl font-bold mt-1" style={{ color: k.c }}>{k.v}</p>
                  <p className="text-[10px] opacity-60 mt-0.5">{k.d}</p>
                </div>
                <div className="h-9 w-9 rounded-xl grid place-items-center shrink-0" style={{ background: `${k.c}1A` }}>
                  <Icon className="h-4 w-4" style={{ color: k.c }} />
                </div>
              </div>
            </div>
          );
        })}
      </div>
      <div className="flex-1 min-h-0 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border flex flex-col" style={{ borderColor: "#DFE5ED" }}>
        <div className="flex items-center justify-between mb-4 shrink-0">
          <h2 className="text-sm font-bold">Reportes Disponibles</h2>
          <button className="flex items-center gap-1.5 text-xs font-semibold px-3 py-1.5 rounded-lg text-white" style={{ background: "#557EFF" }}>
            <Download className="h-3 w-3" /> Exportar
          </button>
        </div>
        <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl shrink-0" style={{ background: "#DFE5ED", color: "#162744" }}>
          <div className="col-span-5">Reporte</div>
          <div className="col-span-2">Categoría</div>
          <div className="col-span-2">Periodo</div>
          <div className="col-span-2">Estado</div>
          <div className="col-span-1 text-right">Acción</div>
        </div>
        <div className="space-y-2 flex-1 overflow-y-auto pt-2">
          {[
            ["Reporte Mensual de Traspasos", "Operación", "Mayo 2026", "Listo"],
            ["Auditoría de Validaciones RUNT", "Calidad", "Q2 2026", "Listo"],
            ["Resumen de comparendos", "Gobierno", "Abr 2026", "Programado"],
            ["Eficiencia operativa por tenant", "Analítica", "2026", "Listo"],
            ["Cumplimiento ISO 27001", "Seguridad", "Continuo", "En curso"],
          ].map(([name, category, period, status]) => (
            <div key={name as string} className="grid grid-cols-12 items-center p-3 rounded-xl border text-xs" style={{ borderColor: "#DFE5ED" }}>
              <div className="col-span-5 font-medium">{name as string}</div>
              <div className="col-span-2 opacity-75">{category as string}</div>
              <div className="col-span-2 opacity-75">{period as string}</div>
              <div className="col-span-2">
                <span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: status === "Listo" ? "#00DBD5" : status === "Programado" ? "#557EFF" : "#F9AC00" }}>
                  {status as string}
                </span>
              </div>
              <div className="col-span-1 text-right">
                <button className="text-[10px] font-semibold opacity-70 hover:opacity-100">PDF</button>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
