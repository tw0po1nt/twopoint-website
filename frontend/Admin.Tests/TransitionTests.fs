module TwoPoint.Admin.Tests.TransitionTests

open TwoPoint.Admin

open Expecto

let private choose = Transition.choose Transition.Full Transition.ViewTransitions Transition.AppAnimates

[<Tests>]
let tests =
  testList "Transition" [
    test "a post opens in the editor with the light" {
      Expect.equal (choose (Posts PostFilter.All) (Post "first")) (Transition.Lamp Transition.IntoEditor) "From the posts"
      Expect.equal (choose Dashboard (Post "first")) (Transition.Lamp Transition.IntoEditor) "From the dashboard"
      Expect.equal (choose (PostComments "first") (Post "first")) (Transition.Lamp Transition.IntoEditor) "From the comments of the post"
    }

    test "a move out of the editor takes the light back into its post" {
      Expect.equal (choose (Post "first") (Posts PostFilter.All)) (Transition.Lamp(Transition.OutOfEditor "first")) "To the posts"
      Expect.equal (choose (Post "first") Dashboard) (Transition.Lamp(Transition.OutOfEditor "first")) "To the dashboard"
    }

    test "a move that does not go into or out of the editor has no transition" {
      Expect.equal (choose Dashboard (Posts PostFilter.All)) Transition.NoMove "Between two pages of the admin"
      Expect.equal (choose (Post "first") (Post "renamed")) Transition.NoMove "From one post to another"
    }

    test "with reduce motion, the move is a cross-fade" {
      let reduced = Transition.choose Transition.Reduced Transition.ViewTransitions Transition.AppAnimates
      Expect.equal (reduced Dashboard (Post "first")) Transition.CrossFade "Into the editor"
      Expect.equal (reduced (Post "first") Dashboard) Transition.CrossFade "Out of the editor"
    }

    test "with no View Transitions, only the new screen animates" {
      let alone = Transition.choose Transition.Full Transition.NoViewTransitions Transition.AppAnimates
      Expect.equal (alone Dashboard (Post "first")) (Transition.NewScreenOnly Transition.IntoEditor) "Into the editor"
      Expect.equal (alone (Post "first") Dashboard) (Transition.NewScreenOnly(Transition.OutOfEditor "first")) "Out of the editor"
    }

    test "with no View Transitions and reduce motion, the screen changes at once" {
      let still = Transition.choose Transition.Reduced Transition.NoViewTransitions Transition.AppAnimates
      Expect.equal (still Dashboard (Post "first")) Transition.NoMove "No animation"
    }

    test "a back that the browser animated has no second animation" {
      let swiped = Transition.choose Transition.Full Transition.ViewTransitions Transition.BrowserAnimated
      Expect.equal (swiped (Post "first") (Posts PostFilter.All)) Transition.NoMove "The edge swipe of iOS"
    }
  ]
