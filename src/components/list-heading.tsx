import { FC } from "react";
import { Roboto } from "next/font/google";
import Divider from "./divider";

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export interface ListHeadingProps {
  title: string;
}

const ListHeading: FC<ListHeadingProps> = ({ title }) => {
  return (
    <div className="w-full flex flex-col items-center gap-4 mb-6">
      <h2 className={`${robotoBold.className} text-3xl`}>{title}</h2>
      <Divider />
    </div>
  );
};

export default ListHeading;
