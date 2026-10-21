import Link from "next/link";

export default function NotFound() {
  return (
    <main className="flex min-h-full flex-col items-center justify-center gap-4 px-6 py-16 text-center">
      <h1 className="text-2xl font-semibold text-flit-primary">Esta página no existe</h1>
      <Link href="/" className="text-flit-brand underline">
        Volver al inicio
      </Link>
    </main>
  );
}
