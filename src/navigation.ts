import { getPermalink, getBlogPermalink } from './utils/permalinks';

export const headerData = {
  links: [
    {
      text: 'Blog',
      href: getBlogPermalink(),
      links: [
        {
          text: 'All Posts',
          href: getBlogPermalink(),
        },
        {
          text: 'One-offs',
          href: getPermalink('one-offs', 'category'),
        },
        {
          text: 'Series',
          href: getPermalink('series'),
        },
        {
          text: 'Talks',
          href: getPermalink('talks', 'category'),
        },
      ],
    },
    {
      text: 'About',
      href: getPermalink('/about'),
    },
  ],
  actions: [],
};

export const footerData = {
  links: [],
  secondaryLinks: [],
  socialLinks: [
    { ariaLabel: 'X', icon: 'tabler:brand-x', href: 'https://x.com/tw0po1nt' },
    { ariaLabel: 'Github', icon: 'tabler:brand-github', href: 'https://github.com/tw0po1nt' },
  ],
  footNote: `
    <a class="flex flex-row items-center font-mono font-bold text-xl dark:text-white">Two&nbsp;<svg xmlns="http://www.w3.org/2000/svg" class="text-red-600 dark:text-red-500" width="1em" height="1em" viewBox="0 0 24 24">
	  <path fill="none" stroke="currentColor" stroke-linecap="round" stroke-linejoin="round" stroke-width="2" d="m7 8l-4 4l4 4m10-8l4 4l-4 4M14 4l-4 16" />
    </svg> &nbsp;Point</a>
  `,
};
