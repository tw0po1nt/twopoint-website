import { ByPostSlug } from "@/components/content/topics";
import Page from "@/components/page";

export default function TalkPage({ params }: { params: { slug: string } }) {
  const Post = ByPostSlug[params.slug];

  return (
    <Page backAction={{ title: "All talks", href: "/talks" }}>
      <Post.Content />
    </Page>
  );
}
