import { Post } from '@/models/post';
import * as SecurePdf from './posts/swiftui-password-protected-pdf';

export { TopicSummary } from './topic';

export const AllPosts: Post[] = [
  SecurePdf.PostSummary,
];

export const PostsBySlug = {
  [SecurePdf.PostSummary.slug]: {
    Summary: SecurePdf.PostSummary,
    Content: SecurePdf.PostContent,
  },
};
