import { Post } from "./post";

export const BLOB_STORAGE_BASE_URL = "https://twopointwebsite.blob.core.windows.net";

export const getAssetsDirectory = (post: Post) =>
  `${BLOB_STORAGE_BASE_URL}/${post.type === 'blog' ? 'posts' : 'talks'}/${post.topics[0].slug}/${post.slug}`;
