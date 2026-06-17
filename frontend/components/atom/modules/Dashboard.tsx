import { useEffect, useState } from "react";
import { FileText, Car, ClipboardList, Activity, ChevronLeft, ChevronRight, Sparkles, ShieldCheck } from "lucide-react";
import {
  BarChart,
  Bar,
  XAxis,
  YAxis,
  Tooltip,
  ResponsiveContainer,
  CartesianGrid,
} from "recharts";

const KPIS = [
  { label: "Comparendos nuevos", value: 12, icon: FileText, color: "#557EFF" },
  { label: "Vehículos sin SOAT", value: 138, icon: Car, color: "#FF4E00" },
  { label: "Total comparendos", value: 135, icon: ClipboardList, color: "#00DBD5" },
  { label: "Trámites activos", value: 38, icon: Activity, color: "#557EFF" },
];

const FUNNEL = [
  { n: 1, label: "Borrador", value: 45, color: "#557EFF" },
  { n: 2, label: "Validación RUNT", value: 23, color: "#00DBD5" },
  { n: 3, label: "Firma digital", value: 15, color: "#F9AC00" },
  { n: 4, label: "Emisión", value: 8, color: "#8CC63F" },
];

const EVENTS = [
  { title: "Entrega completada ABC123", when: "Hace 5 minutos", color: "#00DBD5" },
  { title: "Trámite aprobado AbC123", when: "Hace 8 minutos", color: "#557EFF" },
  { title: "Reporte Mensual listo", when: "Hace 15 minutos", color: "#557EFF" },
  { title: "Nuevo Comparendo XBC123", when: "Hace 60 minutos", color: "#FF4E00" },
  { title: "Nuevo SOAT generado", when: "Hace 45 minutos", color: "#00DBD5" },
];

const BARS = [
  { m: "Ene", Iniciados: 70, Pendientes: 30, Entregados: 40 },
  { m: "Feb", Iniciados: 50, Pendientes: 40, Entregados: 30 },
  { m: "Mar", Iniciados: 45, Pendientes: 35, Entregados: 45 },
  { m: "Abr", Iniciados: 40, Pendientes: 45, Entregados: 35 },
  { m: "May", Iniciados: 45, Pendientes: 40, Entregados: 50 },
  { m: "Jun", Iniciados: 35, Pendientes: 45, Entregados: 30 },
  { m: "Jul", Iniciados: 40, Pendientes: 20, Entregados: 40 },
];

const SLIDES = [
  {
    type: "welcome" as const,
    title: "Hola, Mateo Ruiz 👋",
    body: "Tus procesos y validaciones se encuentran sincronizados. Continúa gestionando tu operación de manera segura y eficiente.",
    bg: "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)",
  },
  {
    type: "news" as const,
    title: "Nueva integración disponible",
    body: "Sistema de validación de identidad con Inteligencia Artificial ya integrado en tus trámites.",
    bg: "linear-gradient(120deg,#16a34a 0%,#22c55e 100%)",
  },
  {
    type: "promo" as const,
    title: "Compra el SOAT de tu vehículo",
    body: "Aprovecha tarifas preferenciales y emisión inmediata directamente desde la plataforma.",
    cta: "Cómpralo aquí",
    bg: "#ff4e00",
  },
  {
    type: "alert" as const,
    title: "3 SOAT próximos a vencer",
    body: "Revisa los vehículos de tu flota con pólizas que vencen en los próximos 7 días para evitar bloqueos operativos.",
    bg: "#ff4e00",
  },
  {
    type: "info" as const,
    title: "Nuevas novedades de la plataforma",
    body: "Hemos publicado mejoras en validación RUNT, reportes ejecutivos y trazabilidad de firmas digitales.",
    bg: "#557eff",
  },
];

