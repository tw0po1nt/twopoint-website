module TwoPoint.Admin.Main

open TwoPoint.Site.Widgets

open Browser

match document.getElementById "admin" with
| null -> console.error "admin: the page has no element with the id 'admin'."
| mount -> Mount.replace mount (fun () -> ShellView.view ())
