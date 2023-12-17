import { Post } from '@/models/post';
import * as NighthawkZcon4 from './posts/nighthawks-roadmap-for-zcash-with-aditya-bharadwaj-and-matthew-watt-zcon4';

export { TopicSummary } from './topic';

export const AllPosts: Post[] = [
  NighthawkZcon4.PostSummary,
];

export const PostsBySlug = {
  [NighthawkZcon4.PostSummary.slug]: {
    Summary: NighthawkZcon4.PostSummary,
    Content: NighthawkZcon4.PostContent,
  },
};
