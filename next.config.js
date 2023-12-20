/** @type {import('next').NextConfig} */
const nextConfig = {
  output: 'export',
  images: {
    remotePatterns: [
      {
        protocol: 'https',
        hostname: 'twopointwebsite.blob.core.windows.net',
        port: '',
        pathname: '**',
      },
    ],
  }
}

module.exports = nextConfig
