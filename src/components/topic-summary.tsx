import { FC } from "react";
import { Roboto } from "next/font/google";
import Image from "next/image";
import Link from "next/link";
import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faCode } from "@fortawesome/free-solid-svg-icons";
import { Topic } from "@/models/topic";

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export interface TopicSummaryProps {
  topic: Topic;
}

const TopicSummary: FC<TopicSummaryProps> = ({ topic }) => {
  return (
    <Link href={`/topics/${topic.slug}`}>
      <article className="bg-white dark:bg-zinc-800 rounded-xl overflow-clip border border-slate-200 dark:border-zinc-700 drop-shadow hover:scale-[1.025] hover:transition-all duration-300 cursor-pointer">
        <header>
          <div className="flex items-center justify-center w-full h-32 bg-gradient-to-b from-red-500 dark:from-red-600 to-red-700 dark:to-red-800">
            {topic?.img?.src && topic.img.alt ? (
              <Image
                src={topic.img.src}
                alt={topic.img.alt}
                width={100}
                height={100}
              />
            ) : (
              <FontAwesomeIcon
                icon={faCode}
                size="4x"
                color="white"
              />
            )}
          </div>
        </header>
        <div className="p-4 flex flex-col gap-1">
          <p
            className={`${robotoBold.className} text-sm text-zinc-500 dark:text-zinc-400`}
          >
            TOPIC
          </p>
          <p className={`${robotoBold.className} text-xl`}>{topic.title}</p>
        </div>
      </article>
    </Link>
  );
};

export default TopicSummary;
