module TwoPoint.Admin.Tests.MenuTests

open TwoPoint.Admin

open Expecto

let private trigger: Menu.Box =
  { Left = 12.
    Top = 740.
    Right = 228.
    Bottom = 780. }

let private viewport: Menu.Viewport = { Width = 1280.; Height = 800. }

[<Tests>]
let tests =
  testList "Menu" [
    test "a menu above its trigger starts at the left edge of the trigger" {
      let position = Menu.place { Side = Menu.Above; Edge = Menu.Start } trigger viewport
      Expect.equal position { Vertical = Menu.Bottom 66.; Horizontal = Menu.Left 12. } "6 px above the trigger"
      Expect.equal (Menu.style position) "bottom: 66px; left: 12px" "The style of the menu"
    }

    test "a menu below its trigger ends at the right edge of the trigger" {
      let avatar: Menu.Box =
        { Left = 1236.
          Top = 8.
          Right = 1268.
          Bottom = 40. }

      let position = Menu.place { Side = Menu.Below; Edge = Menu.End } avatar viewport
      Expect.equal position { Vertical = Menu.Top 46.; Horizontal = Menu.Right 12. } "6 px below the trigger"
      Expect.equal (Menu.style position) "top: 46px; right: 12px" "The style of the menu"
    }

    test "the arrow keys, Home, and End move the focus, and the other keys do not" {
      Expect.equal
        ([ "ArrowDown"; "ArrowUp"; "Home"; "End"; "Enter"; "a" ] |> List.map Menu.move)
        [ Some Menu.Next; Some Menu.Previous; Some Menu.First; Some Menu.Last; None; None ]
        "The moves"
    }

    test "a move goes to the next item, and wraps at each end" {
      Expect.equal (Menu.target Menu.Next 3 (Some 0)) (Some 1) "Down from the first item"
      Expect.equal (Menu.target Menu.Next 3 (Some 2)) (Some 0) "Down from the last item"
      Expect.equal (Menu.target Menu.Previous 3 (Some 0)) (Some 2) "Up from the first item"
      Expect.equal (Menu.target Menu.First 3 (Some 2)) (Some 0) "Home"
      Expect.equal (Menu.target Menu.Last 3 (Some 0)) (Some 2) "End"
    }

    test "with the focus on no item, down goes to the first item and up to the last item" {
      Expect.equal (Menu.target Menu.Next 3 None) (Some 0) "Down"
      Expect.equal (Menu.target Menu.Previous 3 None) (Some 2) "Up"
    }

    test "a menu with no item gets no focus" {
      Expect.equal (Menu.target Menu.Next 0 None) None "No item"
    }
  ]
