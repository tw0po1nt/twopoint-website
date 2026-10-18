module TwoPoint.Site.Widgets.Tests.HeadTests

open TwoPoint.Site.Widgets

open Expecto

let private themes = [ Head.Theme.Light; Head.Theme.Dark ]

let private forcedThemes = None :: List.map Some themes

let private shown (system: Head.Theme) (forced: Head.Theme option) = forced |> Option.defaultValue system

let private throws _ = failwith "The storage is not available"

[<Tests>]
let tests =
  testList "Head" [
    test "the stored text gives the theme" {
      Expect.equal (Head.parse "light") (Some Head.Theme.Light) "light is Light"
      Expect.equal (Head.parse "dark") (Some Head.Theme.Dark) "dark is Dark"
    }

    test "no stored text, or other text, follows the system setting" {
      Expect.isNone (Head.parse null) "No text follows the system setting"
      Expect.isNone (Head.parse "Dark") "The text is case-sensitive"
      Expect.isNone (Head.parse "sepia") "An unknown theme follows the system setting"
    }

    test "the stored text of each theme gives the same theme" {
      for theme in themes do
        Expect.equal (Head.parse (Head.name theme)) (Some theme) $"{Head.name theme} gives the same theme"
    }

    test "the first click forces the theme that is not the system setting" {
      Expect.equal (Head.toggle Head.Theme.Light None) (Some Head.Theme.Dark) "A light system gives dark"
      Expect.equal (Head.toggle Head.Theme.Dark None) (Some Head.Theme.Light) "A dark system gives light"
    }

    test "a click that gives the system setting deletes the choice" {
      Expect.isNone (Head.toggle Head.Theme.Light (Some Head.Theme.Dark)) "Dark on a light system goes back to the system setting"
      Expect.isNone (Head.toggle Head.Theme.Dark (Some Head.Theme.Light)) "Light on a dark system goes back to the system setting"
    }

    test "a choice that agrees with the system setting switches to the other theme" {
      Expect.equal (Head.toggle Head.Theme.Light (Some Head.Theme.Light)) (Some Head.Theme.Dark) "Light on a light system gives dark"
      Expect.equal (Head.toggle Head.Theme.Dark (Some Head.Theme.Dark)) (Some Head.Theme.Light) "Dark on a dark system gives light"
    }

    test "each click shows the other theme" {
      for system in themes do
        for forced in forcedThemes do
          let next = Head.toggle system forced
          Expect.notEqual (shown system next) (shown system forced) $"The click changes the theme, from %A{forced} on a %A{system} system"
    }

    test "a stored choice is never the system setting" {
      for system in themes do
        for forced in forcedThemes do
          Expect.notEqual (Head.toggle system forced) (Some system) $"The click from %A{forced} on a %A{system} system stores no system theme"
    }

    test "the stored choice comes from the theme key" {
      let mutable key = ""

      let theme =
        Head.read (fun name ->
          key <- name
          "dark")

      Expect.equal key "theme" "The script reads the theme key"
      Expect.equal theme (Some Head.Theme.Dark) "The stored text gives the theme"
    }

    test "a choice goes to the theme key, and the system setting deletes the key" {
      let calls = ResizeArray<string>()
      let setItem key value = calls.Add $"set {key} {value}"
      let removeItem key = calls.Add $"remove {key}"
      Head.write setItem removeItem (Some Head.Theme.Light)
      Head.write setItem removeItem None
      Expect.equal (List.ofSeq calls) [ "set theme light"; "remove theme" ] "The script sets, then deletes, the theme key"
    }

    test "storage that throws on a read follows the system setting" {
      Expect.isNone (Head.read throws) "A read that throws gives no choice"
    }

    test "storage that throws on a write does not stop the click" {
      for forced in forcedThemes do
        Head.write (fun _ _ -> throws ()) throws forced
    }
  ]
