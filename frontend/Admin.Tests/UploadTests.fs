module TwoPoint.Admin.Tests.UploadTests

open TwoPoint.Admin

open Browser.Types
open Expecto

let private url = "https://twopointwebsite.blob.core.windows.net/assets/0123456789abcdef.png"

// The model never reads the browser file, so the tests pass a null file.
let private browserFile = Unchecked.defaultof<File>

let private file name contentType size : Upload.Dropped =
  { Name = name
    ContentType = contentType
    Size = size
    File = browserFile
    At = 5 }

let private send msg (model: Shell.Model) = Shell.update (Shell.Upload msg) model

let private started () =
  let model, _ = Shell.init "/posts/my-post"
  fst (Shell.update (Shell.Session(Session.Started true)) model)

let private only (model: Shell.Model) =
  match model.Uploads.Items with
  | [ item ] -> item
  | items -> failtestf "Expected one upload, got %A" items

[<Tests>]
let tests =
  testList "Upload" [
    test "a dropped image starts an upload, and the answer gives the URL for the editor" {
      let model, effect = send (Upload.Dropped(file "cat.png" "image/png" 1024.)) (started ())
      Expect.equal effect (Some(Shell.Send [ Shell.UploadAsset(1, browserFile) ])) "The call"
      Expect.equal (only model).State Upload.Uploading "The placeholder shows"
      Expect.equal (only model).Kind Upload.Image "An image"
      Expect.equal (only model).At 5 "The place of the placeholder"

      let model, effect = send (Upload.Answered(1, Ok url)) model
      Expect.isNone effect "No call"
      Expect.equal (only model).State (Upload.Ready url) "The editor can write the Markdown"

      let model, _ = send (Upload.Inserted 1) model
      Expect.isEmpty model.Uploads.Items "The editor wrote the Markdown"
    }

    test "an image gets the Markdown with the cursor between the brackets" {
      let text, cursor = Upload.insertion Upload.Image url "Before " " after"
      Expect.equal text $"![]({url})" "The image"
      Expect.equal (text.Substring(0, cursor)) "![" "The cursor is in the alt text"
      Expect.equal text[cursor] ']' "The cursor is before the closing bracket"
    }

    test "a dropped PDF gets a ::pdf line between blank lines" {
      let model, effect = send (Upload.Dropped(file "slides.pdf" "application/pdf" 2048.)) (started ())
      Expect.isSome effect "A call"
      Expect.equal (only model).Kind Upload.Pdf "A PDF"

      let pdf = "https://twopointwebsite.blob.core.windows.net/assets/0123456789abcdef.pdf"
      let line = $"::pdf{{file=\"{pdf}\"}}"
      Expect.equal (fst (Upload.insertion Upload.Pdf pdf "" "")) line "In an empty body"
      Expect.equal (fst (Upload.insertion Upload.Pdf pdf "Text" "More")) $"\n\n{line}\n\n" "Inside text"
      Expect.equal (fst (Upload.insertion Upload.Pdf pdf "Text\n" "\nMore")) $"\n{line}\n" "After one line break"
      Expect.equal (fst (Upload.insertion Upload.Pdf pdf "Text\n\n" "\n\nMore")) line "Between blank lines"
      Expect.equal (snd (Upload.insertion Upload.Pdf pdf "Text" "More")) (2 + line.Length) "The cursor is at the end of the line"
    }

    test "the name gives the type when the browser gives no content type" {
      Expect.equal (Upload.kindOf "" "Photo.JPG") (Some Upload.Image) "A JPEG"
      Expect.equal (Upload.kindOf "" "slides.pdf") (Some Upload.Pdf) "A PDF"
      Expect.isNone (Upload.kindOf "" "notes.txt") "A text file"
    }

    test "an SVG and a file above 25 MB are refused with no call and no upload" {
      let model, effect = send (Upload.Dropped(file "logo.svg" "image/svg+xml" 100.)) (started ())
      Expect.isNone effect "No call for an SVG"
      Expect.isEmpty model.Uploads.Items "No upload"

      Expect.equal
        (Upload.notices model.Uploads)
        [ { Id = 1
            Title = "A file did not upload."
            Reason = "logo.svg is not a JPEG, PNG, WebP, GIF, or PDF file."
            CanRetry = false } ]
        "The notice has no retry"

      let model, effect = send (Upload.Dropped(file "big.pdf" "application/pdf" (Upload.maxBytes + 1.))) model
      Expect.isNone effect "No call for a big file"
      Expect.equal (Upload.notices model.Uploads |> List.map _.Reason |> List.last) "big.pdf is larger than 25 MB." "The reason"

      let model, effect = send (Upload.Dropped(file "full.pdf" "application/pdf" Upload.maxBytes)) model
      Expect.isSome effect "A file of 25 MB goes out"
      Expect.equal (only model).Id 3 "The ids go on"

      let model, _ = send (Upload.Dismissed 1) model
      Expect.equal (Upload.notices model.Uploads |> List.map _.Id) [ 2 ] "The author dismissed the refusal"
    }

    test "a failed upload shows the reason, and a retry sends the same file again" {
      let model, _ = send (Upload.Dropped(file "cat.png" "image/png" 1024.)) (started ())
      let model, _ = send (Upload.Answered(1, Error Api.Unanswered)) model
      Expect.equal (only model).State (Upload.Failed "The API did not answer.") "The reason"

      Expect.equal
        (Upload.notices model.Uploads)
        [ { Id = 1; Title = "cat.png did not upload."; Reason = "The API did not answer."; CanRetry = true } ]
        "The retry shows"

      let model, effect = send (Upload.Retried 1) model
      Expect.equal effect (Some(Shell.Send [ Shell.UploadAsset(1, browserFile) ])) "The same file"
      Expect.equal (only model).State Upload.Uploading "The placeholder shows again"
      Expect.isEmpty (Upload.notices model.Uploads) "No notice"

      let model, _ = send (Upload.Answered(1, Ok url)) model
      Expect.equal (only model).State (Upload.Ready url) "The second try works"
    }

    test "a retry uses the place that the placeholder had when the upload failed" {
      let model, _ = send (Upload.Dropped(file "cat.png" "image/png" 1024.)) (started ())
      let model, _ = send (Upload.Answered(1, Error Api.Unanswered)) model
      let model, _ = send (Upload.Placed(1, 12)) model
      let model, _ = send (Upload.Retried 1) model
      Expect.equal (only model).At 12 "The moved place"

      let model, _ = send (Upload.Placed(1, 30)) model
      Expect.equal (only model).At 12 "No place is taken from an upload in flight"
    }

    test "the author can dismiss a failed upload, and cannot retry or dismiss an upload in flight" {
      let model, _ = send (Upload.Dropped(file "cat.png" "image/png" 1024.)) (started ())
      let kept, effect = send (Upload.Retried 1) model
      Expect.equal kept model "No retry in flight"
      Expect.isNone effect "No call"
      Expect.equal (fst (send (Upload.Dismissed 1) model)) model "No dismiss in flight"

      let model, _ = send (Upload.Answered(1, Error(Api.Failed "The file is larger than 25 MB"))) model
      Expect.equal (only model).State (Upload.Failed "The file is larger than 25 MB") "The reason of the API"

      let model, _ = send (Upload.Dismissed 1) model
      Expect.isEmpty model.Uploads.Items "Dismissed"
    }

    test "two uploads get two ids, and an answer for an unknown upload changes nothing" {
      let model, _ = send (Upload.Dropped(file "a.png" "image/png" 1.)) (started ())
      let model, _ = send (Upload.Dropped(file "b.png" "image/png" 1.)) model
      Expect.equal (model.Uploads.Items |> List.map _.Id) [ 1; 2 ] "The ids"

      let answered, effect = send (Upload.Answered(9, Ok url)) model
      Expect.equal answered model "Unknown id"
      Expect.isNone effect "No call"
    }

    test "a refused token ends the session" {
      let model, _ = send (Upload.Dropped(file "cat.png" "image/png" 1024.)) (started ())
      let model, _ = send (Upload.Answered(1, Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "The account is refused"
    }

    test "the uploads end when the author leaves the post" {
      let model, _ = send (Upload.Dropped(file "cat.png" "image/png" 1024.)) (started ())
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/posts")) model
      Expect.isEmpty model.Uploads.Items "No uploads"
    }
  ]
