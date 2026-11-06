module TwoPoint.Admin.Tests.DiffTests

open TwoPoint.Admin

open Expecto

[<Tests>]
let tests =
  testList "Diff" [
    test "the same text has no change" {
      let lines = Diff.lines "a\nb\n" "a\nb\n"
      Expect.isFalse (Diff.hasChanges lines) "No change"
      Expect.equal lines [ Diff.Same "a"; Diff.Same "b"; Diff.Same "" ] "Each line is the same"
    }

    test "a changed line is one removed line and one added line, in that order" {
      Expect.equal
        (Diff.lines "a\nb\nc" "a\nB\nc")
        [ Diff.Same "a"; Diff.Removed "b"; Diff.Added "B"; Diff.Same "c" ]
        "The lines"
    }

    test "an added line and a removed line keep the lines around them" {
      Expect.equal (Diff.lines "a\nc" "a\nb\nc") [ Diff.Same "a"; Diff.Added "b"; Diff.Same "c" ] "Added"
      Expect.equal (Diff.lines "a\nb\nc" "a\nc") [ Diff.Same "a"; Diff.Removed "b"; Diff.Same "c" ] "Removed"
    }

    test "an empty text against a text adds each line, and the reverse removes each line" {
      Expect.equal (Diff.lines "" "a\nb") [ Diff.Added "a"; Diff.Added "b" ] "Added"
      Expect.equal (Diff.lines "a\nb" "") [ Diff.Removed "a"; Diff.Removed "b" ] "Removed"
      Expect.equal (Diff.lines "" "") [] "Nothing"
    }

    test "a moved block keeps the longest common run of lines" {
      let lines = Diff.lines "a\nb\nc\nd" "c\nd\na\nb"

      let same =
        lines
        |> List.filter (function
          | Diff.Same _ -> true
          | _ -> false)

      Expect.hasLength same 2 "Two lines stay"
      Expect.hasLength lines 6 "Two stay, two go, and two come"
    }

    test "a line ending with CR and LF is the same line as one with LF" {
      Expect.isFalse (Diff.hasChanges (Diff.lines "a\r\nb" "a\nb")) "No change"
    }

    test "each line of the result is in the before text or in the after text, and the same lines are in both" {
      let before = "one\ntwo\nthree\nfour\nfive"
      let after = "one\n2\nthree\nfive\nsix"
      let lines = Diff.lines before after

      let kept predicate =
        lines
        |> List.choose (fun line ->
          match predicate line with
          | Some text -> Some text
          | None -> None)
        |> String.concat "\n"

      Expect.equal
        (kept (function
          | Diff.Same text
          | Diff.Removed text -> Some text
          | Diff.Added _ -> None))
        before
        "Same and removed lines give the before text"

      Expect.equal
        (kept (function
          | Diff.Same text
          | Diff.Added text -> Some text
          | Diff.Removed _ -> None))
        after
        "Same and added lines give the after text"
    }

    test "the context keeps the lines around a change and folds a longer run of the same lines" {
      let before = [ for number in 1..20 -> $"line {number}" ] |> String.concat "\n"
      let after = before.Replace("line 10", "LINE 10")

      Expect.equal
        (Diff.withContext 2 (Diff.lines before after))
        [ Diff.Skipped 7
          Diff.Shown(Diff.Same "line 8")
          Diff.Shown(Diff.Same "line 9")
          Diff.Shown(Diff.Removed "line 10")
          Diff.Shown(Diff.Added "LINE 10")
          Diff.Shown(Diff.Same "line 11")
          Diff.Shown(Diff.Same "line 12")
          Diff.Skipped 8 ]
        "The entries"
    }

    test "two changes that are close together share their context" {
      let before = "a\nb\nc\nd\ne"
      let after = "A\nb\nc\nd\nE"

      Expect.equal
        (Diff.withContext 2 (Diff.lines before after))
        [ Diff.Shown(Diff.Removed "a")
          Diff.Shown(Diff.Added "A")
          Diff.Shown(Diff.Same "b")
          Diff.Shown(Diff.Same "c")
          Diff.Shown(Diff.Same "d")
          Diff.Shown(Diff.Removed "e")
          Diff.Shown(Diff.Added "E") ]
        "No fold"
    }
  ]
