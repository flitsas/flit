import Link from "next/link";
import { buttonClass } from "@flit/ui/Button";

// Con el fondo y la tipografía de la suite (propuesta «Diseño Inteligente», HU #14487): el body del hub no pinta fondo.
export default function NotFound() {
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-5 bg-[color:var(--di-surface)] px-6 py-16 text-center">
      <h1 className="text-2xl font-bold text-[color:var(--di-heading)]">Esta página no existe</h1>
      <Link href="/" className={buttonClass()}>
        Volver al inicio
      </Link>
    </main>
  );
}
