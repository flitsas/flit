import { useState } from "react";
import { Check, ChevronLeft, ChevronRight, Search, ShieldCheck, AlertTriangle, FileSearch, GripVertical, Settings2, List } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";

const STEPS = [
  "Consulta del Vehículo",
  "Actores",
  "Documentos y firmas",
  "Confirmación",
];

type Row = { id: string; placa: string; veh: string; estado: string; resp: string; prog: number };

const ROWS_TRASPASOS: Row[] = [
  { id: "TR-098123849", placa: "ABC123", veh: "Toyota Corolla 2022 Blanco", estado: "Pendiente Firma", resp: "Juan Pérez", prog: 80 },
  { id: "TR-098123850", placa: "XYZ987", veh: "Mazda 3 2021 Gris", estado: "Validación", resp: "María Gómez", prog: 45 },
  { id: "TR-098123851", placa: "DEF456", veh: "Chevrolet Sail 2019 Rojo", estado: "Borrador", resp: "Luis Vélez", prog: 15 },
  { id: "TR-098123852", placa: "GHI321", veh: "Renault Logan 2020 Azul", estado: "Rechazado", resp: "Sonia Cadavid", prog: 100 },
  { id: "TR-098123853", placa: "JKL654", veh: "Nissan Versa 2018 Negro", estado: "Entregado", resp: "Laura Bedoya", prog: 100 },
];

const ROWS_MATRICULA: Row[] = [
  { id: "MI-202312001", placa: "NEW001", veh: "Mazda CX-30 2026 Rojo", estado: "Validación", resp: "Carlos Pineda", prog: 35 },
  { id: "MI-202312002", placa: "NEW002", veh: "Kia Sportage 2026 Blanco", estado: "Pendiente Firma", resp: "Andrea Ríos", prog: 70 },
  { id: "MI-202312003", placa: "NEW003", veh: "Renault Duster 2026 Gris", estado: "Borrador", resp: "Felipe Cardona", prog: 10 },
  { id: "MI-202312004", placa: "NEW004", veh: "Chevrolet Tracker 2026 Negro", estado: "Entregado", resp: "Paula Restrepo", prog: 100 },
];

const ROWS_OTROS: Row[] = [
  { id: "OT-552010091", placa: "ABC123", veh: "Cambio de color · Toyota Corolla", estado: "Validación", resp: "Mateo Ruiz", prog: 55 },
  { id: "OT-552010092", placa: "JKL654", veh: "Levantamiento de prenda · Nissan Versa", estado: "Pendiente Firma", resp: "Diana López", prog: 80 },
  { id: "OT-552010093", placa: "GHI321", veh: "Cambio de servicio · Renault Logan", estado: "Rechazado", resp: "Sergio Mejía", prog: 100 },
  { id: "OT-552010094", placa: "XYZ987", veh: "Regrabación de motor · Mazda 3", estado: "Entregado", resp: "Camila Soto", prog: 100 },
];

const TABS = [
  { id: "matricula", label: "Matrícula inicial", rows: ROWS_MATRICULA },
  { id: "traspasos", label: "Traspasos", rows: ROWS_TRASPASOS },
  { id: "otros", label: "Otros Trámites", rows: ROWS_OTROS },
] as const;

const BADGE: Record<string, string> = {
  "Pendiente Firma": "#F9AC00",
  Validación: "#557EFF",
  Borrador: "#8CC63F",
  Rechazado: "#FF4E00",
  Entregado: "#00DBD5",
};

export function Tramites() {
  const [wizard, setWizard] = useState(false);
  const [view, setView] = useState<"operacion" | "parametrizacion">("operacion");
  const [tab, setTab] = useState<(typeof TABS)[number]["id"]>("traspasos");
  if (wizard) return <Wizard onExit={() => setWizard(false)} />;
  const activeRows = TABS.find((t) => t.id === tab)!.rows;

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

      {view === "parametrizacion" ? (
        <ParametrizacionView />
      ) : (
        <OperacionView
          tab={tab}
          setTab={setTab}
          activeRows={activeRows}
          onNewTramite={() => setWizard(true)}
        />
      )}
    </div>
  );
}