export function Dashboard({ onNewTramite }: { onNewTramite: () => void }) {
  const [slide, setSlide] = useState(0);
  useEffect(() => {
    const id = setInterval(() => setSlide((s) => (s + 1) % SLIDES.length), 6000);
    return () => clearInterval(id);
  }, []);
  const s = SLIDES[slide];
  return (
    <div className="h-full w-full px-6 pt-5 pb-24 overflow-hidden flex flex-col gap-4">
      {/* Banner + KPIs in the same row (3-col grid: banner spans 2, KPIs 2x2 in col 3) */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3 shrink-0">
        {/* Banner Carousel */}
        <div
          className="relative md:col-span-2 rounded-2xl px-6 py-5 text-white overflow-hidden flex flex-col justify-between"
          style={{
            background: s.bg,
            minHeight: "220px",
          }}
        >
          <div className="absolute -right-10 -top-10 h-36 w-36 rounded-full opacity-15" style={{ background: "#ffffff" }} />
          <div className="relative flex flex-col gap-3 max-w-[85%]">
            <div className="flex items-center gap-3">
              <div className="h-10 w-10 rounded-xl grid place-items-center shrink-0" style={{ background: "rgba(255,255,255,0.18)" }}>
                {s.type === "welcome" ? <Activity className="h-5 w-5" /> : s.type === "news" ? <Sparkles className="h-5 w-5" /> : <ShieldCheck className="h-5 w-5" />}
              </div>
              <h2 className="text-2xl md:text-3xl font-bold leading-tight">{s.title}</h2>
            </div>
            <p className="text-sm md:text-base opacity-95 leading-snug line-clamp-3">{s.body}</p>
            {s.type === "promo" && (
              <div>
                <button className="px-4 py-2 rounded-lg text-xs font-bold" style={{ background: "#ffffff", color: "#162744" }}>
                  {s.cta}
                </button>
              </div>
            )}
          </div>
          <div className="flex items-center justify-between mt-3 relative">
            <div className="flex gap-1">
              {SLIDES.map((_, i) => (
                <button key={i} onClick={() => setSlide(i)} aria-label={`Slide ${i + 1}`} className="h-1.5 rounded-full transition-all" style={{ width: i === slide ? 16 : 5, background: i === slide ? "#ffffff" : "rgba(255,255,255,0.5)" }} />
              ))}
            </div>
            <div className="flex items-center gap-1">
              <button onClick={() => setSlide((v) => (v - 1 + SLIDES.length) % SLIDES.length)} aria-label="Anterior" className="h-6 w-6 rounded-full grid place-items-center bg-white/15 hover:bg-white/25">
                <ChevronLeft className="h-3 w-3" />
              </button>
              <button onClick={() => setSlide((v) => (v + 1) % SLIDES.length)} aria-label="Siguiente" className="h-6 w-6 rounded-full grid place-items-center bg-white/15 hover:bg-white/25">
                <ChevronRight className="h-3 w-3" />
              </button>
            </div>
          </div>
        </div>

        {/* KPIs 2x2 inside column 3 */}
        <div className="grid grid-cols-2 gap-3">
        {KPIS.map((k) => {
          const Icon = k.icon;
          return (
            <div
              key={k.label}
              className="rounded-2xl p-4 flex items-center justify-between bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10"
            >
              <div>
                <p className="text-[11px] opacity-70 font-medium">{k.label}</p>
                <p className="text-3xl font-bold mt-1" style={{ color: k.color }}>{k.value}</p>
              </div>
              <div className="h-11 w-11 rounded-xl grid place-items-center" style={{ background: `${k.color}1A` }}>
                <Icon className="h-5 w-5" style={{ color: k.color }} />
              </div>
            </div>
          );
        })}
        </div>
      </div>

      {/* Bottom grid */}
      <div className="grid grid-cols-1 md:grid-cols-3 gap-3 flex-1 min-h-0">
        {/* Embudo */}
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col min-h-0">
          <h2 className="text-sm font-bold mb-3">Embudo Traspasos</h2>
          <ul className="space-y-2 flex-1">
            {FUNNEL.map((f) => (
              <li key={f.n} className="flex items-center gap-3 p-2 rounded-xl bg-[rgba(85,126,255,0.06)] dark:bg-white/5">
                <span className="h-7 w-7 rounded-full grid place-items-center text-[11px] font-bold text-white shrink-0" style={{ background: f.color }}>{f.n}</span>
                <span className="flex-1 text-xs font-medium">{f.label}</span>
                <span className="text-base font-bold" style={{ color: f.color }}>{f.value}</span>
              </li>
            ))}
          </ul>
        </section>

        {/* Eventos */}
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col min-h-0">
          <h2 className="text-sm font-bold mb-3">Eventos Recientes</h2>
          <ul className="space-y-2.5 overflow-y-auto flex-1 pr-1">
            {EVENTS.map((e, i) => (
              <li key={i} className="flex items-start gap-2.5">
                <span className="h-2 w-2 rounded-full mt-1.5 shrink-0" style={{ background: e.color }} />
                <div className="flex-1 min-w-0">
                  <p className="text-xs font-medium truncate">{e.title}</p>
                  <p className="text-[10px] opacity-60">{e.when}</p>
                </div>
              </li>
            ))}
          </ul>
        </section>

        {/* Bar chart */}
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 flex flex-col min-h-0">
          <h2 className="text-sm font-bold mb-3">Seguimiento operativo</h2>
          <div className="flex-1 min-h-0 -mx-2">
            <ResponsiveContainer width="100%" height="100%">
              <BarChart data={BARS} margin={{ top: 4, right: 8, left: -20, bottom: 0 }}>
                <CartesianGrid strokeDasharray="3 3" stroke="rgba(127,127,127,0.18)" vertical={false} />
                <XAxis dataKey="m" tick={{ fontSize: 10, fill: "currentColor" }} axisLine={false} tickLine={false} />
                <YAxis tick={{ fontSize: 10, fill: "currentColor" }} axisLine={false} tickLine={false} />
                <Tooltip
                  cursor={{ fill: "rgba(85,126,255,0.08)" }}
                  contentStyle={{
                    background: "rgba(22,39,68,0.95)",
                    border: "none",
                    borderRadius: 10,
                    color: "#fff",
                    fontSize: 11,
                  }}
                  labelStyle={{ color: "#00DBD5", fontWeight: 600 }}
                />
                <Bar dataKey="Iniciados" fill="#557EFF" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Pendientes" fill="#00DBD5" radius={[4, 4, 0, 0]} />
                <Bar dataKey="Entregados" fill="#F9AC00" radius={[4, 4, 0, 0]} />
              </BarChart>
            </ResponsiveContainer>
          </div>
          <div className="flex items-center justify-center gap-3 mt-1 text-[9px]">
            <Legend color="#557EFF" label="Iniciados" />
            <Legend color="#00DBD5" label="Pendientes" />
            <Legend color="#F9AC00" label="Entregados" />
          </div>
        </section>
      </div>
    </div>
  );
}

function Legend({ color, label }: { color: string; label: string }) {
  return (
    <span className="flex items-center gap-1">
      <span className="h-2 w-2 rounded-full" style={{ background: color }} />
      {label}
    </span>
  );
}