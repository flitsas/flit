"use client";

import { useEffect, useRef, useState } from "react";

const logo = "/assets/logo-flit-white.svg";
import { ShieldAlert, Lock, Mail, User as UserIcon, X, KeyRound } from "lucide-react";

const VALID_USER = "admin@flit.io";
const VALID_PASS = "atom2026";
const VALID_2FA = "123456";

function ParticlesCanvas() {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d")!;
    let raf = 0;
    const resize = () => {
      canvas.width = canvas.offsetWidth * devicePixelRatio;
      canvas.height = canvas.offsetHeight * devicePixelRatio;
    };
    resize();
    window.addEventListener("resize", resize);
    const N = 80;
    const pts = Array.from({ length: N }, () => ({
      x: Math.random() * canvas.width,
      y: Math.random() * canvas.height,
      vx: (Math.random() - 0.5) * 0.4 * devicePixelRatio,
      vy: (Math.random() - 0.5) * 0.4 * devicePixelRatio,
    }));
    const tick = () => {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
      for (const p of pts) {
        p.x += p.vx; p.y += p.vy;
        if (p.x < 0 || p.x > canvas.width) p.vx *= -1;
        if (p.y < 0 || p.y > canvas.height) p.vy *= -1;
      }
      for (let i = 0; i < N; i++) {
        for (let j = i + 1; j < N; j++) {
          const dx = pts[i].x - pts[j].x, dy = pts[i].y - pts[j].y;
          const d = Math.hypot(dx, dy);
          if (d < 120 * devicePixelRatio) {
            ctx.strokeStyle = `rgba(255,255,255,${0.12 * (1 - d / (120 * devicePixelRatio))})`;
            ctx.lineWidth = 1;
            ctx.beginPath();
            ctx.moveTo(pts[i].x, pts[i].y);
            ctx.lineTo(pts[j].x, pts[j].y);
            ctx.stroke();
          }
        }
        ctx.fillStyle = "rgba(255,255,255,0.6)";
        ctx.beginPath();
        ctx.arc(pts[i].x, pts[i].y, 1.5 * devicePixelRatio, 0, Math.PI * 2);
        ctx.fill();
      }
      raf = requestAnimationFrame(tick);
    };
    tick();
    return () => { cancelAnimationFrame(raf); window.removeEventListener("resize", resize); };
  }, []);
  return <canvas ref={ref} className="absolute inset-0 w-full h-full" />;
}

