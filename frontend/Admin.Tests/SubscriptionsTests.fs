module TwoPoint.Admin.Tests.SubscriptionsTests

open TwoPoint.Admin

open Expecto

let private subscription id name isActive : Api.Subscription =
  { Id = id
    Email = $"{id}@Example.com"
    Name = name
    CreatedDate = "2026-01-02T03:04:05Z"
    Status = if isActive then Api.SubscriptionStatus.Active else Api.SubscriptionStatus.Canceled
    CanceledDate = if isActive then None else Some "2026-02-03T04:05:06Z" }

let private pending id : Api.Subscription =
  { subscription id None false with
      Status = Api.SubscriptionStatus.Pending
      CanceledDate = None }

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

let private counts active inactive : Subscriptions.Counts = { Active = active; Pending = 0; Inactive = inactive }

let private sheet id deliveries : Subscriptions.Sheet =
  { Id = id
    Deliveries = deliveries
    State = Progress.Ready }

let private start (path: string) =
  let model, _ = Shell.init path
  Shell.Session(Session.Started true) |> fun msg -> Shell.update msg model

let private listed () =
  let model, _ = start "/subscriptions"
  let model, _ = Shell.update (Shell.Subscriptions(Subscriptions.Answered(Ok [ ada; grace; alan; edsger ]))) model
  model

let private send msg model = Shell.update (Shell.Subscriptions msg) model

let private filterTo (filter: SubscriptionFilter) (model: Shell.Model) =
  Shell.update (Shell.Router(Router.LinkFollowed(Route.path (Subscriptions filter)))) model

let private shown (model: Shell.Model) =
  match model.Router.Route with
  | Subscriptions filter -> Subscriptions.shown filter model.Subscriptions |> List.map _.Id
  | _ -> []

