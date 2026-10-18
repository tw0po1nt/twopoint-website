/// Runs the pure `update` of a widget on a Solid signal.
[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mvu

open Oxpecker.Solid

/// `perform` runs each effect that `update` gives, and sends its result back with the dispatch function.
let run (init: 'Model) (update: 'Msg -> 'Model -> 'Model * 'Effect option) (perform: 'Effect -> ('Msg -> unit) -> unit) =
  let model, setModel = createSignal init

  let rec dispatch (msg: 'Msg) =
    let next, effect = update msg (model ())
    setModel next
    effect |> Option.iter (fun effect -> perform effect dispatch)

  model, dispatch
