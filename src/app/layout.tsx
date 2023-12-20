import type { Metadata, Viewport } from "next";
import { Roboto } from "next/font/google";
import { config } from "@fortawesome/fontawesome-svg-core";
import "@fortawesome/fontawesome-svg-core/styles.css";
config.autoAddCss = false;
import Navigation from "@/components/navigation";
import "./globals.css";

const roboto = Roboto({ subsets: ["latin"], weight: "400" });

export const metadata: Metadata = {
  title: "twopoint.dev",
  description: "Yet another technical blog",
};

export const viewport: Viewport = {
  initialScale: 1.0,
  width: "device-width",
  maximumScale: 1.0,
  userScalable: false,
};

export default function RootLayout({
  children,
}: {
  children: React.ReactNode;
}) {
  return (
    <html lang="en">
      <body
        className={`${roboto.className} text-lg bg-white dark:bg-black overflow-x-hidden`}
      >
        <Navigation />
        {children}
      </body>
    </html>
  );
}
