[<RequireQualifiedAccess>]
module TwoPoint.Site.Widgets.Mvu

open Oxpecker.Solid

/// `around` gets the model before and after a message, and the commit of the new model. It can call the
/// commit later. If the model changed in the meantime, the commit applies the message to the new model.
let runAround
  (around: 'Model -> 'Model -> (unit -> unit) -> unit)
  (init: 'Model * 'Effect option)
  (update: 'Msg -> 'Model -> 'Model * 'Effect option)
  (perform: 'Effect -> ('Msg -> unit) -> unit)
  =
  let initModel, initEffect = init
  let model, setModel = createSignal initModel

  let rec dispatch (msg: 'Msg) =
    let current = model ()
    let next, effect = update msg current

    let commit () =
      let next, effect = if obj.ReferenceEquals(model (), current) then next, effect else update msg (model ())
      setModel next
      effect |> Option.iter (fun effect -> perform effect dispatch)

    around current next commit

  initEffect |> Option.iter (fun effect -> perform effect dispatch)
  model, dispatch

let run
  (init: 'Model * 'Effect option)
  (update: 'Msg -> 'Model -> 'Model * 'Effect option)
  (perform: 'Effect -> ('Msg -> unit) -> unit)
  =
  runAround (fun _ _ commit -> commit ()) init update perform
