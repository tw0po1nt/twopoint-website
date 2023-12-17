import PostList from "@/components/post-list";
import { AllPosts } from "@/components/content/topics";
import Page from "@/components/page";

export default function LatestPosts() {
  const latestPosts = [...AllPosts].sort(
    (lhs, rhs) => lhs.date.getTime() - rhs.date.getTime()
  );

  return (
    <Page title="Latest posts">
      <section>
        <PostList posts={latestPosts} />
      </section>
    </Page>
  );
}
