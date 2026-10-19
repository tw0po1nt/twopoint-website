[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Sitemap

open System.Xml

let indexUrlPath = "/sitemap-index.xml"

let urlPath = "/sitemap-0.xml"

let private xmlns = "http://www.sitemaps.org/schemas/sitemap/0.9"

let private loc (config: StageConfig) (writer: XmlWriter) (urlPath: string) =
  writer.WriteElementString("loc", xmlns, Layout.url config urlPath)

let renderIndex (config: StageConfig) =
  XmlFile.write (fun writer ->
    writer.WriteStartElement("sitemapindex", xmlns)
    writer.WriteStartElement("sitemap", xmlns)
    loc config writer urlPath
    writer.WriteEndElement()
    writer.WriteEndElement())

/// `pages` gives the URL path and the robots rule of each page. Only the robots rule decides the entries, so each
/// stage lists the same pages.
let render (config: StageConfig) (pages: (string * Layout.Robots) list) =
  XmlFile.write (fun writer ->
    writer.WriteStartElement("urlset", xmlns)

    for urlPath, robots in List.sortBy fst pages do
      if robots = Layout.Robots.Index then
        writer.WriteStartElement("url", xmlns)
        loc config writer urlPath
        writer.WriteEndElement()

    writer.WriteEndElement())
