import { FC } from 'react';
import { Topic } from '@/models/topic';
import { Post } from '@/models/post';
import * as swiftui from './swiftui';
import * as swift from './swift';
import * as csharp from './csharp';
import * as zcash from './zcash';
// import * as miscellaeous from './miscellaneous';

export const AllTopics: Topic[] = [
  swiftui.TopicSummary,
  swift.TopicSummary,
  csharp.TopicSummary,
  zcash.TopicSummary,
  // miscellaeous.TopicSummary,
];

export const AllPosts: Post[] = [
  ...swiftui.AllPosts,
  ...swift.AllPosts,
  ...csharp.AllPosts,
  ...zcash.AllPosts,
];

export const ByTopicSlug = {
  [swiftui.TopicSummary.slug]: {
    summary: swiftui.TopicSummary,
    posts: swiftui.AllPosts,
  },
  [swift.TopicSummary.slug]: {
    summary: swift.TopicSummary,
    posts: swift.AllPosts,
  },
  [csharp.TopicSummary.slug]: {
    summary: csharp.TopicSummary,
    posts: csharp.AllPosts,
  },
  [zcash.TopicSummary.slug]: {
    summary: zcash.TopicSummary,
    posts: zcash.AllPosts,
  },
  // [miscellaeous.TopicSummary.slug]: {
  //   summary: miscellaeous.TopicSummary,
  //   posts: miscellaeous.AllPosts,
  // },
};

export const ByPostSlug: { [slug: string]: { Summary: Post; Content: FC } } = {
  ...swiftui.PostsBySlug,
  ...swift.PostsBySlug,
  ...csharp.PostsBySlug,
  ...zcash.PostsBySlug
  // ...miscellaeous.PostsBySlug
};
