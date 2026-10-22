import { AuthCard } from "@/components/auth/AuthCard";
import { NewPasswordForm } from "@/components/auth/NewPasswordForm";

export const metadata = { title: "Restablecer contraseña" };

// Mismo enlace que envía hoy el correo de recuperación (/auth/reset-password?token=…): al mudarse la raíz al hub
// (A-11), los enlaces viejos siguen funcionando.
export default async function ResetPasswordPage({ searchParams }: { searchParams: Promise<{ token?: string }> }) {
  const { token } = await searchParams;
  return (
    <AuthCard title="Restablecer contraseña" subtitle="Define tu nueva contraseña.">
      <NewPasswordForm mode="reset" token={token ?? null} />
    </AuthCard>
  );
}
