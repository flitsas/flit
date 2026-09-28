import type { Metadata } from "next";
import { Poppins } from "next/font/google";
import { BrandProvider } from "@flit/brand/BrandProvider";
import { BrandStyle } from "@flit/brand/BrandStyle";
import { resolveBrand } from "@flit/brand/resolve-brand.server";
import "./globals.css";

const poppins = Poppins({
  variable: "--font-poppins",
  subsets: ["latin"],
  weight: ["400", "500", "600", "700"],
});

// La marca sale del host de la petición (Marca Blanca, ADR-0060 §D5): en un host FLIT no se llama a nadie; en el
// dominio de una red, título, favicon y colores son los de la red. Se resuelve en el servidor, sin destello.
export async function generateMetadata(): Promise<Metadata> {
  const brand = await resolveBrand();
  return {
    title: brand.platformName,
    description: "Productos de la plataforma en un solo lugar",
    icons: brand.logoUrl ? { icon: brand.logoUrl } : undefined,
  };
}

export default async function RootLayout({ children }: Readonly<{ children: React.ReactNode }>) {
  const brand = await resolveBrand();

  return (
    <html lang="es" suppressHydrationWarning className={`${poppins.variable} h-full`}>
      <head>
        <BrandStyle brand={brand} />
      </head>
      <body suppressHydrationWarning className="h-full font-sans antialiased">
        <BrandProvider brand={brand}>{children}</BrandProvider>
      </body>
    </html>
  );
}
