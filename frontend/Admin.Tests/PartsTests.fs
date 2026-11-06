module TwoPoint.Admin.Tests.PartsTests

open TwoPoint.Admin

open Expecto

[<Tests>]
let tests =
  testList "Parts" [
    test "the avatar shows the initials of the first and the last name" {
      Expect.equal (Parts.initials "Ada Lovelace") "AL" "Two names"
      Expect.equal (Parts.initials "  mary ann evans ") "ME" "Three names, in lower case"
      Expect.equal (Parts.initials "Ada") "A" "One name"
      Expect.equal (Parts.initials "") "" "No name"
    }
  ]
