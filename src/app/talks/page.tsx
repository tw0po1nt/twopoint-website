import PostList from "@/components/post-list";
import { AllPosts } from "@/components/content/topics";
import Page from "@/components/page";
import { Metadata } from "next";

export const metadata: Metadata = {
  title: 'All talks',
}

export default function LatestPosts() {
  const latestPosts = [...AllPosts]
    .filter((p) => p.type === "talk")
    .sort((lhs, rhs) => lhs.date.getTime() - rhs.date.getTime());

  return (
    <Page title="All talks">
      <section>
        <PostList posts={latestPosts} />
      </section>
    </Page>
  );
}
