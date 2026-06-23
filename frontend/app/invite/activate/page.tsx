import { ActivateAccountForm } from "@/components/auth/ActivateAccountForm";

export const metadata = { title: "Activar cuenta — FLIT" };

interface PageProps {
  searchParams: Promise<{ token?: string }>;
}

export default async function ActivateAccountPage({ searchParams }: PageProps) {
  const { token } = await searchParams;

  return (
    <main className="min-h-screen flex">
      {/* Left panel — brand gradient */}
      <div
        className="hidden md:flex md:w-7/12 flex-col items-center justify-center relative overflow-hidden"
        style={{ background: "linear-gradient(120deg,#00dbd5 0%,#557eff 100%)" }}
      >
        <div className="absolute top-[-80px] right-[-80px] w-[350px] h-[350px] rounded-full opacity-20 bg-white" />
        <div className="absolute bottom-[-60px] left-[-60px] w-[280px] h-[280px] rounded-full opacity-15 bg-white" />
        <div className="relative z-10 flex flex-col items-center gap-8 text-white px-12 text-center">
          <img src="/assets/logo-flit-white.svg" alt="FLIT" className="h-12 w-auto" />
          <div>
            <h2 className="text-3xl font-bold leading-tight">Bienvenido a FLIT</h2>
            <p className="mt-3 text-lg opacity-85">Activa tu cuenta para comenzar<br />a gestionar trámites de movilidad.</p>
          </div>
          <div className="flex flex-col gap-3 w-full max-w-xs">
            {[
              "Gestión de trámites en tiempo real",
              "Validación biométrica integrada",
              "Reportes y analíticas avanzadas",
            ].map((item) => (
              <div key={item} className="flex items-center gap-3 text-sm opacity-90">
                <div className="h-5 w-5 rounded-full flex items-center justify-center shrink-0" style={{ background: "rgba(255,255,255,0.25)" }}>
                  <svg viewBox="0 0 12 12" fill="none" className="h-3 w-3">
                    <path d="M2 6l3 3 5-5" stroke="white" strokeWidth="1.5" strokeLinecap="round" strokeLinejoin="round" />
                  </svg>
                </div>
                {item}
              </div>
            ))}
          </div>
        </div>
      </div>

      {/* Right panel — form */}
      <div className="w-full md:w-5/12 bg-white flex flex-col justify-center px-6 sm:px-12 lg:px-16 py-10">
        {/* Mobile logo */}
        <div
          className="md:hidden w-12 h-12 rounded-2xl flex items-center justify-center mb-6"
          style={{ background: "linear-gradient(135deg,#557EFF,#00DBD5)" }}
        >
          <img src="/assets/logo-flit-white.svg" alt="FLIT" className="h-7 w-auto" />
        </div>

        {/* Key icon */}
        <div className="w-12 h-12 rounded-2xl mb-6 hidden md:flex items-center justify-center" style={{ background: "#00dbd5" }}>
          <svg viewBox="0 0 24 24" fill="none" className="h-6 w-6" stroke="white" strokeWidth={2}>
            <path strokeLinecap="round" strokeLinejoin="round" d="M15 7a2 2 0 012 2m4 0a6 6 0 01-7.743 5.743L11 17H9v2H7v2H4a1 1 0 01-1-1v-2.586a1 1 0 01.293-.707l5.964-5.964A6 6 0 1121 9z" />
          </svg>
        </div>

        <h1 className="text-2xl font-bold mb-1" style={{ color: "#162744" }}>Activa tu cuenta</h1>
        <p className="text-sm mb-8 opacity-60" style={{ color: "#162744" }}>
          Define tu contraseña para completar el registro.
        </p>

        <ActivateAccountForm token={token ?? null} />
      </div>
    </main>
  );
}
