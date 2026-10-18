[<RequireQualifiedAccess>]
module TwoPoint.Site.Generator.Mount

open TwoPoint.Site.Widgets

open Oxpecker.ViewEngine
open System

/// `fallback` is the static HTML that stays when the widget script does not run.
let view (widget: Contract.Widget) (fallback: HtmlElement) =
  let mount =
    Contract.toAttributes widget
    |> List.fold (fun (element: div) (name, value) -> element.attr (name, value)) (div ())

  mount { fallback }

let private apiOrigin (config: StageConfig) = config.ApiUrl.GetLeftPart UriPartial.Authority

let subscribe (config: StageConfig) =
  view
    (Contract.Widget.Subscribe { Api = apiOrigin config })
    (p (class' = "subscribe-fallback") {
      "The subscribe form needs JavaScript. You can also follow the "
      a (href = "/rss.xml") { "RSS feed" }
      "."
    })
