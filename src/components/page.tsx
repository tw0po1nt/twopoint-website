"use client";
import { FC, PropsWithChildren, useCallback } from "react";
import { useRouter } from "next/navigation";
import ListHeading from "./list-heading";
import BackButton from "./back-button";

export interface PageProps {
  title?: string;
  backAction?: {
    title: string;
    href: string;
  };
}

const Page: FC<PropsWithChildren<PageProps>> = ({
  backAction,
  children,
  title,
}) => {
  const router = useRouter();

  const navigate = useCallback(
    (href: string) => {
      router.push(href);
    },
    [router]
  );

  return (
    <div className="flex flex-col items-center justify-between px-8 bg-slate-100 dark:bg-zinc-900">
      <main className={`min-h-screen w-full max-w-6xl ${backAction ? "md:pt-8" : "pt-8"}`}>
        {backAction && (
          <header className="w-full py-4 md:hidden">
            <div className="w-full flex items-center">
              <BackButton onClick={() => navigate(backAction.href)}>
                {backAction.title}
              </BackButton>
            </div>
          </header>
        )}
        {title && <ListHeading title={title} />}
        {children}
      </main>
    </div>
  );
};

export default Page;
