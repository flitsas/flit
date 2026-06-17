import { ShieldCheck, Fingerprint, ScanFace, FileCheck2 } from "lucide-react";
import { ModuleTitle } from "./ModuleTitle";

const ITEMS = [
  { id: "VID-2025-001", nombre: "Juan Vélez", tipo: "Cédula + Biometría", estado: "Aprobado", hash: "234344546f066a" },
  { id: "VID-2025-002", nombre: "Juan Camilo León", tipo: "Cédula + OCR IA", estado: "Pendiente", hash: "98aaef21bbcc01" },
  { id: "VID-2025-003", nombre: "Sonia Cadavid Ríos", tipo: "Biometría facial", estado: "Aprobado", hash: "ffaa12389bb009" },
  { id: "VID-2025-004", nombre: "Laura Bedoya Ríos", tipo: "Cédula + OCR IA", estado: "Rechazado", hash: "77deef99aa1100" },
];

const C: Record<string, string> = { Aprobado: "#00DBD5", Pendiente: "#F9AC00", Rechazado: "#FF4E00" };

export function Validaciones() {
  return (
    <div className="h-full w-full px-6 pt-5 pb-24 flex flex-col gap-4 overflow-hidden">
      <ModuleTitle title="Validaciones de Identidad" subtitle="Validación biométrica, OCR IA y cotejo RUNT en tiempo real." />
      <div className="grid grid-cols-4 gap-3 shrink-0">
        {[
          { l: "Validaciones hoy", v: 47, i: ShieldCheck },
          { l: "Biometrías OK", v: 38, i: ScanFace },
          { l: "OCR documentos", v: 124, i: FileCheck2 },
          { l: "Huellas verificadas", v: 22, i: Fingerprint },
        ].map((k) => {
          const Icon = k.i;
          return (
            <div key={k.l} className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border flex items-center justify-between" style={{ borderColor: "#DFE5ED" }}>
              <div>
                <p className="text-[11px] opacity-70 font-medium">{k.l}</p>
                <p className="text-2xl font-bold mt-1" style={{ color: "#00DBD5" }}>{k.v}</p>
              </div>
              <Icon className="h-8 w-8 opacity-40" />
            </div>
          );
        })}
      </div>
      <section className="rounded-2xl p-4 bg-white dark:bg-[#0B0F14] border shrink-0" style={{ borderColor: "#DFE5ED" }}>
        <div className="flex items-center justify-between gap-3 mb-3">
          <h2 className="text-sm font-bold">Reglas de validación documental</h2>
          <button className="px-3 py-1.5 rounded-lg text-[10px] font-semibold text-white" style={{ background: "#00DBD5" }}>
            Nueva regla OCR
          </button>
        </div>
        <div className="grid grid-cols-1 md:grid-cols-3 gap-3">
          {[
            ["Cédula ciudadanía", "OCR + cotejo facial", "Activa"],
            ["Licencia de tránsito", "OCR estructurado", "Activa"],
            ["Formulario firmado", "Detección de firma", "Borrador"],
          ].map(([doc, rule, estado]) => (
            <div key={doc as string} className="rounded-xl p-3 border" style={{ borderColor: "#DFE5ED" }}>
              <div className="flex items-center justify-between gap-2">
                <p className="text-xs font-semibold">{doc}</p>
                <span className="px-2 py-0.5 rounded-full text-[9px] font-semibold text-white" style={{ background: estado === "Activa" ? "#00DBD5" : "#F9AC00" }}>
                  {estado}
                </span>
              </div>
              <p className="text-[11px] opacity-70 mt-2">{rule}</p>
              <div className="flex gap-2 mt-3">
                <button className="px-2 py-1 rounded-lg text-[10px] font-semibold border" style={{ borderColor: "#557EFF", color: "#557EFF" }}>Editar</button>
                <button className="px-2 py-1 rounded-lg text-[10px] font-semibold text-white" style={{ background: "#557EFF" }}>Probar</button>
              </div>
            </div>
          ))}
        </div>
      </section>
      <div className="flex-1 min-h-0 flex flex-col">
        <div className="grid grid-cols-12 px-4 py-2.5 text-[10px] font-semibold uppercase rounded-t-xl" style={{ background: "#DFE5ED", color: "#162744" }}>
          <div className="col-span-2">ID</div>
          <div className="col-span-3">Persona</div>
          <div className="col-span-3">Tipo</div>
          <div className="col-span-2">Estado</div>
          <div className="col-span-2">Hash</div>
        </div>
        <div className="flex-1 overflow-y-auto space-y-2 pt-2">
          {ITEMS.map((r) => (
            <div key={r.id} className="grid grid-cols-12 items-center px-4 py-3 rounded-xl bg-white dark:bg-[#0B0F14] border text-xs" style={{ borderColor: "#DFE5ED" }}>
              <div className="col-span-2 font-mono">{r.id}</div>
              <div className="col-span-3 font-medium">{r.nombre}</div>
              <div className="col-span-3 opacity-70">{r.tipo}</div>
              <div className="col-span-2"><span className="px-2 py-0.5 rounded-full text-[10px] font-semibold text-white" style={{ background: C[r.estado] }}>{r.estado}</span></div>
              <div className="col-span-2 font-mono text-[10px] opacity-60">{r.hash}</div>
            </div>
          ))}
        </div>
      </div>
    </div>
  );
}
