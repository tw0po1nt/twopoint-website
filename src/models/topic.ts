export interface Topic {
  slug: string;
  title: string;
  className?: string;
  img?: {
    type: 'icon' | 'fullsize';
    src: string;
    alt: string;
  };
}
