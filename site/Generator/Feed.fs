[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Feed

open System
open System.Globalization

let urlPath = "/rss.xml"

let title = $"TwoPoint{char 0x2019}s Blog"

/// The backend reads this shape to find new posts, and takes the slug from the path of each `link`.
let render (config: StageConfig) (posts: Post list) =
  XmlFile.write (fun writer ->
    writer.WriteStartElement "rss"
    writer.WriteAttributeString("version", "2.0")
    writer.WriteStartElement "channel"
    writer.WriteElementString("title", title)
    writer.WriteElementString("description", config.Description)
    writer.WriteElementString("link", config.SiteUrl.AbsoluteUri)

    for post in Blog.newestFirst posts do
      let link = Uri(config.SiteUrl, Post.urlPath post).AbsoluteUri
      writer.WriteStartElement "item"
      writer.WriteElementString("title", post.FrontMatter.Title)
      writer.WriteElementString("link", link)
      writer.WriteStartElement "guid"
      writer.WriteAttributeString("isPermaLink", "true")
      writer.WriteString link
      writer.WriteEndElement()
      writer.WriteElementString("description", post.FrontMatter.Excerpt)
      writer.WriteElementString("pubDate", post.FrontMatter.PublishDate.UtcDateTime.ToString("r", CultureInfo.InvariantCulture))
      writer.WriteEndElement()

    writer.WriteEndElement()
    writer.WriteEndElement())
