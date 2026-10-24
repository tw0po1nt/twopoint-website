module TwoPoint.Admin.Tests.SubscriptionsTests

open TwoPoint.Admin

open Expecto

let private subscription id name isActive : Api.Subscription =
  { Id = id
    Email = $"{id}@Example.com"
    Name = name
    CreatedDate = "2026-01-02T03:04:05Z"
    IsActive = isActive
    CanceledDate = if isActive then None else Some "2026-02-03T04:05:06Z" }

let private ada = subscription "ada" (Some "Ada Lovelace") true
let private grace = subscription "grace" (Some "Grace Hopper") false
let private alan = subscription "alan" None true
let private edsger = subscription "edsger" (Some "Edsger Dijkstra") false

let private delivery id deliveryType : Api.Delivery =
  { Id = id
    CreatedDate = "2026-02-01T00:00:00Z"
    Type = deliveryType
    PostSlug = "first"
    PostTitle = "First" }

let private counts active inactive : Subscriptions.Counts = { Active = active; Inactive = inactive }

let private sheet id deliveries : Subscriptions.Sheet = { Id = id; Deliveries = deliveries }

let private start (path: string) =
  let model, _ = Shell.init path
  Shell.Session(Session.Started true) |> fun msg -> Shell.update msg model

let private listed () =
  let model, _ = start "/subscriptions"
  let model, _ = Shell.update (Shell.Subscriptions(Subscriptions.Answered(Ok [ ada; grace; alan; edsger ]))) model
  model

let private send msg model = Shell.update (Shell.Subscriptions msg) model

let private shown (model: Shell.Model) =
  Subscriptions.shown model.Subscriptions |> List.map _.Id

let private tests' =
  testList "Subscriptions" [
    test "a start on the subscriptions page loads the subscriptions with the first API calls" {
      let model, effect = start "/subscriptions"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadDashboard; Shell.LoadQueue; Shell.LoadSubscriptions ])) "The list"
      Expect.equal model.Subscriptions.Subscriptions Subscriptions.Waiting "The list waits"
    }

    test "the counts are the active and the inactive subscriptions" {
      let model = listed ()
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some(counts 2 2)) "Two and two"
    }

    test "the counts do not follow the filter or the search" {
      let model = listed ()
      let model, _ = send (Subscriptions.Filtered Subscriptions.Inactive) model
      let model, _ = send (Subscriptions.Searched "grace") model
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some(counts 2 2)) "Two and two"
    }

    test "the list has no counts before the subscriptions load" {
      let model, _ = start "/subscriptions"
      Expect.isNone (Subscriptions.counts model.Subscriptions) "No counts"
    }

    test "the filter shows all subscriptions, or only the active or the inactive subscriptions" {
      let model = listed ()
      Expect.equal model.Subscriptions.Filter Subscriptions.All "All at first"
      Expect.equal (shown model) [ "ada"; "grace"; "alan"; "edsger" ] "All, in the order of the API"

      for filter, expected in
        [ Subscriptions.Active, [ "ada"; "alan" ]
          Subscriptions.Inactive, [ "grace"; "edsger" ] ] do
        let model, effect = send (Subscriptions.Filtered filter) model
        Expect.isNone effect "The filter calls no API"
        Expect.equal (shown model) expected $"Only {filter}"
    }

    test "the search finds the email address, and ignores the case" {
      let model, _ = send (Subscriptions.Searched "GRACE@example") (listed ())
      Expect.equal (shown model) [ "grace" ] "The email address"
    }

    test "the search finds the name, and ignores the case and the space around the text" {
      let model, effect = send (Subscriptions.Searched "  hopper ") (listed ())
      Expect.isNone effect "The search calls no API"
      Expect.equal (shown model) [ "grace" ] "The name"
    }

    test "the search finds part of a name" {
      let model, _ = send (Subscriptions.Searched "ove") (listed ())
      Expect.equal (shown model) [ "ada" ] "Lovelace"
    }

    test "the search finds a subscription with no name by its email address" {
      let model, _ = send (Subscriptions.Searched "alan@") (listed ())
      Expect.equal (shown model) [ "alan" ] "The email address"
    }

    test "the search and the filter work together" {
      let model = listed ()
      let model, _ = send (Subscriptions.Filtered Subscriptions.Inactive) model
      let model, _ = send (Subscriptions.Searched "dijkstra") model
      Expect.equal (shown model) [ "edsger" ] "Inactive and Dijkstra"

      let model, _ = send (Subscriptions.Filtered Subscriptions.Active) model
      Expect.isEmpty (shown model) "No active subscription has Dijkstra"
    }

    test "an empty search shows the subscriptions of the filter" {
      let model, _ = send (Subscriptions.Searched "grace") (listed ())
      let model, _ = send (Subscriptions.Searched "") model
      Expect.equal (shown model) [ "ada"; "grace"; "alan"; "edsger" ] "All"
    }

    test "the empty text says what the filter and the search found" {
      let none = Subscriptions.emptyText Subscriptions.All ""
      Expect.equal none "The blog has no subscriptions." "No subscription"
      Expect.equal (Subscriptions.emptyText Subscriptions.Active "") "The blog has no active subscriptions." "No active"
      Expect.equal (Subscriptions.emptyText Subscriptions.Inactive "") "The blog has no inactive subscriptions." "No inactive"
      Expect.equal (Subscriptions.emptyText Subscriptions.All "zed") "No subscription matches the search." "No match"
    }

    test "the filter and the search stay after a visit to another page, and the list loads again" {
      let model = listed ()
      let model, _ = send (Subscriptions.Filtered Subscriptions.Inactive) model
      let model, _ = send (Subscriptions.Searched "grace") model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/")) model
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/subscriptions")) model

      Expect.equal model.Subscriptions.Filter Subscriptions.Inactive "The filter"
      Expect.equal model.Subscriptions.Search "grace" "The search"
      Expect.equal model.Subscriptions.Subscriptions Subscriptions.Waiting "The list loads again"

      match effect with
      | Some(Shell.Navigate(_, calls)) -> Expect.equal calls [ Shell.LoadSubscriptions ] "The list"
      | other -> failtestf "Expected a navigation, got %A" other
    }

    test "a list that did not load shows the reason" {
      let model, _ = start "/subscriptions"
      let model, _ = Shell.update (Shell.Subscriptions(Subscriptions.Answered(Error Api.Unanswered))) model

      Expect.equal
        model.Subscriptions.Subscriptions
        (Subscriptions.NotLoaded(Api.reason Api.Unanswered))
        "The reason"

      Expect.isEmpty (shown model) "No row"
    }

    test "a refused list opens the refusal banner" {
      let model, _ = start "/subscriptions"
      let model, _ = Shell.update (Shell.Subscriptions(Subscriptions.Answered(Error Api.Refused))) model
      Expect.equal model.Session.Status Session.Refused "Refused"
    }
  ]

