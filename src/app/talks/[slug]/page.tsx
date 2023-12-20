import { AllPosts, ByPostSlug } from "@/components/content/topics";
import Page from "@/components/page";

export async function generateMetadata({ params }: { params: { slug: string } }) {
  const Post = ByPostSlug[params.slug];
  return {
    title: Post.Summary.title,
  }
}

export async function generateStaticParams() {
  const latestPosts = [...AllPosts]
    .filter((p) => p.type === "talk")
    .sort((lhs, rhs) => lhs.date.getTime() - rhs.date.getTime());

  return latestPosts.map((post) => ({
    slug: post.slug,
  }));
}

export default function TalkPage({ params }: { params: { slug: string } }) {
  const Post = ByPostSlug[params.slug];

  return (
    <Page backAction={{ title: "All talks", href: "/talks" }}>
      <Post.Content />
    </Page>
  );
}
