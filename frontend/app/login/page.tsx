"use client";

// Página de login dedicada (HU #10172, AC1). Tras autenticarse redirige al
// returnUrl (preservado por el modal de sesión expirada) o al dashboard.
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { LoginForm } from "@/components/auth/LoginForm";
import { getRememberedEmail } from "@/lib/auth/session";

function LoginPageContent() {
  const router = useRouter();
  const params = useSearchParams();
  const returnUrl = params.get("returnUrl") || "/";

  return (
    <main className="min-h-screen flex items-center justify-center bg-[#eef5ff] px-4">
      <div className="w-full max-w-md bg-white rounded-2xl shadow-xl p-8">
        <h1 className="text-2xl font-bold text-[#162744] mb-1">Bienvenido a FLIT</h1>
        <p className="text-sm text-slate-500 mb-6">Inicia sesión para continuar.</p>
        <LoginForm defaultEmail={getRememberedEmail()} onSuccess={() => router.replace(returnUrl)} />
      </div>
    </main>
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={null}>
      <LoginPageContent />
    </Suspense>
  );
}