function ParametrizacionView() {
  return (
    <div className="flex-1 min-h-0 flex flex-col gap-3 overflow-y-auto">
      <div className="flex flex-wrap items-center justify-between gap-2 shrink-0">
        <div className="flex flex-wrap gap-2">
          <button className="px-4 py-2 rounded-xl text-xs font-semibold text-white" style={{ background: "#557EFF" }}>
            Nuevo flujo
          </button>
          <button className="px-4 py-2 rounded-xl text-xs font-semibold border" style={{ borderColor: "#00DBD5", color: "#00DBD5" }}>
            Publicar versión
          </button>
          <button className="px-4 py-2 rounded-xl text-xs font-semibold border" style={{ borderColor: "#557EFF", color: "#557EFF" }}>
            Nueva regla
          </button>
        </div>
        <select className="px-3 py-2 rounded-xl border text-xs bg-white dark:bg-[#0B0F14] outline-none" style={{ borderColor: "#DFE5ED" }} defaultValue="sabaneta">
          <option value="sabaneta">OT · Sabaneta</option>
          <option value="medellin">OT · Medellín</option>
          <option value="envigado">OT · Envigado</option>
        </select>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-3 shrink-0">
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <h2 className="text-sm font-bold mb-3">Pasos del trámite</h2>
          <div className="space-y-2">
            {["Formulario base", "Validación RUNT", "Paso documental", "Firma digital", "Confirmación OT"].map((step, index) => (
              <div key={step} className="flex items-center gap-3 rounded-xl p-3 border bg-[rgba(85,126,255,0.03)] dark:bg-white/5" style={{ borderColor: "#DFE5ED" }}>
                <GripVertical className="h-4 w-4 opacity-30 shrink-0" />
                <span className="h-7 w-7 rounded-full grid place-items-center text-[11px] font-bold text-white shrink-0" style={{ background: "#557EFF" }}>
                  {index + 1}
                </span>
                <span className="text-xs font-medium flex-1">{step}</span>
                <button className="text-[10px] font-semibold opacity-60 hover:opacity-100">Editar</button>
              </div>
            ))}
          </div>
        </section>

        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <h2 className="text-sm font-bold mb-3">Constructor de reglas</h2>
          <div className="space-y-2">
            {[
              ["Si el vehículo tiene prenda", "Solicitar documento adicional"],
              ["Si la validación OCR falla", "Enviar a revisión manual"],
              ["Si el actor es jurídico", "Habilitar representante"],
            ].map(([rule, action]) => (
              <div key={rule as string} className="rounded-xl p-3 border" style={{ borderColor: "#DFE5ED" }}>
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
          <button className="w-full mt-3 py-2 rounded-xl text-xs font-semibold border border-dashed" style={{ borderColor: "#557EFF", color: "#557EFF" }}>
            + Agregar condición
          </button>
        </section>
      </div>

      <div className="grid grid-cols-1 xl:grid-cols-2 gap-3 shrink-0">
        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <h2 className="text-sm font-bold mb-3">Orden documental</h2>
          <div className="space-y-2">
            {[
              ["Licencia de tránsito", "Obligatorio"],
              ["SOAT vigente", "Obligatorio"],
              ["Paz y salvo", "Condicional"],
              ["Mandato autenticado", "Opcional"],
            ].map(([doc, level]) => (
              <div key={doc as string} className="flex items-center gap-3 rounded-xl p-3 border bg-[rgba(249,172,0,0.06)] dark:bg-white/5" style={{ borderColor: "#DFE5ED" }}>
                <GripVertical className="h-4 w-4 opacity-30 shrink-0" />
                <span className="text-xs font-medium flex-1">{doc}</span>
                <span className="text-[10px] font-semibold px-2 py-0.5 rounded-full" style={{ background: level === "Obligatorio" ? "rgba(249,172,0,0.2)" : level === "Condicional" ? "rgba(85,126,255,0.15)" : "rgba(0,219,213,0.15)", color: level === "Obligatorio" ? "#F9AC00" : level === "Condicional" ? "#557EFF" : "#00DBD5" }}>
                  {level}
                </span>
              </div>
            ))}
          </div>
        </section>

        <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <h2 className="text-sm font-bold mb-3">Configuración por OT</h2>
          <div className="grid grid-cols-12 px-3 py-2 text-[10px] font-semibold uppercase rounded-t-lg mb-2" style={{ background: "#DFE5ED", color: "#162744" }}>
            <div className="col-span-5">Documento</div>
            <div className="col-span-3">Validación IA</div>
            <div className="col-span-2">Orden</div>
            <div className="col-span-2 text-right">Activo</div>
          </div>
          <div className="space-y-2">
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
                  <span className="h-5 w-9 rounded-full relative" style={{ background: "#00DBD5" }}>
                    <span className="absolute top-0.5 h-4 w-4 rounded-full bg-white" style={{ left: "calc(100% - 18px)" }} />
                  </span>
                </div>
              </div>
            ))}
          </div>
        </section>
      </div>
    </div>
  );
}

