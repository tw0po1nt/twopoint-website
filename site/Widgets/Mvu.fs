[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mvu

open Oxpecker.Solid

let run
  (init: 'Model * 'Effect option)
  (update: 'Msg -> 'Model -> 'Model * 'Effect option)
  (perform: 'Effect -> ('Msg -> unit) -> unit)
  =
  let initModel, initEffect = init
  let model, setModel = createSignal initModel

  let rec dispatch (msg: 'Msg) =
    let next, effect = update msg (model ())
    setModel next
    effect |> Option.iter (fun effect -> perform effect dispatch)

  initEffect |> Option.iter (fun effect -> perform effect dispatch)
  model, dispatch
