import { AuthCard } from "@/components/auth/AuthCard";
import { ActivateAccountForm } from "@/components/auth/ActivateAccountForm";

export const metadata = { title: "Activar cuenta — FLIT" };

interface PageProps {
  searchParams: Promise<{ token?: string }>;
}

export default async function ActivateAccountPage({ searchParams }: PageProps) {
  const { token } = await searchParams;

  return (
    <AuthCard
      title="Activa tu cuenta"
      subtitle="Define tu contraseña para completar el registro."
      backHref={null}
    >
      <ActivateAccountForm token={token ?? null} />
    </AuthCard>
  );
}
