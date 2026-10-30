[<RequireQualifiedAccess>]
module TwoPoint.Admin.SeriesFormView

open Browser
open Browser.Types
open Oxpecker.Solid
open Oxpecker.Solid.Aria

let private pickImage (onPicked: File -> unit) =
  let input = document.createElement "input" :?> HTMLInputElement
  input.``type`` <- "file"
  input.accept <- "image/jpeg,image/png,image/webp,image/gif"

  input.addEventListener (
    "change",
    fun _ ->
      if input.files.length > 0 then
        onPicked input.files[0]
  )

  input.click ()

let private inputText (event: Event) = (event.target :?> HTMLInputElement).value

[<SolidComponent>]
let private error (text: unit -> string) =
  Show(when' = (text () <> "")) { p (class' = "err", role = "alert") { text () } }

/// `others` gives the slugs that the series cannot have. `prefix` keeps the IDs of two forms apart.
[<SolidComponent>]
let fields
  (prefix: string)
  (send: SeriesForm.Msg -> unit)
  (form: unit -> SeriesForm.Form option)
  (others: unit -> string list)
  =
  // A read of the form in the body of a component would build the form again at each change, and an input would lose its focus.
  let read (get: SeriesForm.Form -> 'T) (fallback: 'T) () = form () |> Option.map get |> Option.defaultValue fallback

  let problem field () =
    form ()
    |> Option.bind (fun form -> SeriesForm.fieldProblem (others ()) form field)
    |> Option.defaultValue ""

  Fragment() {
    Parts
      .Field(id = $"{prefix}-title", label = "Title")
      .Has(
        Fragment() {
          Parts
            .Input(id = $"{prefix}-title", value = read _.Title "", onInput = fun event -> send (SeriesForm.TitleTyped(inputText event)))
            .View()

          error (problem SeriesForm.TitleField)
        }
      )

    Parts
      .Field(id = $"{prefix}-slug", label = "Slug")
      .Has(
        Fragment() {
          Parts
            .Input(
              id = $"{prefix}-slug",
              value = read _.Slug "",
              disabledWhen = read SeriesForm.slugLocked false,
              onInput = fun event -> send (SeriesForm.SlugTyped(inputText event))
            )
            .View()

          p (class' = "hint") { read SeriesForm.slugHint "" () }
          error (problem SeriesForm.SlugField)
        }
      )

    Parts
      .Field(id = $"{prefix}-excerpt", label = "Excerpt")
      .Has(
        Fragment() {
          Parts
            .Textarea(
              id = $"{prefix}-excerpt",
              value = read _.Excerpt "",
              onInput = fun event -> send (SeriesForm.ExcerptTyped(inputText event))
            )
            .View()

          error (problem SeriesForm.ExcerptField)
        }
      )

    Parts
      .Field(id = $"{prefix}-image", label = "Image")
      .Has(
        Fragment() {
          div (class' = "row") {
            Parts
              .Input(
                id = $"{prefix}-image",
                kind = "url",
                value = read _.Image "",
                disabledWhen = read SeriesForm.isUploading false,
                onInput = fun event -> send (SeriesForm.ImageTyped(inputText event))
              )
              .View()

            button (
              type' = "button",
              class' = Parts.buttonClass Parts.Secondary Parts.Medium,
              disabled = read SeriesForm.isUploading false (),
              onClick = fun _ -> pickImage (SeriesForm.ImagePicked >> send)
            ) {
              if read SeriesForm.isUploading false () then "Uploading…" else "Upload"
            }
          }

          p (class' = "hint") { "The https URL of the image, or upload an image." }
          error (problem SeriesForm.ImageField)
          error (fun () -> read SeriesForm.uploadFailure None () |> Option.defaultValue "")
        }
      )

    Show(when' = (read SeriesForm.failure None ()).IsSome) {
      Parts.Alert(tone = Parts.Bad).Has(p () { read SeriesForm.failure None () |> Option.defaultValue "" })
    }
  }
