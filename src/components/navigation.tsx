"use client";
import {
  FC,
  MouseEventHandler,
  useCallback,
  useMemo,
  useState,
} from "react";
import Link from "next/link";
import { Roboto, Roboto_Mono } from "next/font/google";
import { usePathname, useRouter } from "next/navigation";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faCode, faXmark } from "@fortawesome/free-solid-svg-icons";

const robotoMono = Roboto_Mono({ subsets: ["latin"], weight: "700" });
const roboto = Roboto({ subsets: ["latin"], weight: "500" });

const Navigation: FC = () => {
  const pathname = usePathname();
  const router = useRouter();
  const [isMobileMenuHidden, setIsMobileMenuHidden] = useState(true);
  const baseMobileMenuClass =
    "absolute left-0 top-0 right-0 bottom-0 bg-slate-100 dark:bg-zinc-900 z-[1]";
  const mobileMenuClass = useMemo(
    () =>
      isMobileMenuHidden
        ? `${baseMobileMenuClass} hidden`
        : baseMobileMenuClass,
    [isMobileMenuHidden]
  );

  const handleMenuButtonClick: MouseEventHandler<HTMLButtonElement> = () => {
    setIsMobileMenuHidden((prev) => !prev);
  };

  const navigate = useCallback(
    (href: string) => {
      setIsMobileMenuHidden(true);
      router.push(href);
    },
    [router]
  );

  return (
    <nav className="flex flex-row justify-between max-w-6xl mx-auto bg-white dark:bg-black p-8">
      <Link
        href="/"
        className={`${robotoMono.className} flex flex-row items-center gap-2 text-2xl`}
      >
        Two
        <FontAwesomeIcon
          icon={faCode}
          className="text-red-600 dark:text-red-500"
        />
        Point.dev
      </Link>
      <ul
        className={`${roboto.className} hidden md:flex flex-row items-center gap-4 text-xl`}
      >
        <NavButton title="Topics" href="/topics" isActive={pathname.includes("/topics")} />
        <NavButton title="Talks" href="/talks" isActive={pathname.includes("/talks")} />
      </ul>
      <div className="md:hidden flex items-center">
        <button className="outline-none" onClick={handleMenuButtonClick}>
          <svg
            className="w-6 h-6 text-black dark:text-white"
            x-show="!showMenu"
            fill="none"
            strokeLinecap="round"
            strokeLinejoin="round"
            strokeWidth="2"
            viewBox="0 0 24 24"
            stroke="currentColor"
          >
            <path d="M4 6h16M4 12h16M4 18h16"></path>
          </svg>
        </button>
      </div>
      <div className={mobileMenuClass}>
        <header className="w-full flex flex-row-reverse">
          <button onClick={handleMenuButtonClick}>
            <FontAwesomeIcon
              size="2x"
              icon={faXmark}
              className="pt-8 pr-8 text-black dark:text-white"
            />
          </button>
        </header>
        <ul className="pt-4">
          <MobileNavButton title="Home" onClick={() => navigate("/")} isActive={pathname === "/"} />
          <MobileNavButton title="Topics" onClick={() => navigate("/topics")} isActive={pathname.includes("/topics")} />
          <MobileNavButton title="Talks" onClick={() => navigate("/talks")} isActive={pathname.includes("/talks")} />
        </ul>
      </div>
    </nav>
  );
};

interface NavButtonProps {
  title: string;
  href: string;
  isActive: boolean;
}
const NavButton: FC<NavButtonProps> = ({ title, href, isActive }) => {
  return (
    <li>
      {isActive ? (
        <Link href={href}>
          {title}
          <span className="block h-0.5 bg-red-600 dark:bg-red-500"></span>
        </Link>
      ) : (
        <Link href={href} className="group transition duration-300">
          {title}
          <span className="block opacity-0 group-hover:opacity-100 transition-all duration-250 h-0.5 bg-red-600 dark:bg-red-500"></span>
        </Link>
      )}
    </li>
  );
};

interface MobileNavButtonProps {
  title: string;
  onClick: () => void;
  isActive: boolean;
}
const MobileNavButton: FC<MobileNavButtonProps> = ({ title, onClick, isActive }) => {
  const bgColorClassName = isActive ? "bg-red-600 dark:bg-red-500" : "";

  return (
    <li>
      <button
        onClick={onClick}
        className={`${roboto.className} ${bgColorClassName} w-full px-8 py-4 text-black dark:text-white text-left hover:text-white hover:bg-red-600 dark:hover:bg-red-500 text-xl`}
      >
        {title}
      </button>
    </li>
  );
};

export default Navigation;