export function Login({ onBypass }: { onBypass: () => void }) {
  const [email, setEmail] = useState("");
  const [pass, setPass] = useState("");
  const [attempts, setAttempts] = useState(0);
  const [error, setError] = useState("");
  const [phase, setPhase] = useState<"creds" | "2fa">("creds");
  const [code, setCode] = useState("");
  const [code2faError, setCode2faError] = useState("");
  const [lockSeconds, setLockSeconds] = useState(0);
  const [forgotOpen, setForgotOpen] = useState(false);
  const [forgotEmail, setForgotEmail] = useState("");
  const [forgotSent, setForgotSent] = useState(false);

  useEffect(() => {
    if (lockSeconds <= 0) return;
    // El efecto se reejecuta en cada cambio de lockSeconds, así que el cierre lee el valor actual.
    const id = setInterval(() => {
      if (lockSeconds <= 1) {
        setLockSeconds(0);
        if (attempts >= 3) setAttempts(0); // reset de intentos al expirar el bloqueo
      } else {
        setLockSeconds(lockSeconds - 1);
      }
    }, 1000);
    return () => clearInterval(id);
  }, [lockSeconds, attempts]);

  const locked = lockSeconds > 0;
  const mm = String(Math.floor(lockSeconds / 60)).padStart(2, "0");
  const ss = String(lockSeconds % 60).padStart(2, "0");

  const submitCreds = (e: React.FormEvent) => {
    e.preventDefault();
    if (locked) return;
    // Acceso libre: cualquier email + cualquier contraseña pasan a 2FA.
    setError("");
    setAttempts(0);
    setPhase("2fa");
  };

  const onCodeChange = (v: string) => {
    const clean = v.replace(/\D/g, "").slice(0, 6);
    setCode(clean);
    setCode2faError("");
    if (clean.length === 6) {
      // Acceso libre: cualquier código de 6 dígitos otorga acceso.
      setTimeout(() => onBypass(), 250);
    }
  };

  return (
    <div className="h-screen w-full flex flex-col md:flex-row overflow-hidden">
      {/* LEFT — Visual panel */}
      <div
        className="relative w-full md:w-7/12 min-h-[260px] md:min-h-0 flex flex-col items-center justify-center overflow-hidden order-1"
        style={{ background: "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)" }}
      >
        <ParticlesCanvas />
        <div className="relative z-10 flex flex-col items-center px-8 text-center">
          <img src={logo} alt="FLIT 2.0" className="max-w-[280px] md:max-w-[360px] w-full h-auto object-contain brightness-0 invert" />
        </div>
        <a
          href="#"
          className="absolute bottom-0 left-0 right-0 mb-6 text-center text-xs text-white/70 hover:text-white transition z-10"
        >
          Políticas de Privacidad y Términos de Uso
        </a>
      </div>

      {/* RIGHT — Form */}
      <div className="w-full md:w-5/12 bg-white flex flex-col justify-center px-6 sm:px-12 lg:px-16 py-10 order-2 overflow-y-auto">
        <div className="max-w-sm w-full mx-auto">
          <div className="flex flex-col items-center gap-3 mb-8">
            <div className="h-16 w-16 rounded-full flex items-center justify-center" style={{ background: "#00dbd5" }}>
              <UserIcon className="h-8 w-8 text-white" strokeWidth={2.2} />
            </div>
            <h1 className="text-2xl font-bold" style={{ color: "#557eff", fontFamily: "Poppins, sans-serif" }}>
              {phase === "creds" ? "Iniciar Sesión" : "Verificación 2FA"}
            </h1>
          </div>

          {locked ? (
            <div className="rounded-xl border p-5 flex gap-3 animate-fade-in" style={{ borderColor: "#ff4e00", background: "rgba(255,78,0,0.06)" }}>
              <ShieldAlert className="h-5 w-5 mt-0.5 shrink-0" style={{ color: "#ff4e00" }} />
              <div className="text-sm">
                <p className="font-semibold" style={{ color: "#ff4e00" }}>Acceso Restringido</p>
                <p className="text-slate-600 text-xs mt-1">Se han detectado 3 intentos fallidos. Acceso bloqueado temporalmente por seguridad.</p>
                <p className="text-2xl mt-3" style={{ color: "#ff4e00", fontFamily: "JetBrains Mono, monospace" }}>{mm}:{ss}</p>
              </div>
            </div>
          ) : phase === "creds" ? (
            <form onSubmit={submitCreds} className="space-y-4 animate-fade-in">
              <div>
                <label className="text-xs font-medium text-slate-600 mb-1.5 block">Usuario Corporativo</label>
                <div className="relative">
                  <Mail className="h-4 w-4 absolute left-3 top-3.5 text-slate-400" />
                  <input
                    type="email"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="usuario@flit.io"
                    className="w-full bg-white border border-slate-200 rounded-xl pl-10 pr-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
                    required
                  />
                </div>
              </div>
              <div>
                <label className="text-xs font-medium text-slate-600 mb-1.5 block">Contraseña</label>
                <div className="relative">
                  <Lock className="h-4 w-4 absolute left-3 top-3.5 text-slate-400" />
                  <input
                    type="password"
                    value={pass}
                    onChange={(e) => setPass(e.target.value)}
                    placeholder="••••••••"
                    className="w-full bg-white border border-slate-200 rounded-xl pl-10 pr-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
                    required
                  />
                </div>
                <button type="button" onClick={() => { setForgotOpen(true); setForgotSent(false); }} className="text-xs mt-2 text-slate-500 hover:text-[#557eff] transition">
                  ¿Olvidó su contraseña?
                </button>
              </div>

              {error && (
                <p className="text-xs flex items-center gap-2" style={{ color: "#ff4e00" }}>
                  <ShieldAlert className="h-3.5 w-3.5" /> {error}
                </p>
              )}

              <button
                type="submit"
                className="w-full rounded-xl py-3 text-sm font-semibold text-white transition hover:opacity-90"
                style={{ background: "#557eff" }}
              >
                Iniciar Sesión
              </button>
            </form>
          ) : (
            <div className="space-y-5 animate-fade-in">
              <div className="rounded-xl bg-[#557eff]/5 border border-[#557eff]/20 p-4 flex gap-3">
                <KeyRound className="h-5 w-5 mt-0.5 shrink-0" style={{ color: "#557eff" }} />
                <div className="text-xs text-slate-600 leading-relaxed">
                  <p className="font-semibold text-slate-800 mb-1">Verificación de Identidad</p>
                  Hemos enviado un código de seguridad de 6 dígitos a su correo corporativo registrado. Por favor ingréselo a continuación.
                </div>
              </div>

              <div>
                <label className="text-xs font-medium text-slate-600 mb-1.5 block">Código de Verificación</label>
                <input
                  autoFocus
                  inputMode="numeric"
                  maxLength={6}
                  value={code}
                  onChange={(e) => onCodeChange(e.target.value)}
                  placeholder="000000"
                  className="w-full bg-white border-2 border-slate-200 rounded-xl px-4 py-4 text-center text-2xl tracking-[0.6em] outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
                  style={{ fontFamily: "JetBrains Mono, monospace" }}
                />
                {code2faError && (
                  <p className="text-xs mt-2 flex items-center gap-2" style={{ color: "#ff4e00" }}>
                    <ShieldAlert className="h-3.5 w-3.5" /> {code2faError}
                  </p>
                )}
                <p className="text-[11px] text-slate-400 mt-3 text-center">
                  El acceso se concederá automáticamente al ingresar el último dígito.
                </p>
              </div>

              <button
                onClick={() => { setPhase("creds"); setCode(""); setCode2faError(""); }}
                className="text-xs text-slate-500 hover:text-[#557eff] transition w-full text-center"
              >
                ← Volver a credenciales
              </button>
            </div>
          )}
        </div>
      </div>

      {/* Forgot password modal */}
      {forgotOpen && (
        <div className="fixed inset-0 z-50 flex items-center justify-center bg-slate-900/40 backdrop-blur-sm animate-fade-in px-4">
          <div className="bg-white rounded-2xl w-full max-w-md p-6 shadow-2xl animate-scale-in">
            <div className="flex items-start justify-between mb-4">
              <div>
                <h3 className="text-lg font-bold text-slate-800" style={{ fontFamily: "Poppins, sans-serif" }}>Recuperar contraseña</h3>
                <p className="text-xs text-slate-500 mt-1">Ingrese su correo corporativo y recibirá instrucciones de recuperación.</p>
              </div>
              <button onClick={() => setForgotOpen(false)} className="text-slate-400 hover:text-slate-700">
                <X className="h-5 w-5" />
              </button>
            </div>
            {forgotSent ? (
              <div className="rounded-xl p-4 text-sm" style={{ background: "rgba(0,219,213,0.08)", color: "#0a7d79" }}>
                Hemos enviado las instrucciones a <span className="font-semibold">{forgotEmail}</span>. Revise su bandeja de entrada.
              </div>
            ) : (
              <form
                onSubmit={(e) => { e.preventDefault(); setForgotSent(true); }}
                className="space-y-3"
              >
                <div className="relative">
                  <Mail className="h-4 w-4 absolute left-3 top-3.5 text-slate-400" />
                  <input
                    type="email"
                    required
                    value={forgotEmail}
                    onChange={(e) => setForgotEmail(e.target.value)}
                    placeholder="usuario@flit.io"
                    className="w-full bg-white border border-slate-200 rounded-xl pl-10 pr-3 py-2.5 text-sm outline-none focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
                  />
                </div>
                <button type="submit" className="w-full rounded-xl py-2.5 text-sm font-semibold text-white" style={{ background: "#557eff" }}>
                  Enviar instrucciones
                </button>
              </form>
            )}
          </div>
        </div>
      )}
    </div>
  );
}