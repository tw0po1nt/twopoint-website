module TwoPoint.Admin.App

open TwoPoint.Site.Widgets

open Browser

let start (signIn: SignIn) (push: Push) =
  match document.getElementById "admin" with
  | null -> console.error "admin: the page has no element with the id 'admin'."
  | mount -> Mount.replace mount (fun () -> ShellView.view signIn push)
