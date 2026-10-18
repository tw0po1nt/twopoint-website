[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mvu

open Oxpecker.Solid

let run (init: 'Model) (update: 'Msg -> 'Model -> 'Model * 'Effect option) (perform: 'Effect -> ('Msg -> unit) -> unit) =
  let model, setModel = createSignal init

  let rec dispatch (msg: 'Msg) =
    let next, effect = update msg (model ())
    setModel next
    effect |> Option.iter (fun effect -> perform effect dispatch)

  model, dispatch
