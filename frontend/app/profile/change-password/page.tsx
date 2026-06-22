import { AuthCard } from "@/components/auth/AuthCard";
import { ChangePasswordForm } from "@/components/auth/ChangePasswordForm";

export default function ChangePasswordPage() {
  return (
    <AuthCard title="Cambiar contraseña" subtitle="Actualiza tu contraseña desde tu perfil." backHref="/">
      <ChangePasswordForm />
    </AuthCard>
  );
}
