"use client";

// Página de login dedicada (HU #10172, AC1). Usa el mismo componente de login del
// diseño base (atom/Login) para no duplicar UI; tras autenticarse redirige al
// returnUrl preservado por el modal de sesión expirada, o al inicio.
import { useRouter, useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { Login } from "@/components/atom/Login";
import { getRememberedEmail } from "@/lib/auth/session";

function LoginPageContent() {
  const router = useRouter();
  const params = useSearchParams();
  const returnUrl = params.get("returnUrl") || "/";

  return (
    <Login onAuthenticated={() => router.replace(returnUrl)} defaultEmail={getRememberedEmail()} />
  );
}

export default function LoginPage() {
  return (
    <Suspense fallback={null}>
      <LoginPageContent />
    </Suspense>
  );
}
