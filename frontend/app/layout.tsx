import type { Metadata } from "next";
import { JetBrains_Mono, Poppins } from "next/font/google";
import { SessionExpiredListener } from "@/components/auth/SessionExpiredListener";
import { BrandProvider } from "@/components/brand/BrandProvider";
import { BrandStyle } from "@/components/brand/BrandStyle";
import { resolveBrand } from "@/lib/brand/resolve-brand.server";
import "./globals.css";

const poppins = Poppins({
  variable: "--font-poppins",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

const jetbrainsMono = JetBrains_Mono({
  variable: "--font-jetbrains-mono",
  subsets: ["latin"],
  weight: ["400", "500", "700"],
});

// HU #12419 (AC2/AC7) — en host FLIT `resolveBrand()` devuelve `FLIT_BRAND` SIN fetch, así que
// esta metadata sale IDÉNTICA a la de siempre (title "FLIT 2.0", sin `icons`: `brand.logoUrl` es
// `null`). En un dominio de red, el título y el favicon pasan a ser los de la marca resuelta.
export async function generateMetadata(): Promise<Metadata> {
  const brand = await resolveBrand();
  return {
    title: brand.platformName,
    description: "Plataforma de trámites vehiculares",
    icons: brand.logoUrl ? { icon: brand.logoUrl } : undefined,
  };
}

export default async function RootLayout({
  children,
}: Readonly<{
  children: React.ReactNode;
}>) {
  const brand = await resolveBrand();

  return (
    <html
      lang="es"
      suppressHydrationWarning
      className={`${poppins.variable} ${jetbrainsMono.variable} h-full`}
    >
      {/* BrandStyle no renderiza nada en host FLIT (AC7): <head> queda igual que hoy. */}
      <head>
        <BrandStyle brand={brand} />
      </head>
      <body suppressHydrationWarning className="h-full font-sans antialiased">
        <BrandProvider brand={brand}>{children}</BrandProvider>
        <SessionExpiredListener />
      </body>
    </html>
  );
}
