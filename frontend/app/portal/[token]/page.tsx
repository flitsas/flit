import { PortalParticipant } from './PortalParticipant';

/**
 * Página PÚBLICA del portal de participantes (Slice 7B). La parte abre el
 * magic-link con su token, acepta el consentimiento (Ley 1581), sube sus
 * documentos, firma (traspaso) y finaliza. Sin auth: el token es la
 * credencial. En Next.js 16 los params son asíncronos.
 */
export default async function PortalPage({
  params,
}: {
  params: Promise<{ token: string }>;
}) {
  const { token } = await params;
  return (
    <main className="min-h-full grid place-items-center px-4 py-10 bg-[#F5F7FA] dark:bg-[#0B0F14]">
      <PortalParticipant token={token} />
    </main>
  );
}
