"use client";

import { useEffect, useRef, useState } from "react";
import Link from "next/link";
import { loginUser } from "@/lib/api/auth";
import { rememberEmail, storeToken } from "@/lib/auth/session";

const logo = "/assets/logo-flit-white.svg";
import { ShieldAlert, Lock, Mail, User as UserIcon } from "lucide-react";

function ParticlesCanvas() {
  const ref = useRef<HTMLCanvasElement>(null);
  useEffect(() => {
    const canvas = ref.current;
    if (!canvas) return;
    const ctx = canvas.getContext("2d");
    if (!ctx) return;
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

// Login real del diseño base (Feature #10113, HU #10172). Conserva el prototipo
// (panel visual con partículas, branding) pero cablea la autenticación al backend:
// loginUser() → JWT en cookie+storage → onAuthenticated(). El 2FA queda fuera de
// alcance del Feature, así que el flujo es de un solo paso (credenciales).
export function Login({
  onAuthenticated,
  defaultEmail = "",
}: {
  onAuthenticated: () => void;
  defaultEmail?: string;
}) {
  const [email, setEmail] = useState(defaultEmail);
  const [pass, setPass] = useState("");
  const [error, setError] = useState("");
  const [blocked, setBlocked] = useState(false);
  const [loading, setLoading] = useState(false);

  async function submitCreds(e: React.FormEvent) {
    e.preventDefault();
    setError("");
    setBlocked(false);

    if (!email.trim() || !pass) {
      setError("Ingresa tu correo y contraseña.");
      return;
    }

    setLoading(true);
    try {
      const result = await loginUser(email.trim(), pass);
      storeToken(result.accessToken);
      rememberEmail(email.trim());
      onAuthenticated();
    } catch (err) {
      const status = (err as { status?: number }).status;
      if (status === 403) {
        // Cuenta bloqueada temporalmente (HU #10170): panel de acceso restringido.
        setBlocked(true);
      } else {
        setError("Correo o contraseña incorrectos.");
      }
    } finally {
      setLoading(false);
    }
  }

  return (
    <div className="h-screen w-full flex flex-col md:flex-row overflow-hidden">
      {/* LEFT — Visual panel */}
      <div
        className="relative w-full md:w-7/12 min-h-[260px] md:min-h-0 flex flex-col items-center justify-center overflow-hidden order-1"
        style={{ background: "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)" }}
      >
        <ParticlesCanvas />
        <div className="relative z-10 flex flex-col items-center px-8 text-center">
          <img src={logo} alt="FLIT 2.0" className="max-w-[280px] md:max-w-[360px] w-full h-auto object-contain" />
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
              Iniciar Sesión
            </h1>
          </div>

          {blocked ? (
            <div className="rounded-xl border p-5 flex gap-3 animate-fade-in" style={{ borderColor: "#ff4e00", background: "rgba(255,78,0,0.06)" }} role="alert">
              <ShieldAlert className="h-5 w-5 mt-0.5 shrink-0" style={{ color: "#ff4e00" }} />
              <div className="text-sm">
                <p className="font-semibold" style={{ color: "#ff4e00" }}>Acceso Restringido</p>
                <p className="text-slate-600 text-xs mt-1">Tu cuenta está bloqueada temporalmente. Contacta a tu administrador para restablecer el acceso.</p>
                <button
                  type="button"
                  onClick={() => setBlocked(false)}
                  className="text-xs mt-3 font-semibold transition hover:opacity-80"
                  style={{ color: "#557eff" }}
                >
                  ← Volver a intentar
                </button>
              </div>
            </div>
          ) : (
            <form onSubmit={submitCreds} className="space-y-4 animate-fade-in" aria-label="Iniciar sesión" noValidate>
              <div>
                <label htmlFor="login-email" className="text-xs font-medium text-slate-600 mb-1.5 block">Usuario Corporativo</label>
                <div className="relative">
                  <Mail className="h-4 w-4 absolute left-3 top-3.5 text-slate-400" />
                  <input
                    id="login-email"
                    type="email"
                    autoComplete="username"
                    value={email}
                    onChange={(e) => setEmail(e.target.value)}
                    placeholder="usuario@flit.io"
                    aria-invalid={error ? true : undefined}
                    className="w-full bg-white border border-slate-200 rounded-xl pl-10 pr-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
                  />
                </div>
              </div>
              <div>
                <label htmlFor="login-password" className="text-xs font-medium text-slate-600 mb-1.5 block">Contraseña</label>
                <div className="relative">
                  <Lock className="h-4 w-4 absolute left-3 top-3.5 text-slate-400" />
                  <input
                    id="login-password"
                    type="password"
                    autoComplete="current-password"
                    value={pass}
                    onChange={(e) => setPass(e.target.value)}
                    placeholder="••••••••"
                    aria-invalid={error ? true : undefined}
                    className="w-full bg-white border border-slate-200 rounded-xl pl-10 pr-3 py-2.5 text-sm outline-none transition focus:border-[#557eff] focus:ring-2 focus:ring-[#557eff]/20"
                  />
                </div>
                <Link href="/auth/forgot-password" className="inline-block text-xs mt-2 text-slate-500 hover:text-[#557eff] transition">
                  ¿Olvidó su contraseña?
                </Link>
              </div>

              {error && (
                <p role="alert" className="text-xs flex items-center gap-2" style={{ color: "#ff4e00" }}>
                  <ShieldAlert className="h-3.5 w-3.5" /> {error}
                </p>
              )}

              <button
                type="submit"
                disabled={loading}
                className="w-full rounded-xl py-3 text-sm font-semibold text-white transition hover:opacity-90 disabled:opacity-60"
                style={{ background: "#557eff" }}
              >
                {loading ? "Ingresando…" : "Iniciar Sesión"}
              </button>
            </form>
          )}
        </div>
      </div>
    </div>
  );
}
