import { FontAwesomeIcon } from "@fortawesome/react-fontawesome";
import { faSmile } from "@fortawesome/free-regular-svg-icons";
import { ByTopicSlug } from "@/components/content/topics";
import PostList from "@/components/post-list";
import Page from "@/components/page";

export default function TopicPage({ params }: { params: { slug: string } }) {
  const { summary, posts } = ByTopicSlug[params.slug];

  return (
    <Page title={summary.title} backAction={{ title: "All topics", href: "/topics" }}>
      {posts.length ? (
        <PostList posts={posts} />
      ) : (
        <div className="flex flex-col items-center justify-center gap-4 text-zinc-500 dark:text-zinc-400">
          <p className="text-xl">{`No posts about ${summary.title}...yet`}</p>
          <FontAwesomeIcon size="5x" icon={faSmile} />
          <p className="text-xl">Check back soon!</p>
        </div>
      )}
    </Page>
  );
}
