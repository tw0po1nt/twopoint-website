[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Robots

open System

let render (config: StageConfig) =
  let sitemapUrl = Uri(config.SiteUrl, "sitemap-index.xml")
  let sitemap = if config.Indexable then $"\nSitemap: {sitemapUrl}\n" else ""
  $"User-agent: *\nDisallow:\n{sitemap}"
