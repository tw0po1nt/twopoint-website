import { ByPostSlug } from "@/components/content/topics";
import Page from "@/components/page";

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
