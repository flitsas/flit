import { useState } from "react";
import { Mail, Phone, Building2, KeyRound, LifeBuoy, ArrowLeft, Check, X } from "lucide-react";
import { buttonClass } from "@flit/ui/Button";

// Homologación (HU #14511): tarjetas, controles y botones del kit; el diálogo se repinta en su sitio con la franja del Modal.
const CARD = "rounded-[20px] border border-border bg-card p-6 shadow-[var(--shadow-card)]";
const INPUT =
  "w-full mt-1 rounded-xl border border-[color:var(--di-border-control)] bg-[color:var(--di-surface-elevated)] px-3.5 py-2.5 text-sm text-foreground outline-none transition " +
  "focus:border-[color:var(--di-action)] focus:ring-2 focus:ring-[color:var(--di-accent-soft)]";
const ITEM = "w-full flex items-center gap-3 rounded-xl border border-border bg-[color:var(--di-surface-elevated)] hover:bg-[color:var(--di-accent-soft)] p-4 text-left transition";

export function ProfileZone({ onBack }: { onBack: () => void }) {
  const [name, setName] = useState("Auditor Senior FLIT");
  const [addr, setAddr] = useState("Cra. 43A # 5-15, Medellín");
  const [phone, setPhone] = useState("+57 300 123 4567");
  const [showPwdModal, setShowPwdModal] = useState(false);
  const phoneOk = /^\+?\d[\d\s-]{6,}$/.test(phone);

  return (
    <div className="space-y-6">
      <button onClick={onBack} className="inline-flex items-center gap-2 text-sm text-muted-foreground hover:text-foreground"><ArrowLeft className="h-4 w-4" /> Volver al Dashboard</button>

      <div className="grid lg:grid-cols-3 gap-6">
        {/* Bloque 1 */}
        <div className={CARD}>
          <p className="text-[10px] font-mono uppercase text-muted-foreground">Bloque Informativo Corporativo</p>
          <div className="flex items-center gap-4 mt-4">
            <div className="h-16 w-16 rounded-2xl flex items-center justify-center text-[color:var(--di-on-action)] text-xl font-bold [background:var(--di-gradient-action)]">AE</div>
            <div>
              <p className="font-bold text-foreground">Auditor Senior FLIT</p>
              <p className="text-xs text-muted-foreground">Rol · Gobernanza Transaccional</p>
            </div>
          </div>
          <dl className="mt-5 space-y-3 text-sm">
            <div className="flex items-center gap-2 text-muted-foreground"><Mail className="h-4 w-4 text-muted-foreground" /><span className="font-mono text-xs">auditor.senior@flit.io</span></div>
            <div className="flex items-center gap-2 text-muted-foreground"><Phone className="h-4 w-4 text-muted-foreground" /><span className="font-mono text-xs">+57 1 555 0099</span></div>
            <div className="flex items-center gap-2 text-muted-foreground"><Building2 className="h-4 w-4 text-muted-foreground" /><span className="text-xs">Renting Colombia S.A.</span></div>
          </dl>
          <p className="text-xs text-muted-foreground mt-4 leading-relaxed">Responsable de auditoría transaccional sobre el ecosistema SaaS multi-tenant. Acceso al permiso <span className="font-mono">tramites.admin.maestro</span> y orquestación de la máquina de estados finita.</p>
        </div>

        {/* Bloque 2 */}
        <div className={CARD}>
          <p className="text-[10px] font-mono uppercase text-muted-foreground">Autoservicio Contenidista</p>
          <div className="mt-4 space-y-3">
            <div>
              <label className="text-xs text-muted-foreground">Nombre completo</label>
              <input value={name} onChange={(e) => setName(e.target.value)} className={INPUT} />
              {name.length < 3 && <p className="text-[10px] mt-1" style={{ color: "var(--badge-danger-fg)" }}>Mínimo 3 caracteres</p>}
            </div>
            <div>
              <label className="text-xs text-muted-foreground">Dirección residencial</label>
              <input value={addr} onChange={(e) => setAddr(e.target.value)} className={INPUT} />
            </div>
            <div>
              <label className="text-xs text-muted-foreground">Teléfono de contacto</label>
              <input value={phone} onChange={(e) => setPhone(e.target.value)} className={`${INPUT} font-mono`} />
              {!phoneOk ? (
                <p className="text-[10px] mt-1 flex items-center gap-1" style={{ color: "var(--badge-danger-fg)" }}><X className="h-3 w-3" /> Formato inválido</p>
              ) : (
                <p className="text-[10px] mt-1 flex items-center gap-1" style={{ color: "var(--badge-success-fg)" }}><Check className="h-3 w-3" /> Formato válido</p>
              )}
            </div>
            <button className={buttonClass({ className: "w-full mt-2" })}>Guardar cambios</button>
          </div>
        </div>

        {/* Bloque 3 */}
        <div className={`${CARD} space-y-3`}>
          <p className="text-[10px] font-mono uppercase text-muted-foreground">Panel Transaccional</p>
          <button onClick={() => setShowPwdModal(true)} className={ITEM}>
            <KeyRound className="h-5 w-5 text-[color:var(--di-action)]" />
            <div>
              <p className="text-sm font-semibold text-foreground">Cambiar Contraseña</p>
              <p className="text-xs text-muted-foreground">Política: 12 chars · alfanum + símbolo</p>
            </div>
          </button>
          <button className={ITEM}>
            <LifeBuoy className="h-5 w-5 text-[color:var(--di-info)]" />
            <div>
              <p className="text-sm font-semibold text-foreground">Solicitud de Soporte Técnico</p>
              <p className="text-xs text-muted-foreground font-mono">→ landing externa con metadatos</p>
            </div>
          </button>
          <button onClick={onBack} className={ITEM}>
            <ArrowLeft className="h-5 w-5 text-muted-foreground" />
            <div>
              <p className="text-sm font-semibold text-foreground">Volver al Dashboard</p>
              <p className="text-xs text-muted-foreground">Grilla Maestra de Trámites</p>
            </div>
          </button>
        </div>
      </div>

      {showPwdModal && (
        <div className="fixed inset-0 z-50 flex items-center justify-center overflow-y-auto bg-[var(--overlay)] p-4 backdrop-blur-md">
          <div className="w-full max-w-md max-h-[90dvh] overflow-y-auto overflow-x-clip rounded-[var(--di-radius-modal)] bg-card shadow-[var(--shadow-modal)]">
            <div className="relative flex items-center justify-between gap-3 overflow-clip bg-[color:var(--di-accent)] px-5 py-4 text-[color:var(--di-on-accent)]">
              <span aria-hidden="true" className="pointer-events-none absolute -right-[30px] -top-[60px] h-[120px] w-[120px] rounded-full bg-[color:var(--di-highlight-decor)]" />
              <h3 className="relative z-[1] font-bold text-[color:var(--di-on-accent)]">Cambiar Contraseña</h3>
              <button onClick={() => setShowPwdModal(false)} className="relative z-[1] grid h-10 w-10 place-items-center rounded-full bg-[color-mix(in_srgb,var(--di-on-accent)_18%,transparent)] text-[color:var(--di-on-accent)] transition hover:bg-[color-mix(in_srgb,var(--di-on-accent)_30%,transparent)]"><X className="h-5 w-5" /></button>
            </div>
            <div className="space-y-3 p-5">
              <input type="password" placeholder="Contraseña actual" className={INPUT} />
              <input type="password" placeholder="Nueva contraseña" className={INPUT} />
              <input type="password" placeholder="Confirmar nueva contraseña" className={INPUT} />
              <button onClick={() => setShowPwdModal(false)} className={buttonClass({ className: "w-full" })}>Actualizar contraseña</button>
            </div>
          </div>
        </div>
      )}
    </div>
  );
}