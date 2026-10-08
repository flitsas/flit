import type { Metadata } from "next";
import { CapturaManualFlow } from "./_components/CapturaManualFlow";

// Página PÚBLICA de captura manual (Épica #13202, Feature B). El token es la credencial; sin sesión.
// No reutiliza app/biometric/[token] (proveedor mock, usa <input type=file>).
export const metadata: Metadata = {
  title: "Verificación de identidad",
  robots: { index: false, follow: false },
};

export default async function CapturaManualPage({ params }: { params: Promise<{ token: string }> }) {
  const { token } = await params;
  return <CapturaManualFlow token={token} />;
}
