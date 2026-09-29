import type { Metadata } from "next";
import localFont from "next/font/local";
import { Providers } from "./providers";
import "./globals.css";

// Vendored locally (public/fonts) — no network access needed at build time, unlike
// next/font/google, which hit a real, currently-unresolved Turbopack bug fetching from
// Googles CDN during the Docker build. Paths are relative to THIS file (app/layout.tsx),
// so reaching public/ (a sibling of app/) needs "../public/...".
//
// Filenames below assume Google Fonts&apos; standard static-export naming — confirm against
// `dir web\public\fonts` and adjust any that dont match exactly; a wrong filename here
// fails the build immediately and obviously, so its cheap to fix once spotted.
const spaceGrotesk = localFont({
  src: [
    { path: "../../public/fonts/SpaceGrotesk-Medium.ttf", weight: "500", style: "normal" },
    { path: "../../public/fonts/SpaceGrotesk-SemiBold.ttf", weight: "600", style: "normal" },
    { path: "../../public/fonts/SpaceGrotesk-Bold.ttf", weight: "700", style: "normal" },
  ],
  variable: "--font-space-grotesk",
  display: "swap",
});

const ibmPlexSans = localFont({
  src: [
    { path: "../../public/fonts/IBMPlexSans-Regular.ttf", weight: "400", style: "normal" },
    { path: "../../public/fonts/IBMPlexSans-Medium.ttf", weight: "500", style: "normal" },
    { path: "../../public/fonts/IBMPlexSans-SemiBold.ttf", weight: "600", style: "normal" },
  ],
  variable: "--font-ibm-plex-sans",
  display: "swap",
});

export const metadata: Metadata = {
  title: "Todo Planner",
  description: "Plan today&apos;s jobs — for gardeners working across multiple properties",
};

export default function RootLayout({
  children,
}: Readonly<{ children: React.ReactNode }>) {
  return (
    <html lang="en" className={`${spaceGrotesk.variable} ${ibmPlexSans.variable}`}>
      <body className="font-sans antialiased">
        <Providers>{children}</Providers>
      </body>
    </html>
  );
}
