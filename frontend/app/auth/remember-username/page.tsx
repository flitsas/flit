import { AuthCard } from "@/components/auth/AuthCard";
import { RememberUsernameForm } from "@/components/auth/RememberUsernameForm";

export default function RememberUsernamePage() {
  return (
    <AuthCard
      title="Recordar usuario"
      subtitle="Ingresa tu número de documento y te enviaremos tu usuario al correo registrado."
    >
      <RememberUsernameForm />
    </AuthCard>
  );
}
