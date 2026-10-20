module TwoPoint.Site.Widgets.Tests.PdfTests

open TwoPoint.Site.Widgets

open Expecto

let private showing number count = Pdf.Showing { Number = number; Count = count }

[<Tests>]
let tests =
  testList "Pdf" [
    test "the viewer opens the file of the mount" {
      Expect.equal
        (Pdf.init "https://files.example/slides.pdf")
        (Pdf.Opening, Some(Pdf.Open "https://files.example/slides.pdf"))
        "The viewer opens the file"
    }

    test "an open PDF shows and draws its first page" {
      Expect.equal (Pdf.update (Pdf.Opened 12) Pdf.Opening) (showing 1 12, Some(Pdf.Draw 1)) "Page 1 of 12 draws"
    }

    test "a PDF with no page or a failure gives the error" {
      Expect.equal (Pdf.update (Pdf.Opened 0) Pdf.Opening) (Pdf.Broken, None) "No page"
      Expect.equal (Pdf.update Pdf.Failed Pdf.Opening) (Pdf.Broken, None) "The PDF did not load"
      Expect.equal (Pdf.update Pdf.Failed (showing 3 12)) (Pdf.Broken, None) "The page did not draw"
    }

    test "next and previous move one page and draw it" {
      Expect.equal (Pdf.update Pdf.Next (showing 3 12)) (showing 4 12, Some(Pdf.Draw 4)) "Next"
      Expect.equal (Pdf.update Pdf.Previous (showing 3 12)) (showing 2 12, Some(Pdf.Draw 2)) "Previous"
    }

    test "a change of width draws the current page again" {
      Expect.equal (Pdf.update Pdf.Resized (showing 3 12)) (showing 3 12, Some(Pdf.Draw 3)) "Page 3 draws again"
    }

    test "a link to a page shows and draws that page" {
      Expect.equal (Pdf.update (Pdf.GoTo 7) (showing 3 12)) (showing 7 12, Some(Pdf.Draw 7)) "Page 7 draws"
    }

    test "a link to the current page or to no page of the PDF does nothing" {
      Expect.equal (Pdf.update (Pdf.GoTo 3) (showing 3 12)) (showing 3 12, None) "The current page"
      Expect.equal (Pdf.update (Pdf.GoTo 0) (showing 3 12)) (showing 3 12, None) "Before the first page"
      Expect.equal (Pdf.update (Pdf.GoTo 13) (showing 3 12)) (showing 3 12, None) "After the last page"
      Expect.equal (Pdf.update (Pdf.GoTo 2) Pdf.Opening) (Pdf.Opening, None) "No page shows"
    }

    test "previous on the first page and next on the last page do nothing" {
      Expect.equal (Pdf.update Pdf.Previous (showing 1 12)) (showing 1 12, None) "No page before the first"
      Expect.equal (Pdf.update Pdf.Next (showing 12 12)) (showing 12 12, None) "No page after the last"
      Expect.equal (Pdf.update Pdf.Next (showing 1 1)) (showing 1 1, None) "A PDF of one page"
    }

    test "the page buttons work only between the first and the last page" {
      Expect.isFalse (Pdf.hasPrevious (showing 1 12)) "No previous on the first page"
      Expect.isTrue (Pdf.hasNext (showing 1 12)) "Next on the first page"
      Expect.isTrue (Pdf.hasPrevious (showing 12 12)) "Previous on the last page"
      Expect.isFalse (Pdf.hasNext (showing 12 12)) "No next on the last page"

      for model in [ Pdf.Opening; Pdf.Broken ] do
        Expect.isFalse (Pdf.hasPrevious model || Pdf.hasNext model) $"No page buttons when %A{model}"
    }

    test "no message changes the error, and a page message does nothing before the PDF opens" {
      for msg in [ Pdf.Opened 3; Pdf.Failed; Pdf.Previous; Pdf.Next; Pdf.Resized ] do
        Expect.equal (Pdf.update msg Pdf.Broken) (Pdf.Broken, None) $"%A{msg} keeps the error"

      for msg in [ Pdf.Previous; Pdf.Next; Pdf.Resized ] do
        Expect.equal (Pdf.update msg Pdf.Opening) (Pdf.Opening, None) $"%A{msg} waits for the PDF"
    }
  ]
