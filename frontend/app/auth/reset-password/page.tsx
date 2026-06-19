"use client";

import { useSearchParams } from "next/navigation";
import { Suspense } from "react";
import { AuthCard } from "@/components/auth/AuthCard";
import { ResetPasswordForm } from "@/components/auth/ResetPasswordForm";

function ResetPasswordContent() {
  const params = useSearchParams();
  const token = params.get("token");

  return (
    <AuthCard title="Restablecer contraseña" subtitle="Define una nueva contraseña para tu cuenta.">
      <ResetPasswordForm token={token} />
    </AuthCard>
  );
}

export default function ResetPasswordPage() {
  return (
    <Suspense fallback={null}>
      <ResetPasswordContent />
    </Suspense>
  );
}
