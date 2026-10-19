[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.XmlFile

open System.IO
open System.Text
open System.Xml

/// Gives the document as UTF-8 with no byte order mark.
let write (body: XmlWriter -> unit) =
  use stream = new MemoryStream()

  do
    use writer = XmlWriter.Create(stream, XmlWriterSettings(Encoding = UTF8Encoding false, Indent = true))
    writer.WriteStartDocument()
    body writer
    writer.WriteEndDocument()

  stream.ToArray()
