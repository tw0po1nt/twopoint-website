[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Robots

open System

/// The text of `robots.txt`. Only an indexable stage names the sitemap.
let render (config: StageConfig) =
  let sitemapUrl = Uri(config.SiteUrl, "sitemap-index.xml")
  let sitemap = if config.Indexable then $"\nSitemap: {sitemapUrl}\n" else ""
  $"User-agent: *\nDisallow:\n{sitemap}"
