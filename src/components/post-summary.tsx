import { FC } from "react";
import { Roboto } from "next/font/google";
import Image from "next/image";
import Link from "next/link";
import { format } from "date-fns";
import { Post } from "@/models/post";

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export interface PostSummaryProps {
  post: Post;
}

const PostSummary: FC<PostSummaryProps> = ({ post }) => {
  return (
    <Link href={`/${post.type === 'blog' ? 'post' : 'talk'}s/${post.slug}`}>
      <article className="bg-white dark:bg-zinc-800 rounded-xl overflow-clip border border-slate-200 dark:border-zinc-700 drop-shadow hover:scale-[1.025] hover:transition-all duration-300 cursor-pointer">
        <header>
          {post.img.type === "icon" ? (
            <div className="flex items-center justify-center w-full h-32 bg-gradient-to-b from-red-500 dark:from-red-600 to-red-700 dark:to-red-800">
              <Image
                unoptimized
                src={post.img.src}
                alt={post.img.alt}
                width={100}
                height={100}
              />
            </div>
          ) : (
            <Image
              unoptimized
              src={post.img.src}
              alt={post.img.alt}
              width="0"
              height="0"
              sizes="100vw"
              className="w-full h-auto"
            />
          )}
        </header>
        <div className="p-4 flex flex-col gap-2">
          <p
            className={`${robotoBold.className} text-sm text-zinc-500 dark:text-zinc-400`}
          >
            {post.type.toUpperCase()} ·{" "}
            {format(post.date, "dd MMM yyyy").toUpperCase()}
          </p>
          <p className={`${robotoBold.className} text-xl`}>{post.title}</p>
          <div className="flex flex-row">
            {post.topics.map((t) => (
              <p
                key={t.slug}
                className={`py-1 px-2 text-xs border rounded-full dark:text-white ${
                  t.className ?? "text-red-600 border-red-600 dark:border-white"
                }`}
              >
                {t.title}
              </p>
            ))}
          </div>
        </div>
      </article>
    </Link>
  );
};

export default PostSummary;
