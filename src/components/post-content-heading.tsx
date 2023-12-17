import { FC } from "react";
import { Roboto } from "next/font/google";
import { Post } from "@/models/post";
import { format } from "date-fns";

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export interface PostContentHeadingProps {
  summary: Post;
  className?: string;
}

const PostContentHeading: FC<PostContentHeadingProps> = ({ className, summary }) => {
  return (
    <section className={className}>
      <header className="flex flex-col gap-2">
        <h1 className={`${robotoBold.className} text-3xl`}>{summary.title}</h1>
        <p
          className={`${robotoBold.className} text-lg text-zinc-500 dark:text-zinc-400`}
        >
          {summary.type.toUpperCase()} ·{" "}
          {format(summary.date, "dd MMM yyyy").toUpperCase()}
        </p>
      </header>
    </section>
  );
};

export default PostContentHeading;