let private tests' =
  testList "Subscriptions" [
    test "a start on the subscriptions page loads the subscriptions with the first API calls" {
      let model, effect = start "/subscriptions"
      Expect.equal effect (Some(Shell.Send [ Shell.LoadQueue; Shell.LoadSubscriptions ])) "The list"
      Expect.equal model.Subscriptions.Subscriptions Subscriptions.Waiting "The list waits"
    }

    test "the counts are the active, the inactive subscriptions" {
      let model = listed ()
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some(counts 2 2)) "Two and two"
    }

    test "the counts do not follow the filter or the search" {
      let model = listed ()
      let model, _ = filterTo SubscriptionFilter.Inactive model
      let model, _ = send (Subscriptions.Searched "grace") model
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some(counts 2 2)) "Two and two"
    }

    test "the list has no counts before the subscriptions load" {
      let model, _ = start "/subscriptions"
      Expect.isNone (Subscriptions.counts model.Subscriptions) "No counts"
    }

    test "the filter shows all subscriptions, or only the active or the inactive subscriptions" {
      let model = listed ()
      Expect.equal model.Router.Route (Subscriptions SubscriptionFilter.All) "All at first"
      Expect.equal (shown model) [ "ada"; "grace"; "alan"; "edsger" ] "All, in the order of the API"

      for filter, expected in
        [ SubscriptionFilter.Active, [ "ada"; "alan" ]
          SubscriptionFilter.Inactive, [ "grace"; "edsger" ] ] do
        let model, effect = filterTo filter model
        Expect.equal effect (Some(Shell.Navigate(Router.PushUrl(Route.path (Subscriptions filter)), []))) "The filter calls no API"
        Expect.equal (shown model) expected $"Only {filter}"
    }

    test "a pending subscription has its own status, filter, and count, and is not inactive" {
      let bot = pending "bot"
      let model, _ = start "/subscriptions"
      let model, _ = Shell.update (Shell.Subscriptions(Subscriptions.Answered(Ok [ ada; grace; bot ]))) model

      Expect.equal (Subscriptions.statusText bot) "Pending" "The status"
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some { Active = 1; Pending = 1; Inactive = 1 }) "One of each"

      for filter, expected in
        [ SubscriptionFilter.Pending, [ "bot" ]
          SubscriptionFilter.Inactive, [ "grace" ]
          SubscriptionFilter.Active, [ "ada" ] ] do
        let model, _ = filterTo filter model
        Expect.equal (shown model) expected $"Only {filter}"

      Expect.equal (Subscriptions.emptyText SubscriptionFilter.Pending "") "No pending subscriptions." "No pending"
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
      let model, _ = filterTo SubscriptionFilter.Inactive model
      let model, _ = send (Subscriptions.Searched "dijkstra") model
      Expect.equal (shown model) [ "edsger" ] "Inactive and Dijkstra"

      let model, _ = filterTo SubscriptionFilter.Active model
      Expect.isEmpty (shown model) "No active subscription has Dijkstra"
    }

    test "an empty search shows the subscriptions of the filter" {
      let model, _ = send (Subscriptions.Searched "grace") (listed ())
      let model, _ = send (Subscriptions.Searched "") model
      Expect.equal (shown model) [ "ada"; "grace"; "alan"; "edsger" ] "All"
    }

    test "the empty text says what the filter and the search found" {
      let none = Subscriptions.emptyText SubscriptionFilter.All ""
      Expect.equal none "No subscriptions yet." "No subscription"

      Expect.equal
        (Subscriptions.emptyText SubscriptionFilter.Active "")
        "No active subscriptions."
        "No active"

      Expect.equal
        (Subscriptions.emptyText SubscriptionFilter.Inactive "")
        "No inactive subscriptions."
        "No inactive"

      Expect.equal (Subscriptions.emptyText SubscriptionFilter.All "zed") "No subscriptions match your search." "No match"
    }

    test "a change of the filter alone sends no API call and keeps the search" {
      let model, _ = send (Subscriptions.Searched "grace") (listed ())
      let model, effect = filterTo SubscriptionFilter.Inactive model

      Expect.equal effect (Some(Shell.Navigate(Router.PushUrl "/subscriptions?filter=inactive", []))) "No API call"
      Expect.equal model.Subscriptions.Search "grace" "The search"
      Expect.equal (shown model) [ "grace" ] "Inactive and Grace"
    }

    test "a move from another page to a filter loads the list and clears the search" {
      let model, _ = send (Subscriptions.Searched "grace") (listed ())
      let model, _ = Shell.update (Shell.Router(Router.LinkFollowed "/")) model
      let model, effect = Shell.update (Shell.Router(Router.LinkFollowed "/subscriptions?filter=active")) model

      Expect.equal model.Router.Route (Subscriptions SubscriptionFilter.Active) "The filter"
      Expect.equal model.Subscriptions.Search "" "No search"
      Expect.equal model.Subscriptions.Subscriptions Subscriptions.Waiting "The list loads again"

      Expect.equal
        effect
        (Some(Shell.Navigate(Router.PushUrl "/subscriptions?filter=active", [ Shell.LoadSubscriptions ])))
        "The list"
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
      let model, _ = filterTo SubscriptionFilter.Inactive (listed ())
      let model, _ = send (Subscriptions.Opened "grace") model
      let model, effect = send Subscriptions.Closed model

      Expect.isNone effect "No call"
      Expect.isNone model.Subscriptions.Sheet "No sheet"
      Expect.equal model.Router.Route (Subscriptions SubscriptionFilter.Inactive) "The filter"
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

let private actionTests =
  testList "Subscription actions" [
    test "an active subscription can be canceled or deleted, and an inactive subscription only deleted" {
      Expect.equal (Subscriptions.actions ada) [ Subscriptions.Cancel; Subscriptions.Delete ] "Active"
      Expect.equal (Subscriptions.actions grace) [ Subscriptions.Delete ] "Inactive"
      Expect.equal (Subscriptions.actions (pending "bot")) [ Subscriptions.Cancel; Subscriptions.Delete ] "Pending"
    }

    test "a cancel of a pending subscription sends the call, and the subscription becomes inactive" {
      let model, _ = start "/subscriptions"
      let model, _ = Shell.update (Shell.Subscriptions(Subscriptions.Answered(Ok [ ada; pending "bot" ]))) model
      let model, _ = send (Subscriptions.Opened "bot") model
      let model, effect = send (Subscriptions.Chosen("bot", Subscriptions.Cancel)) model

      Expect.equal effect (Some(Shell.Send [ Shell.CancelSubscription "bot" ])) "The call"

      let canceled =
        { pending "bot" with
            Status = Api.SubscriptionStatus.Canceled
            CanceledDate = Some "2026-03-01T12:00:00Z" }

      let model, _ = send (Subscriptions.Canceled("bot", Ok canceled)) model
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some { Active = 1; Pending = 0; Inactive = 1 }) "The counts follow"
    }

    test "a cancel sends the call and marks the sheet as sending" {
      let model, _ = send (Subscriptions.Opened "ada") (listed ())
      let model, effect = send (Subscriptions.Chosen("ada", Subscriptions.Cancel)) model

      Expect.equal effect (Some(Shell.Send [ Shell.CancelSubscription "ada" ])) "The call"

      Expect.equal
        (model.Subscriptions.Sheet |> Option.map _.State)
        (Some(Progress.Sending Subscriptions.Cancel))
        "Sending"
    }

    test "a cancel of an inactive subscription sends no call" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let _, effect = send (Subscriptions.Chosen("grace", Subscriptions.Cancel)) model
      Expect.isNone effect "No call"
    }

    test "a second choice while the call is out sends no call" {
      let model, _ = send (Subscriptions.Opened "ada") (listed ())
      let model, _ = send (Subscriptions.Chosen("ada", Subscriptions.Cancel)) model
      let _, effect = send (Subscriptions.Chosen("ada", Subscriptions.Delete)) model
      Expect.isNone effect "No call"
    }

    test "a canceled subscription is inactive with the date from the API, and the sheet stays open" {
      let model, _ = send (Subscriptions.Opened "ada") (listed ())
      let model, _ = send (Subscriptions.Chosen("ada", Subscriptions.Cancel)) model

      let canceled =
        { ada with
            Status = Api.SubscriptionStatus.Canceled
            CanceledDate = Some "2026-03-01T12:00:00Z" }

      let model, effect = send (Subscriptions.Canceled("ada", Ok canceled)) model

      Expect.isNone effect "No call"
      Expect.equal (Subscriptions.tryOpened model.Subscriptions) (Some canceled) "Canceled"
      Expect.equal (model.Subscriptions.Sheet |> Option.map _.State) (Some Progress.Ready) "Ready"
      Expect.equal (Subscriptions.counts model.Subscriptions) (Some(counts 1 3)) "The counts follow"
    }

    test "a cancel that did not complete shows the reason and keeps the subscription" {
      let model, _ = send (Subscriptions.Opened "ada") (listed ())
      let model, _ = send (Subscriptions.Chosen("ada", Subscriptions.Cancel)) model
      let model, _ = send (Subscriptions.Canceled("ada", Error Api.Unanswered)) model

      Expect.equal (Subscriptions.tryOpened model.Subscriptions) (Some ada) "Unchanged"

      Expect.equal
        (model.Subscriptions.Sheet |> Option.bind (fun sheet -> Subscriptions.failure sheet.State))
        (Some $"Couldn't cancel the subscription. {Api.reason Api.Unanswered}")
        "The reason"
    }

    test "a delete sends the call only after the confirmation of the shell" {
      let model, _ = send (Subscriptions.Opened "ada") (listed ())
      let choice = Shell.Subscriptions(Subscriptions.Chosen("ada", Subscriptions.Delete))
      let unconfirmed, effect = Shell.update choice model

      Expect.isNone effect "No call without the dialog"
      Expect.equal unconfirmed model "No change"

      let opened, _ = Shell.update (Shell.Opened Shell.DeleteSubscriptionDialog) model
      Expect.equal (Shell.deleteSubscriptionChosen opened) (Some choice) "The dialog confirms"

      let confirmed, effect = Shell.update choice opened
      Expect.equal effect (Some(Shell.Send [ Shell.DeleteSubscription "ada" ])) "The call"
      Expect.isNone confirmed.Overlay "The dialog closes"
    }

    test "the dialog has nothing to confirm without an open sheet" {
      let model, _ = Shell.update (Shell.Opened Shell.DeleteSubscriptionDialog) (listed ())
      Expect.isNone (Shell.deleteSubscriptionChosen model) "No sheet"
    }

    test "a deleted subscription leaves the list, and the sheet closes" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let model, _ = send (Subscriptions.Chosen("grace", Subscriptions.Delete)) model
      let model, effect = send (Subscriptions.Deleted("grace", Ok())) model

      Expect.isNone effect "No call"
      Expect.isNone model.Subscriptions.Sheet "No sheet"
      Expect.equal (shown model) [ "ada"; "alan"; "edsger" ] "Gone"
    }

    test "a delete that did not complete shows the reason and keeps the subscription" {
      let model, _ = send (Subscriptions.Opened "grace") (listed ())
      let model, _ = send (Subscriptions.Chosen("grace", Subscriptions.Delete)) model
      let model, _ = send (Subscriptions.Deleted("grace", Error(Api.Failed "Boom."))) model

      Expect.equal (shown model) [ "ada"; "grace"; "alan"; "edsger" ] "Kept"

      Expect.equal
        (model.Subscriptions.Sheet |> Option.bind (fun sheet -> Subscriptions.failure sheet.State))
        (Some "Couldn't delete the subscription. Boom.")
        "The reason"
    }

    test "a refused cancel or delete opens the refusal banner" {
      let model, _ = send (Subscriptions.Opened "ada") (listed ())
      let canceled, _ = send (Subscriptions.Canceled("ada", Error Api.Refused)) model
      Expect.equal canceled.Session.Status Session.Refused "Refused cancel"
      let deleted, _ = send (Subscriptions.Deleted("ada", Error Api.Refused)) model
      Expect.equal deleted.Session.Status Session.Refused "Refused delete"
    }
  ]

[<Tests>]
let tests =
  testList "Subscriptions page" [
    tests'
    sheetTests
    actionTests
  ]
