import { FC } from "react";

export interface DividerProps {
  className?: string;
}

const Divider: FC<DividerProps> = ({ className }) => {
  return <span className={`block w-1/6 h-0.5 bg-slate-300 dark:bg-zinc-700 ${className ?? ""}`}></span>;
};

export default Divider;
