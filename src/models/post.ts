import { Topic } from './topic';

export interface Post {
  type: 'blog' | 'talk';
  slug: string;
  img: {
    type: 'icon' | 'fullsize';
    src: string;
    alt: string;
  };
  title: string;
  summary?: string;
  date: Date;
  topics: Topic[];
}
