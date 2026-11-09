import type { Metadata } from "next";
import localFont from "next/font/local";
import { isOidcSessionMode } from "@flit/auth/request";
import { SessionExpiredListener } from "@/components/auth/SessionExpiredListener";
import { BrandProvider } from "@/components/brand/BrandProvider";
import { BrandStyle } from "@/components/brand/BrandStyle";
import { resolveBrand } from "@/lib/brand/resolve-brand.server";
import "./globals.css";

// Las fuentes viven en el repo: el build no depende de lo que responda Google Fonts.
const poppins = localFont({
  variable: "--font-poppins",
  src: [
    { path: "./fonts/Poppins-400-latin.woff2", weight: "400", style: "normal" },
    { path: "./fonts/Poppins-500-latin.woff2", weight: "500", style: "normal" },
    { path: "./fonts/Poppins-600-latin.woff2", weight: "600", style: "normal" },
    { path: "./fonts/Poppins-700-latin.woff2", weight: "700", style: "normal" },
  ],
});

// Placa colombiana (UI kit «Diseño Inteligente», HU #14475): local, como Poppins.
const barlowCondensed = localFont({
  variable: "--font-barlow-condensed",
  src: "./fonts/BarlowCondensed-700-latin.woff2",
  weight: "700",
  style: "normal",
});

const jetbrainsMono = localFont({
  variable: "--font-jetbrains-mono",
  src: "./fonts/JetBrainsMono-variable-latin.woff2",
  weight: "100 800",
  style: "normal",
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
      // A-10 (HU #13001): modo de sesión en runtime (FLIT_SESSION_MODE), leído por lib/auth/session-mode.ts.
      data-session-mode={isOidcSessionMode() ? "oidc" : "legacy"}
      suppressHydrationWarning
      className={`${poppins.variable} ${barlowCondensed.variable} ${jetbrainsMono.variable} h-full`}
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
