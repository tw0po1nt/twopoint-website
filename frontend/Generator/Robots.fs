[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Robots

let render (config: StageConfig) (sitemapUrlPath: string) =
  let sitemap = if config.Indexable then $"\nSitemap: {Stage.url config sitemapUrlPath}\n" else ""
  $"User-agent: *\nDisallow:\n{sitemap}"
