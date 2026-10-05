import { AuthCard } from "@/components/auth/AuthCard";
import { NewPasswordForm } from "@/components/auth/NewPasswordForm";

export const metadata = { title: "Activar cuenta" };

// Mismo enlace que envía hoy la invitación (/invite/activate?token=…).
export default async function ActivateAccountPage({ searchParams }: { searchParams: Promise<{ token?: string }> }) {
  const { token } = await searchParams;
  return (
    <AuthCard title="Activa tu cuenta" subtitle="Define tu contraseña para completar el registro." backHref={null}>
      <NewPasswordForm mode="activate" token={token ?? null} />
    </AuthCard>
  );
}