function OperacionView({
  tab,
  setTab,
  activeRows,
  onNewTramite,
}: {
  tab: (typeof TABS)[number]["id"];
  setTab: (v: (typeof TABS)[number]["id"]) => void;
  activeRows: Row[];
  onNewTramite: () => void;
}) {
  return (
    <div className="flex-1 min-h-0 flex flex-col gap-4 overflow-hidden">
      <div className="grid grid-cols-12 gap-3 shrink-0">
        <div className="col-span-12 lg:col-span-8 flex flex-col gap-3">
          {/* Horizontal funnel — 7 states */}
          <div className="grid grid-cols-7 gap-2">
            {[
              ["Borrador", 128, "#8CC63F"],
              ["Validación", 96, "#557EFF"],
              ["Pendiente Docs.", 78, "#00DBD5"],
              ["Firma pendiente", 42, "#F9AC00"],
              ["Radicados", 28, "#557EFF"],
              ["Rechazados", 7, "#FF4E00"],
              ["Aprobados", 85, "#00DBD5"],
            ].map(([l, v, c], i, arr) => (
              <div key={l as string} className="relative rounded-xl p-3 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 text-center">
                <p className="text-[10px] opacity-70 font-medium truncate">{l as string}</p>
                <p className="text-xl font-bold mt-1" style={{ color: c as string }}>{v as number}</p>
                {i < arr.length - 1 && (
                  <ChevronRight className="hidden lg:block absolute -right-2 top-1/2 -translate-y-1/2 h-3 w-3 opacity-30" />
                )}
              </div>
            ))}
          </div>
          {/* Quick actions */}
          <div className="flex flex-wrap items-center gap-2">
            <button onClick={onNewTramite} className="px-5 py-2.5 rounded-xl text-xs font-semibold text-white" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>
              Nuevo Trámite
            </button>
            <button className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-xs font-semibold border" style={{ borderColor: "#557EFF", color: "#557EFF" }}>
              <ShieldCheck className="h-4 w-4" /> Validar documentos / Revisión OCR IA
            </button>
            <button className="flex items-center gap-2 px-4 py-2.5 rounded-xl text-xs font-semibold border" style={{ borderColor: "#557EFF", color: "#557EFF" }}>
              <FileSearch className="h-4 w-4" /> Consultar placa
            </button>
          </div>
        </div>
        {/* Alertas importantes */}
        <aside className="col-span-12 lg:col-span-4 rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10">
          <div className="flex items-center gap-2 mb-2">
            <AlertTriangle className="h-4 w-4" style={{ color: "#FF4E00" }} />
            <h3 className="text-xs font-bold">Alertas importantes</h3>
          </div>
          <ul className="space-y-1.5">
            {[
              { t: "Trámite bloqueado AAA111", d: "Requiere revisión", c: "#FF4E00" },
              { t: "1 firma pendiente ABC123", d: "Acción requerida", c: "#F9AC00" },
              { t: "3 SOAT próximos a vencer", d: "Faltan 7 días", c: "#557EFF" },
            ].map((a) => (
              <li key={a.t} className="flex items-start gap-2">
                <span className="h-1.5 w-1.5 rounded-full mt-1.5 shrink-0" style={{ background: a.c }} />
                <div className="min-w-0">
                  <p className="text-[11px] font-semibold truncate">{a.t}</p>
                  <p className="text-[10px] opacity-60">{a.d}</p>
                </div>
              </li>
            ))}
          </ul>
        </aside>
      </div>

      {/* Tabs */}
      <div className="flex items-center gap-1 border-b border-[#DFE5ED] dark:border-white/10 shrink-0">
        {TABS.map((t) => {
          const isActive = t.id === tab;
          return (
            <button
              key={t.id}
              onClick={() => setTab(t.id)}
              className="relative px-4 py-2.5 text-xs font-semibold transition"
              style={{
                color: isActive ? "#557EFF" : undefined,
                opacity: isActive ? 1 : 0.65,
              }}
            >
              {t.label}
              {isActive && (
                <span className="absolute left-2 right-2 -bottom-px h-0.5 rounded-full" style={{ background: "#557EFF" }} />
              )}
            </button>
          );
        })}
      </div>

      {/* Table */}
      <div className="flex-1 min-h-0 rounded-2xl overflow-hidden flex flex-col bg-transparent">
        <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl" style={{ background: "#DFE5ED", color: "#162744" }}>
          <div className="col-span-2">ID Trámite</div>
          <div className="col-span-1">Placa</div>
          <div className="col-span-3">Vehículo</div>
          <div className="col-span-2">Estado</div>
          <div className="col-span-2">Responsable</div>
          <div className="col-span-2">Progreso</div>
        </div>
        <div className="flex-1 min-h-0 overflow-y-auto overflow-x-auto space-y-2 pt-2">
          {activeRows.map((r) => (
            <div key={r.id} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border border-[#DFE5ED] dark:border-white/10 text-xs">
              <div className="col-span-2 font-mono font-medium">{r.id}</div>
              <div className="col-span-1 font-bold tracking-wider">{r.placa}</div>
              <div className="col-span-3 opacity-80">{r.veh}</div>
              <div className="col-span-2"><span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: BADGE[r.estado] }}>{r.estado}</span></div>
              <div className="col-span-2 opacity-80">{r.resp}</div>
              <div className="col-span-2 flex items-center gap-2">
                <div className="flex-1 h-1.5 rounded-full overflow-hidden" style={{ background: "#DFE5ED" }}>
                  <div className="h-full" style={{ width: `${r.prog}%`, background: "#557EFF" }} />
                </div>
                <span className="text-[10px] opacity-70 w-7">{r.prog}%</span>
              </div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}

function Wizard({ onExit }: { onExit: () => void }) {
  const [step, setStep] = useState(0);
  const [done, setDone] = useState(false);

  if (done) return <CompletedModal onClose={() => { setDone(false); onExit(); }} />;

  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <div className="flex items-center justify-between">
        <h1 className="text-2xl font-bold">Nuevo Traspaso Vehicular</h1>
        <button onClick={onExit} className="text-xs opacity-70 hover:opacity-100">← Cancelar</button>
      </div>

      <div className="grid grid-cols-12 gap-4 flex-1 min-h-0">
        {/* Stepper sidebar */}
        <aside className="col-span-12 md:col-span-3 rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border" style={{ borderColor: "#DFE5ED" }}>
          <p className="text-[10px] font-semibold uppercase opacity-60 mb-3">Asistente de seguimiento</p>
          <ul className="space-y-3">
            {STEPS.map((s, i) => {
              const isDone = i < step;
              const isActive = i === step;
              return (
                <li key={s} className="flex items-center gap-3">
                  <span
                    className="h-8 w-8 rounded-full grid place-items-center text-[11px] font-bold shrink-0"
                    style={{
                      background: isDone ? "#00DBD5" : isActive ? "#557EFF" : "#DFE5ED",
                      color: isDone || isActive ? "#fff" : "#162744",
                    }}
                  >
                    {isDone ? <Check className="h-4 w-4" /> : i + 1}
                  </span>
                  <span className={`text-xs ${isActive ? "font-bold" : "opacity-70"}`}>{s}</span>
                </li>
              );
            })}
          </ul>
        </aside>

        {/* Step content */}
        <section className="col-span-12 md:col-span-9 rounded-2xl p-5 bg-white dark:bg-[#0B0F14] border overflow-y-auto" style={{ borderColor: "#DFE5ED" }}>
          {step === 0 && <StepConsulta />}
          {step === 1 && <StepActores />}
          {step === 2 && <StepDocs />}
          {step === 3 && <StepConfirm />}

          <div className="flex items-center justify-between mt-6 pt-4 border-t" style={{ borderColor: "#DFE5ED" }}>
            <button
              onClick={() => setStep((s) => Math.max(0, s - 1))}
              disabled={step === 0}
              className="flex items-center gap-1 px-4 py-2 rounded-xl text-xs font-medium border disabled:opacity-30"
              style={{ borderColor: "#162744", color: "#162744" }}
            >
              <ChevronLeft className="h-3 w-3" /> Anterior
            </button>
            {step < STEPS.length - 1 ? (
              <button
                onClick={() => setStep((s) => s + 1)}
                className="flex items-center gap-1 px-5 py-2 rounded-xl text-xs font-semibold text-white"
                style={{ background: "#557EFF" }}
              >
                Continuar <ChevronRight className="h-3 w-3" />
              </button>
            ) : (
              <button
                onClick={() => setDone(true)}
                className="px-5 py-2 rounded-xl text-xs font-semibold text-white"
                style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
              >
                Finalizar Traspaso
              </button>
            )}
          </div>
        </section>
      </div>
    </div>
  );
}

function Field({ label, value }: { label: string; value: string }) {
  return (
    <div>
      <p className="text-[10px] opacity-60 font-medium">{label}</p>
      <p className="text-xs font-medium mt-0.5">{value}</p>
    </div>
  );
}

function StepConsulta() {
  return (
    <div className="space-y-4">
      <div>
        <label className="text-xs font-semibold block mb-1.5">
          Tipo de trámite <span style={{ color: "#FF4E00" }}>*</span>
        </label>
        <select
          required
          defaultValue=""
          className="w-full md:w-1/2 px-3 py-2 rounded-xl border bg-white dark:bg-[#0B0F14] text-xs outline-none focus:border-[#557EFF]"
          style={{ borderColor: "#DFE5ED" }}
        >
          <option value="" disabled>Selecciona el tipo de trámite</option>
          <option value="traspaso">Traspaso</option>
          <option value="matricula">Matrícula Inicial</option>
          <option value="otros">Otros Trámites</option>
        </select>
      </div>
      <h2 className="text-base font-bold">Datos del Vehículo consultado</h2>
      <div className="flex items-center gap-2 p-3 rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
        <Search className="h-4 w-4 opacity-60" />
        <input defaultValue="ABC123" className="flex-1 bg-transparent outline-none text-sm tracking-[0.4em] font-bold uppercase" />
        <button className="px-3 py-1 rounded-md text-xs font-semibold text-white" style={{ background: "#557EFF" }}>Consultar</button>
      </div>
      <div className="grid grid-cols-2 md:grid-cols-3 gap-4">
        <Field label="Placa" value="ABC123" />
        <Field label="VIN" value="1234567890" />
        <Field label="Marca" value="Toyota" />
        <Field label="Modelo" value="Corolla" />
        <Field label="Año" value="2019" />
        <Field label="Organismo de tránsito" value="Sabaneta" />
        <Field label="Multas activas" value="NO" />
        <Field label="Deuda detectada" value="$2,340,500 COP" />
        <Field label="RNMC" value="2 medidas correctivas" />
      </div>
      <div className="pt-3">
        <p className="text-xs font-semibold mb-2">Tipo de traspaso</p>
        <div className="flex gap-3">
          <label className="flex items-center gap-2 text-xs"><input type="radio" name="tt" defaultChecked /> Traspaso bilateral</label>
          <label className="flex items-center gap-2 text-xs"><input type="radio" name="tt" /> Traspaso unilateral</label>
        </div>
      </div>
    </div>
  );
}

function StepActores() {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
      <div>
        <h3 className="text-sm font-bold mb-3">Vendedor</h3>
        <div className="space-y-2">
          <Field label="Cédula" value="1234567" />
          <Field label="Nombre" value="Juan Vélez" />
          <Field label="Celular" value="300 987 7888" />
          <Field label="Correo" value="juvez@gmail.com" />
          <Field label="Dirección" value="calle 9 #7-29 Medellín" />
        </div>
      </div>
      <div>
        <h3 className="text-sm font-bold mb-3">Comprador</h3>
        <div className="flex items-center gap-2 p-2 rounded-xl border mb-3" style={{ borderColor: "#DFE5ED" }}>
          <Search className="h-4 w-4 opacity-60" />
          <input placeholder="Consulta cédula del comprador" className="flex-1 bg-transparent outline-none text-xs" />
        </div>
        <div className="space-y-2">
          <Field label="Nombre" value="—" />
          <Field label="Celular" value="—" />
          <Field label="Correo" value="—" />
          <Field label="Dirección" value="—" />
        </div>
      </div>
    </div>
  );
}

function StepDocs() {
  return (
    <div className="grid grid-cols-1 md:grid-cols-2 gap-5">
      {["Vendedor", "Comprador"].map((p) => (
        <div key={p}>
          <h3 className="text-sm font-bold mb-3">{p}</h3>
          <div className="space-y-2 mb-3">
            <Field label="Validado" value="✓ Identidad verificada" />
            <Field label="Hash Validación" value="234344546f066a" />
          </div>
          <div className="p-3 rounded-xl border text-center text-xs opacity-70" style={{ borderColor: "#DFE5ED" }}>
            Firma digital pendiente
          </div>
          {p === "Comprador" && (
            <button className="mt-3 w-full px-4 py-2 rounded-xl text-xs font-semibold text-white" style={{ background: "#557EFF" }}>Enviar Validación</button>
          )}
        </div>
      ))}
    </div>
  );
}

function StepConfirm() {
  return (
    <div className="space-y-4">
      <h2 className="text-base font-bold">Confirmación final</h2>
      <p className="text-xs opacity-70">Revisa los datos antes de radicar el trámite al organismo de tránsito.</p>
      <div className="grid grid-cols-1 md:grid-cols-2 gap-4">
        {["Vendedor", "Comprador"].map((p) => (
          <div key={p} className="p-4 rounded-xl border" style={{ borderColor: "#DFE5ED" }}>
            <p className="text-xs font-bold mb-2">{p}</p>
            <Field label="Validado" value="✓ Completado" />
            <Field label="Hash" value="234344546f066a" />
            <Field label="Firma" value="✓ Aplicada" />
          </div>
        ))}
      </div>
    </div>
  );
}

function CompletedModal({ onClose }: { onClose: () => void }) {
  return (
    <div className="h-full w-full grid place-items-center px-6 pb-24">
      <div className="max-w-md w-full rounded-3xl p-8 bg-white dark:bg-[#0B0F14] border text-center" style={{ borderColor: "#DFE5ED" }}>
        <div className="h-16 w-16 mx-auto rounded-full grid place-items-center mb-4" style={{ background: "rgba(0,219,213,0.15)" }}>
          <Check className="h-8 w-8" style={{ color: "#00DBD5" }} />
        </div>
        <h2 className="text-xl font-bold">¡Traspaso completado!</h2>
        <p className="text-xs opacity-70 mt-2">Placa asociada:</p>
        <div className="flex justify-center gap-2 my-4">
          {"ABC123".split("").map((c, i) => (
            <span key={i} className="h-10 w-9 rounded-lg grid place-items-center text-lg font-bold border" style={{ borderColor: "#557EFF", color: "#162744" }}>{c}</span>
          ))}
        </div>
        <p className="text-xs opacity-70 mb-5">El trámite fue validado y enviado correctamente al organismo de tránsito.</p>
        <button onClick={onClose} className="w-full py-2.5 rounded-xl text-sm font-semibold text-white" style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}>Ir al Dashboard</button>
      </div>
    </div>
  );
}