module TwoPoint.Site.Widgets.Tests.ContractTests

open TwoPoint.Site.Widgets

open Expecto
open Microsoft.FSharp.Reflection

let private subscribe = Contract.Widget.Subscribe { Api = "http://localhost:7071" }

/// One value of each case of `Contract.Widget`.
let private widgets = [ subscribe ]

[<Tests>]
let tests =
  testList "Contract" [
    test "parse reverses toAttributes for each widget" {
      for widget in widgets do
        Expect.equal (Contract.parse (Contract.toAttributes widget)) (Ok widget) $"%A{widget} gives the same widget"
    }

    test "the mount gives the name of the widget and its props as data attributes" {
      Expect.equal
        (Contract.toAttributes subscribe)
        [ "data-widget", "subscribe"; "data-api", "http://localhost:7071" ]
        "The Subscribe mount has its name and the API origin"
    }

    test "the names list each widget one time" {
      Expect.hasLength widgets (FSharpType.GetUnionCases(typeof<Contract.Widget>).Length) "The test has one value for each widget"
      Expect.equal Contract.names (widgets |> List.map Contract.name) "Each widget has its name on the list"
    }

    test "parse ignores the attributes that are not part of the contract" {
      Expect.equal
        (Contract.parse [ "id", "x"; "data-widget", "subscribe"; "class", "y"; "data-api", "https://api.example" ])
        (Ok(Contract.Widget.Subscribe { Api = "https://api.example" }))
        "Other attributes do not change the widget"
    }

    test "a mount with no widget name or an unknown name gives an error" {
      Expect.equal (Contract.parse [ "data-api", "https://api.example" ]) (Error "The mount has no 'data-widget' attribute.") "No name"

      Expect.equal
        (Contract.parse [ "data-widget", "search" ])
        (Error "The widget 'search' is unknown. Use one of these names: subscribe.")
        "An unknown name"
    }

    test "a Subscribe mount with no API origin gives an error" {
      Expect.equal
        (Contract.parse [ "data-widget", "subscribe" ])
        (Error "The subscribe mount has no 'data-api' attribute.")
        "No data-api"
    }

    test "a Subscribe mount with an API value that is not an http or https origin gives an error" {
      for value in [ ""; "localhost:7071"; "ftp://api.example"; "https://api.example/" ] do
        Expect.equal
          (Contract.parse [ "data-widget", "subscribe"; "data-api", value ])
          (Error $"The 'data-api' of the subscribe mount is '{value}'. Give an http or https origin with no '/' at the end.")
          $"'{value}' is not valid"
    }
  ]
