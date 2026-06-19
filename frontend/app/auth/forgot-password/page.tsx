import { AuthCard } from "@/components/auth/AuthCard";
import { ForgotPasswordForm } from "@/components/auth/ForgotPasswordForm";

export default function ForgotPasswordPage() {
  return (
    <AuthCard
      title="Recuperar contraseña"
      subtitle="Ingresa tu correo y te enviaremos un enlace para restablecerla."
    >
      <ForgotPasswordForm />
    </AuthCard>
  );
}
