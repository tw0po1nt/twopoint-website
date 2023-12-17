import { FC } from "react";
import PostSummary from "./post-summary";
import { Post } from "@/models/post";

export interface PostListProps {
  posts: Post[];
}

const PostList: FC<PostListProps> = ({ posts }) => {
  return (
    <article className="grid max-[500px]:grid-cols-1 grid-cols-2 md:grid-cols-3 gap-4">
      {posts.map(p => <PostSummary key={p.slug} post={p} />)}
    </article>
  );
};

export default PostList;
