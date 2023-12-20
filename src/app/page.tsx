import { AllPosts, ByPostSlug } from "@/components/content/topics";
import ListHeading from "@/components/list-heading";
import Page from "@/components/page";
import PostList from "@/components/post-list";

const latestPosts = [...AllPosts].sort(
  (lhs, rhs) => rhs.date.getTime() - lhs.date.getTime()
);
const Post = latestPosts.length ? ByPostSlug[latestPosts[0].slug] : null;

export default function HomePage() {
  return (
    <Page>
      <ListHeading title="Latest post" />
      {Post ? <Post.Content /> : <p>TODO: Implement no posts placeholder</p>}
      <div className="py-16">
        <ListHeading title="More posts" />
        <PostList
          posts={latestPosts.filter((p) => p.slug !== Post?.Summary.slug).slice(0, 3)}
        />
      </div>
    </Page>
  );
}
