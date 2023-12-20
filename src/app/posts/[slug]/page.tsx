import { ByPostSlug } from "@/components/content/topics";
import Page from "@/components/page";
import { AllPosts } from "@/components/content/topics";

export async function generateMetadata({ params }: { params: { slug: string } }) {
  const Post = ByPostSlug[params.slug];
  return {
    title: Post.Summary.title,
  }
}

export async function generateStaticParams() { 
  return AllPosts.map((post) => ({
    slug: post.slug,
  }));
}

export default function PostPage({ params }: { params: { slug: string } }) {
  const Post = ByPostSlug[params.slug];
  const PrimaryTopic = Post.Summary.topics[0];

  return (
    <Page
      backAction={{
        title: `All ${PrimaryTopic.title}`,
        href: `/topics/${PrimaryTopic.slug}`,
      }}
    >
      <Post.Content />
    </Page>
  );
}
