import { BiometricCapture } from './BiometricCapture';

/**
 * Página PÚBLICA de captura biométrica (Slice 6). El participante abre el
 * magic-link con su token y sube las 3 fotos. Sin auth: el token es la
 * credencial. En Next.js 16 los params son asíncronos.
 */
export default async function BiometricCapturePage({
  params,
}: {
  params: Promise<{ token: string }>;
}) {
  const { token } = await params;
  return (
    <main className="min-h-full grid place-items-center px-4 py-10 bg-[#F5F7FA] dark:bg-[#0B0F14]">
      <BiometricCapture token={token} />
    </main>
  );
}
