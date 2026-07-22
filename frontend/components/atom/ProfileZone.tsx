import { useState } from "react";
import { Mail, Phone, Building2, KeyRound, LifeBuoy, ArrowLeft, Check, X } from "lucide-react";

export function ProfileZone({ onBack }: { onBack: () => void }) {
  const [name, setName] = useState("Auditor Senior FLIT");
  const [addr, setAddr] = useState("Cra. 43A # 5-15, Medellín");
  const [phone, setPhone] = useState("+57 300 123 4567");
  const [showPwdModal, setShowPwdModal] = useState(false);
  const phoneOk = /^\+?\d[\d\s-]{6,}$/.test(phone);

  return (
    <div className="space-y-6">
      <button onClick={onBack} className="inline-flex items-center gap-2 text-sm text-slate-600 hover:text-slate-900"><ArrowLeft className="h-4 w-4" /> Volver al Dashboard</button>

      <div className="grid lg:grid-cols-3 gap-6">
        {/* Bloque 1 */}
        <div className="glass rounded-2xl p-6">
          <p className="text-[10px] font-mono uppercase text-slate-400">Bloque Informativo Corporativo</p>
          <div className="flex items-center gap-4 mt-4">
            <div className="h-16 w-16 rounded-2xl flex items-center justify-center text-white text-xl font-bold" style={{ background: "linear-gradient(135deg,#557eff,#00dbd5)" }}>AE</div>
            <div>
              <p className="font-bold text-slate-800">Auditor Senior FLIT</p>
              <p className="text-xs text-slate-500">Rol · Gobernanza Transaccional</p>
            </div>
          </div>
          <dl className="mt-5 space-y-3 text-sm">
            <div className="flex items-center gap-2 text-slate-600"><Mail className="h-4 w-4 text-slate-400" /><span className="font-mono text-xs">auditor.senior@flit.io</span></div>
            <div className="flex items-center gap-2 text-slate-600"><Phone className="h-4 w-4 text-slate-400" /><span className="font-mono text-xs">+57 1 555 0099</span></div>
            <div className="flex items-center gap-2 text-slate-600"><Building2 className="h-4 w-4 text-slate-400" /><span className="text-xs">Renting Colombia S.A.</span></div>
          </dl>
          <p className="text-xs text-slate-500 mt-4 leading-relaxed">Responsable de auditoría transaccional sobre el ecosistema SaaS multi-tenant. Acceso al permiso <span className="font-mono">tramites.admin.maestro</span> y orquestación de la máquina de estados finita.</p>
        </div>

        {/* Bloque 2 */}
        <div className="glass rounded-2xl p-6">
          <p className="text-[10px] font-mono uppercase text-slate-400">Autoservicio Contenidista</p>
          <div className="mt-4 space-y-3">
            <div>
              <label className="text-xs text-slate-500">Nombre completo</label>
              <input value={name} onChange={(e) => setName(e.target.value)} className="w-full mt-1 bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff] transition" />
              {name.length < 3 && <p className="text-[10px] mt-1" style={{ color: "#ff4e00" }}>Mínimo 3 caracteres</p>}
            </div>
            <div>
              <label className="text-xs text-slate-500">Dirección residencial</label>
              <input value={addr} onChange={(e) => setAddr(e.target.value)} className="w-full mt-1 bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff] transition" />
            </div>
            <div>
              <label className="text-xs text-slate-500">Teléfono de contacto</label>
              <input value={phone} onChange={(e) => setPhone(e.target.value)} className="w-full mt-1 bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm font-mono outline-none focus:border-[#557eff] transition" />
              {!phoneOk ? (
                <p className="text-[10px] mt-1 flex items-center gap-1" style={{ color: "#ff4e00" }}><X className="h-3 w-3" /> Formato inválido</p>
              ) : (
                <p className="text-[10px] mt-1 flex items-center gap-1" style={{ color: "#00a39f" }}><Check className="h-3 w-3" /> Formato válido</p>
              )}
            </div>
            <button className="w-full rounded-xl py-2.5 text-sm font-semibold text-white mt-2" style={{ background: "#557eff" }}>Guardar cambios</button>
          </div>
        </div>

        {/* Bloque 3 */}
        <div className="glass rounded-2xl p-6 space-y-3">
          <p className="text-[10px] font-mono uppercase text-slate-400">Panel Transaccional</p>
          <button onClick={() => setShowPwdModal(true)} className="w-full flex items-center gap-3 rounded-xl bg-white/70 hover:bg-white p-4 text-left transition">
            <KeyRound className="h-5 w-5" style={{ color: "#557eff" }} />
            <div>
              <p className="text-sm font-semibold text-slate-800">Cambiar Contraseña</p>
              <p className="text-xs text-slate-500">Política: 12 chars · alfanum + símbolo</p>
            </div>
          </button>
          <button className="w-full flex items-center gap-3 rounded-xl bg-white/70 hover:bg-white p-4 text-left transition">
            <LifeBuoy className="h-5 w-5" style={{ color: "#00dbd5" }} />
            <div>
              <p className="text-sm font-semibold text-slate-800">Solicitud de Soporte Técnico</p>
              <p className="text-xs text-slate-500 font-mono">→ landing externa con metadatos</p>
            </div>
          </button>
          <button onClick={onBack} className="w-full flex items-center gap-3 rounded-xl bg-white/70 hover:bg-white p-4 text-left transition">
            <ArrowLeft className="h-5 w-5 text-slate-600" />
            <div>
              <p className="text-sm font-semibold text-slate-800">Volver al Dashboard</p>
              <p className="text-xs text-slate-500">Grilla Maestra de Trámites</p>
            </div>
          </button>
        </div>
      </div>

      {showPwdModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto p-4" style={{ background: "rgba(15,23,42,0.4)", backdropFilter: "blur(8px)" }}>
          <div className="glass rounded-2xl p-6 w-full max-w-md max-h-[90dvh] overflow-y-auto">
            <div className="flex justify-between items-center mb-4">
              <h3 className="font-bold text-slate-800">Cambiar Contraseña</h3>
              <button onClick={() => setShowPwdModal(false)} className="p-1 rounded-lg hover:bg-white/70"><X className="h-4 w-4" /></button>
            </div>
            <div className="space-y-3">
              <input type="password" placeholder="Contraseña actual" className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff]" />
              <input type="password" placeholder="Nueva contraseña" className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff]" />
              <input type="password" placeholder="Confirmar nueva contraseña" className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff]" />
              <button onClick={() => setShowPwdModal(false)} className="w-full rounded-xl py-2.5 text-sm font-semibold text-white" style={{ background: "#557eff" }}>Actualizar contraseña</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}