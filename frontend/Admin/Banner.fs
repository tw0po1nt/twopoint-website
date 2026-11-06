[<RequireQualifiedAccess>]
module TwoPoint.Admin.Banner

open Oxpecker.Solid

[<SolidComponent>]
let private bannerWith (text: string) (action: string) (disabled: bool) (click: unit -> unit) =
  Parts
    .Alert(tone = Parts.Warn, extra = "banner")
    .Has(
      Fragment() {
        p () { text }

        Parts
          .Button(kind = Parts.Primary, size = Parts.Small, disabled = disabled, onClick = fun _ -> click ())
          .Has(Fragment() { action })
      }
    )

[<SolidComponent>]
let view (dispatch: Shell.Msg -> unit) (model: Session.Model<Shell.Call>) =
  let session = Shell.Session >> dispatch

  match model.Status with
  | Session.Broken ->
    Fragment() { bannerWith "Sign-in failed." "Sign in" false (fun () -> session Session.SignInClicked) }
  | Session.Refused ->
    Fragment() {
      bannerWith (Session.bannerText model) "Use another account" false (fun () -> session Session.SignInClicked)
    }
  | _ ->
    Fragment() {
      Show(when' = Session.showsBanner model) {
        bannerWith (Session.bannerText model) "Sign in again" (model.Status = Session.Renewing) (fun () ->
          session Session.SignInAgainClicked)
      }
    }
