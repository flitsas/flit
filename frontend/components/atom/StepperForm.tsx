import { useEffect, useRef, useState } from "react";
import { Check, Lock, AlertTriangle, Upload, ArrowLeft, ArrowRight, ShieldCheck, FileSignature, Pause, Sparkles, X, Loader2 } from "lucide-react";

const STEPS = [
  "Consulta del Vehículo",
  "Validación RUNT / RNMC",
  "Alertas SOAT y RTM",
  "Modalidad del Activo",
  "Vendedor & Comprador",
  "OCR Improntas",
  "Firma del Vendedor",
  "Firma del Comprador",
  "Gobernanza (isPaused)",
  "Confirmación & Hash",
];

export function StepperForm({ onExit }: { onExit: () => void }) {
  const [step, setStep] = useState(1);
  // Step 1
  const [placa, setPlaca] = useState("");
  const [forceDev, setForceDev] = useState(false);
  const collision = ["ABC123", "XYZ789", "EVE001"].includes(placa) && !forceDev;
  // Step 2
  const [pazSalvo, setPazSalvo] = useState(false);
  // Step 3
  const [yearAfter2019] = useState(true);
  // Step 4
  const [modalidad, setModalidad] = useState<"bilateral" | "leasing" | null>(null);
  // Step 5
  const [vendedorTipo, setVendedorTipo] = useState<"PN" | "PJ">("PN");
  const allowCopyEmail = false;
  // Step 6
  const [ocrRunning, setOcrRunning] = useState(false);
  const [ocrPct, setOcrPct] = useState(0);
  useEffect(() => {
    if (!ocrRunning) return;
    const id = setInterval(() => {
      setOcrPct((p) => {
        if (p >= 94) { clearInterval(id); setOcrRunning(false); return 94; }
        return p + 2;
      });
    }, 80);
    return () => clearInterval(id);
  }, [ocrRunning]);
  // Step 7 canvas
  const canvasRef = useRef<HTMLCanvasElement | null>(null);
  const drawing = useRef(false);
  const [signedVendor, setSignedVendor] = useState(false);
  const startDraw = (e: React.MouseEvent<HTMLCanvasElement>) => {
    drawing.current = true;
    const c = canvasRef.current!; const ctx = c.getContext("2d")!;
    ctx.strokeStyle = "#1e293b"; ctx.lineWidth = 2; ctx.lineCap = "round";
    ctx.beginPath();
    const r = c.getBoundingClientRect();
    ctx.moveTo(e.clientX - r.left, e.clientY - r.top);
  };
  const moveDraw = (e: React.MouseEvent<HTMLCanvasElement>) => {
    if (!drawing.current) return;
    const c = canvasRef.current!; const ctx = c.getContext("2d")!;
    const r = c.getBoundingClientRect();
    ctx.lineTo(e.clientX - r.left, e.clientY - r.top); ctx.stroke();
    setSignedVendor(true);
  };
  const endDraw = () => (drawing.current = false);
  const clearCanvas = () => {
    const c = canvasRef.current; if (!c) return;
    c.getContext("2d")!.clearRect(0, 0, c.width, c.height); setSignedVendor(false);
  };
  // Step 9
  const [isPaused, setIsPaused] = useState(false);
  const [pauseReason, setPauseReason] = useState("");
  // Step 10 hash — se genera en el handler de avance (no en render/efecto) para no llamar funciones impuras.
  const [hash, setHash] = useState("");
  useEffect(() => {
    if (step === 10 && !hash) {
      const h = Array.from({ length: 64 }, () => "0123456789abcdef"[Math.floor(Math.random() * 16)]).join("");
      // Genera el hash una sola vez al alcanzar el paso 10 (estado derivado del paso).
      // eslint-disable-next-line react-hooks/set-state-in-effect
      setHash(h);
    }
  }, [step, hash]);

  const canAdvance = () => {
    if (step === 1) return placa.length >= 5 && !collision;
    if (step === 2) return pazSalvo;
    if (step === 3) return true; // SOAT vigente simulado
    if (step === 4) return modalidad !== null;
    if (step === 6) return ocrPct >= 70;
    if (step === 7) return signedVendor;
    if (step === 8) return modalidad === "leasing" ? true : signedVendor;
    if (step === 9) return isPaused ? pauseReason.length > 5 : true;
    return true;
  };

  const next = () => {
    if (!canAdvance() || step >= 10) return;
    const target = step + 1;
    if (target === 10 && !hash) {
      setHash(Array.from({ length: 64 }, () => "0123456789abcdef"[Math.floor(Math.random() * 16)]).join(""));
    }
    setStep(target);
  };
  const prev = () => step > 1 && setStep(step - 1);

  return (
    <div className="grid lg:grid-cols-[280px_1fr] gap-6">
      {/* Stepper Sidebar */}
      <div className="glass rounded-2xl p-4 lg:sticky lg:top-[88px] lg:self-start max-h-[calc(100vh-120px)] overflow-y-auto">
        <div className="flex items-center justify-between mb-4">
          <h3 className="font-bold text-slate-800 text-sm">Asistente Transaccional</h3>
          <button onClick={onExit} className="p-1 rounded-lg hover:bg-white/70"><X className="h-4 w-4 text-slate-500" /></button>
        </div>
        <ol className="space-y-2">
          {STEPS.map((label, i) => {
            const n = i + 1;
            const done = n < step; const active = n === step;
            return (
              <li key={n} className={`flex items-start gap-3 p-2.5 rounded-xl transition ${active ? "bg-white/80 scale-[1.02]" : done ? "" : "opacity-40"}`} style={active ? { boxShadow: "inset 0 0 0 2px #557eff" } : {}}>
                <div className="h-7 w-7 rounded-full flex items-center justify-center text-xs font-bold font-mono shrink-0" style={{ background: done ? "#00dbd5" : active ? "#557eff" : "rgba(100,116,139,0.15)", color: done || active ? "white" : "#475569" }}>
                  {done ? <Check className="h-4 w-4" /> : n}
                </div>
                <div className="min-w-0">
                  <p className="text-[10px] font-mono text-slate-400">PASO {String(n).padStart(2, "0")}</p>
                  <p className="text-xs font-medium text-slate-700">{label}</p>
                </div>
              </li>
            );
          })}
        </ol>
      </div>

      {/* Canvas */}
      <div className="space-y-4">
        <div className="glass rounded-2xl p-6 sm:p-8 min-h-[500px]">
          <div className="flex items-center gap-2 mb-1">
            <span className="text-[10px] font-mono px-2 py-0.5 rounded" style={{ background: "rgba(85,126,255,0.1)", color: "#557eff" }}>PASO {step}/10</span>
            <span className="text-[10px] font-mono text-slate-400">TRASP-02_FLIT-DRAFT</span>
          </div>
          <h2 className="text-2xl font-bold text-slate-800 mt-2">{STEPS[step - 1]}</h2>

          <div className="mt-6">
            {step === 1 && (
              <div className="space-y-4">
                <p className="text-sm text-slate-500">Digite la placa del vehículo. El sistema validará colisiones de transacciones concurrentes.</p>
                <input
                  value={placa}
                  onChange={(e) => setPlaca(e.target.value.toUpperCase().replace(/[^A-Z0-9]/g, "").slice(0, 8))}
                  placeholder="ABC123"
                  className="w-full font-mono text-4xl tracking-[0.3em] text-center bg-white/70 border-2 border-slate-200 rounded-2xl py-6 outline-none focus:border-[#557eff] transition"
                />
                {collision && (
                  <div className="rounded-xl p-4" style={{ background: "rgba(255,78,0,0.08)", border: "1px solid #ff4e00" }}>
                    <div className="flex items-start gap-3">
                      <AlertTriangle className="h-5 w-5 mt-0.5 shrink-0" style={{ color: "#ff4e00" }} />
                      <div>
                        <p className="font-bold text-sm" style={{ color: "#ff4e00" }}>BLOQUEO POR COLISIÓN DE TRANSACCIÓN</p>
                        <p className="text-xs text-slate-600 mt-1 font-mono">La placa <b>{placa}</b> ya existe en trámite activo con estado ∈ &#123;Borrador(1), Preparado(3), Enviado(4)&#125;.</p>
                        <label className="flex items-center gap-2 mt-3 text-xs">
                          <input type="checkbox" checked={forceDev} onChange={(e) => setForceDev(e.target.checked)} />
                          <span className="text-slate-700">Forzar ambiente de desarrollo (DEV)</span>
                        </label>
                      </div>
                    </div>
                  </div>
                )}
              </div>
            )}

            {step === 2 && (
              <div className="space-y-4">
                <div className="grid sm:grid-cols-2 gap-3">
                  {!pazSalvo ? (
                    <>
                      <div className="rounded-xl p-4" style={{ background: "rgba(255,78,0,0.08)", border: "1px solid #ff4e00" }}>
                        <p className="text-xs font-mono text-slate-500">transitTaxes.paceAndSafe</p>
                        <p className="font-bold mt-1" style={{ color: "#ff4e00" }}>NO · Multas activas</p>
                        <p className="text-xs text-slate-600 mt-2">Deuda detectada: <span className="font-mono">$2,340,500 COP</span></p>
                      </div>
                      <div className="rounded-xl p-4" style={{ background: "rgba(255,78,0,0.08)", border: "1px solid #ff4e00" }}>
                        <p className="text-xs font-mono text-slate-500">RNMC.status</p>
                        <p className="font-bold mt-1" style={{ color: "#ff4e00" }}>2 medidas correctivas</p>
                        <p className="text-xs text-slate-600 mt-2">Bloqueo activo en Registro Nacional</p>
                      </div>
                    </>
                  ) : (
                    <div className="sm:col-span-2 rounded-xl p-4" style={{ background: "rgba(0,219,213,0.1)", border: "1px solid #00dbd5" }}>
                      <Check className="inline h-5 w-5 mr-2" style={{ color: "#00dbd5" }} />
                      <span className="font-semibold" style={{ color: "#00a39f" }}>Paz y Salvo Corporativo adjunto · Validación RUNT/RNMC superada</span>
                    </div>
                  )}
                </div>
                <button onClick={() => setPazSalvo(true)} className="w-full rounded-xl border-2 border-dashed border-[#557eff] py-8 text-sm text-[#557eff] hover:bg-[#557eff]/5 transition flex flex-col items-center gap-2">
                  <Upload className="h-6 w-6" />
                  Adjuntar Paz y Salvo Corporativo (Drag &amp; Drop)
                </button>
              </div>
            )}

            {step === 3 && (
              <div className="grid sm:grid-cols-2 gap-4">
                <div className="glass rounded-xl p-4">
                  <p className="text-xs text-slate-500">SOAT</p>
                  <p className="text-lg font-bold mt-1" style={{ color: "#00a39f" }}>VIGENTE</p>
                  <p className="text-xs font-mono text-slate-500 mt-2">Expira: 2026-11-12</p>
                </div>
                <div className="rounded-xl p-4" style={{ background: "rgba(245,158,11,0.08)", border: "1px solid rgba(245,158,11,0.4)" }}>
                  <p className="text-xs text-slate-500">RTM (Revisión Tecnicomecánica)</p>
                  <p className="text-lg font-bold mt-1" style={{ color: "#b45309" }}>ADVERTENCIA</p>
                  <p className="text-xs text-slate-600 mt-2">Vehículo matriculado posterior a 19/05/2019. Vigencia máxima: <b>5 años</b>.</p>
                  <p className="text-[10px] font-mono text-slate-400 mt-1">RTM.maxValidity = {yearAfter2019 ? "5y" : "6y"}</p>
                </div>
              </div>
            )}

            {step === 4 && (
              <div className="grid sm:grid-cols-2 gap-4">
                {([
                  { id: "bilateral", title: "Traspaso Estándar Bilateral", desc: "Operación clásica vendedor-comprador. Requiere ambas firmas y precio real." },
                  { id: "leasing", title: "Contrato Unilateral de Leasing", desc: "Precio simbólico $1 USD. Comprador deviene Locatario. Sin firma del comprador." },
                ] as const).map((m) => {
                  const sel = modalidad === m.id;
                  return (
                    <button key={m.id} onClick={() => setModalidad(m.id)} className={`text-left rounded-2xl p-5 glass transition hover:scale-[1.02] ${sel ? "" : "opacity-80"}`} style={sel ? { boxShadow: "inset 0 0 0 2px #557eff" } : {}}>
                      <div className="flex items-center justify-between">
                        <p className="font-bold text-slate-800">{m.title}</p>
                        {sel && <div className="h-6 w-6 rounded-full flex items-center justify-center" style={{ background: "#557eff" }}><Check className="h-4 w-4 text-white" /></div>}
                      </div>
                      <p className="text-xs text-slate-500 mt-2">{m.desc}</p>
                    </button>
                  );
                })}
              </div>
            )}

            {step === 5 && (
              <div className="grid md:grid-cols-2 gap-6">
                <div className="space-y-3">
                  <p className="text-xs font-mono text-slate-400 uppercase">Vendedor</p>
                  <div className="flex gap-2">
                    {(["PN", "PJ"] as const).map((t) => (
                      <button key={t} onClick={() => setVendedorTipo(t)} className={`px-3 py-1.5 rounded-lg text-xs font-semibold ${vendedorTipo === t ? "text-white" : "text-slate-600 bg-white/60"}`} style={vendedorTipo === t ? { background: "#557eff" } : {}}>
                        {t === "PN" ? "Persona Natural" : "Persona Jurídica (NIT)"}
                      </button>
                    ))}
                  </div>
                  <input placeholder={vendedorTipo === "PN" ? "Nombres y apellidos" : "Razón social"} className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff]" />
                  <input placeholder={vendedorTipo === "PN" ? "Cédula" : "NIT"} className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm font-mono outline-none focus:border-[#557eff]" />
                  {vendedorTipo === "PJ" && (
                    <select className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm">
                      <option>Representante Legal: María Restrepo</option>
                      <option>Representante Legal: Juan P. López</option>
                    </select>
                  )}
                </div>
                <div className="space-y-3">
                  <p className="text-xs font-mono text-slate-400 uppercase">Comprador {modalidad === "leasing" && "(Locatario)"}</p>
                  <input placeholder="Nombre completo" className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff]" />
                  <input placeholder="Identificación" className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm font-mono outline-none focus:border-[#557eff]" />
                  <div>
                    <input
                      placeholder="Correo electrónico"
                      onPaste={(e) => !allowCopyEmail && e.preventDefault()}
                      onCopy={(e) => !allowCopyEmail && e.preventDefault()}
                      className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff]"
                    />
                    {!allowCopyEmail && <p className="text-[10px] font-mono text-slate-400 mt-1">transferAllowsCopyEmail = false · copy/paste deshabilitado</p>}
                  </div>
                </div>
              </div>
            )}

            {step === 6 && (
              <div className="space-y-4">
                <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
                  {["Motor", "Chasis", "Serie", "VIN"].map((l) => (
                    <div key={l} className="rounded-xl bg-white/70 border border-dashed border-slate-300 p-4 text-center">
                      <Upload className="h-5 w-5 mx-auto text-slate-400" />
                      <p className="text-xs font-mono mt-2 text-slate-600">{l}</p>
                      {ocrPct > 30 && <p className="text-[10px]" style={{ color: "#00a39f" }}>✓ Capturado</p>}
                    </div>
                  ))}
                </div>
                <div className="flex items-center gap-4">
                  <div className="relative h-24 w-24 shrink-0">
                    <svg className="-rotate-90" viewBox="0 0 100 100">
                      <circle cx="50" cy="50" r="42" stroke="rgba(100,116,139,0.15)" strokeWidth="8" fill="none" />
                      <circle cx="50" cy="50" r="42" stroke="#00dbd5" strokeWidth="8" fill="none" strokeLinecap="round" strokeDasharray={`${(ocrPct / 100) * 264} 264`} />
                    </svg>
                    <div className="absolute inset-0 flex items-center justify-center font-mono font-bold text-slate-800">{ocrPct}%</div>
                  </div>
                  <div className="flex-1">
                    <button onClick={() => { setOcrPct(0); setOcrRunning(true); }} disabled={ocrRunning} className="rounded-xl px-4 py-2.5 text-sm font-semibold text-white inline-flex items-center gap-2" style={{ background: "#557eff" }}>
                      {ocrRunning ? <Loader2 className="h-4 w-4 animate-spin" /> : <Sparkles className="h-4 w-4" />} Simular Procesamiento OCR (IA)
                    </button>
                    {ocrPct >= 90 && <p className="text-xs mt-2 font-semibold" style={{ color: "#00a39f" }}>✓ Coincidencia ≥90% · Aprobación automática</p>}
                    {ocrPct >= 70 && ocrPct < 90 && <p className="text-xs mt-2 font-semibold" style={{ color: "#b45309" }}>⚠ Coincidencia 70-89% · Advertencia ámbar inyectada</p>}
                    {ocrPct > 0 && ocrPct < 70 && <p className="text-xs mt-2 font-semibold" style={{ color: "#ff4e00" }}>✕ &lt;70% · Improntas deficientes, recargar</p>}
                  </div>
                </div>
              </div>
            )}

            {step === 7 && (
              <div className="space-y-4">
                <div className="flex flex-wrap gap-2">
                  {["UNICUS Digital", "Firma en Pantalla", "Baúl Corporativo"].map((m, i) => (
                    <span key={m} className={`text-xs px-3 py-1.5 rounded-full ${i === 1 ? "text-white" : "bg-white/70 text-slate-600"}`} style={i === 1 ? { background: "#557eff" } : {}}>{m}</span>
                  ))}
                </div>
                <div className="rounded-xl bg-white border-2 border-slate-200 overflow-hidden">
                  <canvas ref={canvasRef} width={800} height={220} className="w-full h-[220px] cursor-crosshair" onMouseDown={startDraw} onMouseMove={moveDraw} onMouseUp={endDraw} onMouseLeave={endDraw} />
                </div>
                <div className="flex justify-between items-center">
                  <button onClick={clearCanvas} className="text-xs px-3 py-1.5 rounded-lg bg-white/70 text-slate-600 hover:bg-white">Limpiar Canvas</button>
                  {signedVendor && <span className="text-xs" style={{ color: "#00a39f" }}>✓ Firma capturada</span>}
                </div>
                <pre className="text-[10px] bg-slate-100 text-slate-500 rounded-lg p-2 font-mono overflow-x-auto">[IP: 192.168.1.45 | UA: Mozilla/5.0 (Windows NT 10.0; Win64) | RequestPath: /api/v2/tramites/sign]</pre>
              </div>
            )}

            {step === 8 && (
              modalidad === "leasing" ? (
                <div className="rounded-2xl p-8 text-center opacity-80" style={{ background: "rgba(100,116,139,0.08)", border: "1px dashed rgba(100,116,139,0.3)" }}>
                  <Lock className="h-10 w-10 mx-auto text-slate-400" />
                  <p className="font-bold text-slate-700 mt-3">Firma del comprador no requerida por ley bajo modalidad de leasing corporativo</p>
                  <p className="text-xs text-slate-500 mt-2 font-mono">auto-skip → step 9</p>
                </div>
              ) : (
                <div className="rounded-xl bg-white border-2 border-slate-200 p-6 text-center">
                  <FileSignature className="h-8 w-8 mx-auto text-slate-400" />
                  <p className="text-sm text-slate-600 mt-2">Repetir captura de firma para el comprador (mismas opciones).</p>
                </div>
              )
            )}

            {step === 9 && (
              <div className="space-y-4">
                <label className="flex items-start gap-3 p-4 rounded-xl bg-white/70 border border-slate-200 cursor-pointer">
                  <input type="checkbox" checked={isPaused} onChange={(e) => setIsPaused(e.target.checked)} className="mt-1" />
                  <div>
                    <p className="font-semibold text-slate-800 flex items-center gap-2"><Pause className="h-4 w-4" /> Activar flag de pausa global <span className="font-mono text-xs text-slate-500">isPaused: true</span></p>
                    <p className="text-xs text-slate-500 mt-1">Blindará el registro y deshabilitará cualquier transición de estado superior.</p>
                  </div>
                </label>
                {isPaused && (
                  <textarea value={pauseReason} onChange={(e) => setPauseReason(e.target.value)} placeholder="Justificación obligatoria de la pausa (mín. 6 caracteres)..." className="w-full bg-white/70 border border-slate-200 rounded-xl px-3 py-2.5 text-sm outline-none focus:border-[#557eff] min-h-[120px]" />
                )}
              </div>
            )}

            {step === 10 && (
              <div className="text-center py-6 space-y-5">
                <div className="relative mx-auto h-24 w-24">
                  <div className="absolute inset-0 rounded-full animate-pulse" style={{ background: "#557eff", filter: "blur(30px)", opacity: 0.5 }} />
                  <div className="relative h-24 w-24 rounded-full flex items-center justify-center" style={{ background: "linear-gradient(135deg,#557eff,#00dbd5)" }}>
                    <Check className="h-12 w-12 text-white" strokeWidth={3} />
                  </div>
                </div>
                <div>
                  <h3 className="text-2xl font-bold text-slate-800">Transacción Emitida</h3>
                  <p className="text-sm text-slate-500 mt-1">Expediente blindado e inmutable en el holding.</p>
                </div>
                <div className="inline-flex items-center gap-2 px-4 py-2 rounded-full font-mono text-xs" style={{ background: "rgba(85,126,255,0.1)", color: "#557eff" }}>
                  <ShieldCheck className="h-4 w-4" /> Estado: 4 (Submitted) · Bus QX OK
                </div>
                <div className="glass rounded-xl p-4 text-left mx-auto max-w-xl">
                  <p className="text-[10px] font-mono text-slate-400 uppercase">SHA-256 Inmutable</p>
                  <p className="font-mono text-[11px] break-all text-slate-700 mt-1">{hash}</p>
                </div>
                <button onClick={onExit} className="rounded-xl px-5 py-2.5 text-sm font-semibold text-white" style={{ background: "#557eff" }}>Volver a la Grilla Maestra</button>
              </div>
            )}
          </div>
        </div>

        {/* Footer nav */}
        {step < 10 && (
          <div className="flex justify-between items-center">
            <button onClick={prev} disabled={step === 1} className="inline-flex items-center gap-2 px-4 py-2.5 rounded-xl glass text-sm text-slate-700 disabled:opacity-40"><ArrowLeft className="h-4 w-4" /> Anterior</button>
            <p className="text-xs font-mono text-slate-400 hidden sm:block">Paso {step} de 10</p>
            <button onClick={next} disabled={!canAdvance()} className="inline-flex items-center gap-2 px-5 py-2.5 rounded-xl text-sm font-semibold text-white disabled:opacity-40 disabled:cursor-not-allowed" style={{ background: "#557eff" }}>
              Continuar <ArrowRight className="h-4 w-4" />
            </button>
          </div>
        )}
      </div>
    </div>
  );
}