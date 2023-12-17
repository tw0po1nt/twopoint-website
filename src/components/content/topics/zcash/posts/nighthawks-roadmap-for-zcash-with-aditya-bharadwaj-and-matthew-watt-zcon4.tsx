import { FC } from "react";
import { format } from "date-fns";
import { Roboto } from "next/font/google";
import { Post } from "@/models/post";
import { TopicSummary } from "../topic";
import Divider from "@/components/divider";
import PostContentHeading from "@/components/post-content-heading";

const robotoBold = Roboto({ subsets: ["latin"], weight: "700" });

export const PostSummary: Post = {
  type: "talk",
  slug: "nighthawks-roadmap-for-zcash-with-aditya-bharadwaj-and-matthew-watt-zcon4",
  title:
    "Nighthawk's Roadmap for Zcash with Aditya Bharadwaj and Matthew Watt: Zcon4",
  img: {
    type: "fullsize",
    src: "/img/thumbnails/nighthawkroadmaptalk.jpg",
    alt: "Thumbnail of Aditya and Matt's talk at Zcon4",
  },
  date: new Date(),
  topics: [TopicSummary],
};

export const PostContent: FC = () => {
  return (
    <main className="min-h-screen w-full max-w-6xl">
      <article className="flex flex-col min-h-screen w-full">
        <section className="mb-8">
          <iframe
            src="https://www.youtube.com/embed/qRuKTkDvAk4"
            allowFullScreen
            allow="accelerometer; autoplay; clipboard-write; encrypted-media; gyroscope; picture-in-picture"
            className="w-full aspect-video"
          ></iframe>
        </section>
        <PostContentHeading summary={PostSummary} />
        <Divider className="my-4" />
        <section>
          <header>
            <p>
              A roadmap for Zcash by Nighthawk Apps, expanding on the roadmap
              shared in January 2023 and benefits of delivering a mobile-first
              experience to end-users.
            </p>
          </header>
        </section>
      </article>
    </main>
  );
};
