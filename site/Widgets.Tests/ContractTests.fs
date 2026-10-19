module TwoPoint.Site.Widgets.Tests.ContractTests

open TwoPoint.Site.Widgets

open Expecto
open Microsoft.FSharp.Reflection

let private subscribe = Contract.Widget.Subscribe { Api = "http://localhost:7071" }

let private comments =
  Contract.Widget.Comments { Api = "https://api.example"; Slug = "fable-reminders-part-1" }

let private unsubscribe = Contract.Widget.Unsubscribe { Api = "https://api.example" }

/// One value of each case of `Contract.Widget`.
let private widgets = [ subscribe; comments; unsubscribe ]

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

      Expect.equal
        (Contract.toAttributes comments)
        [ "data-widget", "comments"; "data-api", "https://api.example"; "data-slug", "fable-reminders-part-1" ]
        "The Comments mount has its name, the API origin, and the slug"

      Expect.equal
        (Contract.toAttributes unsubscribe)
        [ "data-widget", "unsubscribe"; "data-api", "https://api.example" ]
        "The Unsubscribe mount has its name and the API origin"
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
        (Error "The widget 'search' is unknown. Use one of these names: subscribe, comments, unsubscribe.")
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
  
    test "a Comments mount with no API origin or no slug gives an error" {
      Expect.equal
        (Contract.parse [ "data-widget", "comments"; "data-slug", "a-post" ])
        (Error "The comments mount has no 'data-api' attribute.")
        "No data-api"

      Expect.equal
        (Contract.parse [ "data-widget", "comments"; "data-api", "https://api.example" ])
        (Error "The comments mount has no 'data-slug' attribute.")
        "No data-slug"
    }

    test "a Comments mount with a bad API origin gives the error of the API origin" {
      Expect.equal
        (Contract.parse [ "data-widget", "comments"; "data-api", "https://api.example/"; "data-slug", "a-post" ])
        (Error "The 'data-api' of the comments mount is 'https://api.example/'. Give an http or https origin with no '/' at the end.")
        "A '/' at the end"
    }

    test "a Comments mount with a slug that is not a post slug gives an error" {
      for value in [ ""; "A-Post"; "a post"; "a/post"; "a-post?x=1" ] do
        Expect.equal
          (Contract.parse [ "data-widget", "comments"; "data-api", "https://api.example"; "data-slug", value ])
          (Error $"The 'data-slug' of the comments mount is '{value}'. Give a post slug of lowercase letters, digits, and '-'.")
          $"'{value}' is not valid"
    }

    test "an Unsubscribe mount with no API origin or a bad one gives an error" {
      Expect.equal
        (Contract.parse [ "data-widget", "unsubscribe" ])
        (Error "The unsubscribe mount has no 'data-api' attribute.")
        "No data-api"

      Expect.equal
        (Contract.parse [ "data-widget", "unsubscribe"; "data-api", "https://api.example/" ])
        (Error "The 'data-api' of the unsubscribe mount is 'https://api.example/'. Give an http or https origin with no '/' at the end.")
        "A '/' at the end"
    }
  ]
