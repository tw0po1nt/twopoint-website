import { Roboto } from "next/font/google";
import { AllPosts, ByPostSlug } from "@/components/content/topics";
import ListHeading from "@/components/list-heading";
import Page from "@/components/page";

const latestPosts = [...AllPosts].sort(
  (lhs, rhs) => lhs.date.getTime() - rhs.date.getTime()
);
const Post = latestPosts.length ? ByPostSlug[latestPosts[0].slug] : null;

export default function HomePage({ params }: { params: { slug: string } }) {
  return (
    <Page>
      <ListHeading title="Latest post" />
      {Post ? <Post.Content /> : <p>TODO: Implement no posts placeholder</p>}
    </Page>
  );
}