let private sheetTests =
  testList "Subscription sheet" [
    test "a click on a subscription opens the sheet and loads its deliveries" {
      let model, effect = send (Subscriptions.Opened "grace") (listed ())

      Expect.equal effect (Some(Shell.Send [ Shell.LoadSubscriptionDeliveries "grace" ])) "The deliveries"
      Expect.equal (Subscriptions.tryOpened model.Subscriptions) (Some grace) "The sheet shows grace"
      Expect.equal model.Subscriptions.Sheet (Some(sheet "grace" (Subscriptions.DeliveriesWaiting))) "Waiting"
    }

    test "the deliveries fill the sheet" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let deliveries = [ delivery "d-2" Api.DeliveryType.NewPost; delivery "d-1" Api.DeliveryType.Welcome ]
      let model, _ = send (Subscriptions.DeliveriesAnswered("grace", Ok deliveries)) model

      Expect.equal
        model.Subscriptions.Sheet
        (Some(sheet "grace" (Subscriptions.DeliveriesLoaded deliveries)))
        "The deliveries"
    }

    test "deliveries that did not load show the reason" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let model, _ = send (Subscriptions.DeliveriesAnswered("grace", Error Api.Unanswered)) model

      Expect.equal
        model.Subscriptions.Sheet
        (Some(sheet "grace" (Subscriptions.DeliveriesNotLoaded(Api.reason Api.Unanswered))))
        "The reason"
    }

    test "a late answer for another subscription changes nothing" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let model, _ = send (Subscriptions.Opened "ada") model
      let late, effect = send (Subscriptions.DeliveriesAnswered("grace", Ok [ delivery "d-1" Api.DeliveryType.Welcome ])) model

      Expect.isNone effect "No call"
      Expect.equal late.Subscriptions model.Subscriptions "No change"
    }

    test "a subscription that the list does not have opens no sheet" {
      let model, effect = send (Subscriptions.Opened "unknown") (listed ())

      Expect.isNone effect "No call"
      Expect.isNone model.Subscriptions.Sheet "No sheet"
    }

    test "a close removes the sheet, and the filter stays" {
      let model, _ = send (Subscriptions.Filtered Subscriptions.Inactive) (listed ())
      let model, _ = send (Subscriptions.Opened "grace") model
      let model, effect = send Subscriptions.Closed model

      Expect.isNone effect "No call"
      Expect.isNone model.Subscriptions.Sheet "No sheet"
      Expect.equal model.Subscriptions.Filter Subscriptions.Inactive "The filter"
    }

    test "a refused delivery list opens the refusal banner" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let model, _ = send (Subscriptions.DeliveriesAnswered("grace", Error Api.Refused)) model
      Expect.equal model.Session.Status Session.Refused "Refused"
    }

    test "the sheet closes when the list loads again" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/")) model
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/subscriptions")) model
      Expect.isNone model.Subscriptions.Sheet "No sheet"
    }
  ]

[<Tests>]
let tests =
  testList "Subscriptions page" [
    tests'
    sheetTests
  ]
