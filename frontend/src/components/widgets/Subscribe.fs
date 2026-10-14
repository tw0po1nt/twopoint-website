module SubscribeUnsubscribe 

open TwoPoint.Api.Subscriptions
open Util

open Fable.Core
open Feliz
open Feliz.UseElmish
open Elmish

// Types
type Email = string

type Name = string

type Msg =
  | SetEmail of Email
  | SetEmailIsFocused of bool
  | SetEmailIsDirty
  | SetName of Name
  | Subscribe
  | SubscriptionFailed of string
  | SubscriptionSucceeded of Email

type SubscriptionStatus =
  | NotStarted
  | Pending of Email
  | Failed of string
  | Succeeded of Email

type SubscriptionState =
  { Email : Email
    EmailIsFocused : bool
    EmailIsDirty : bool
    Name : Name
    Status : SubscriptionStatus }
  
  static member Initial = 
    { Email = ""
      EmailIsFocused = false
      EmailIsDirty = false
      Name = ""
      Status = NotStarted }

type State =
  { Uri : string
    Subscription : SubscriptionState }

let init uri () =
  { Uri = uri
    Subscription = SubscriptionState.Initial 
  },
  Cmd.none

let update state = function
  | SetEmail email ->
    { state with Subscription.Email = email },
    Cmd.none
  | SetEmailIsFocused focused ->
    { state with Subscription.EmailIsFocused = focused },
    Cmd.none
  | SetEmailIsDirty ->
    { state with Subscription.EmailIsDirty = true },
    Cmd.none
  | SetName name ->
    { state with Subscription.Name = name },
    Cmd.none
  | Subscribe ->
    let subscribe newSubscription = async {
      let! result =
        newSubscription
        |> Subscriptions.subscribe state.Uri
        |> Async.AwaitPromise

      if result.Success
      then
        return SubscriptionSucceeded newSubscription.EmailAddress
      else
        return SubscriptionFailed (result.Message |> Option.defaultValue "Unable to subscribe. Please try again later.")
    }

    let name =
      match state.Subscription.Name with
      | "" -> None
      | n -> Some n
    
    let newSubscription =
      { EmailAddress = state.Subscription.Email
        Name = name }

    { state with Subscription.Status = Pending state.Subscription.Email },
    Cmd.fromAsync (subscribe newSubscription)
  | SubscriptionFailed err ->
    { state with Subscription.Status = Failed err },
    Cmd.none
  | SubscriptionSucceeded email ->
    { state with Subscription = { SubscriptionState.Initial with Status = Succeeded email } },
    Cmd.none

[<ReactComponent(exportDefault=true)>]
let Subscribe (apiUri: string) =

  let state, dispatch = React.useElmish(init apiUri, flip update, [| box apiUri |])

  let isEmailValid (email : string) = Constants.emailRegex.IsMatch email

  let errorMessage (msg: string) = Html.p [
    prop.className "bg-red-100 text-red-900 rounded-lg border border-red-900 p-4 mt-4"
    prop.children [
      Html.text msg
    ]
  ]

  let success = Html.p [
    prop.className "bg-green-100 text-green-900 rounded-lg border border-green-900 p-4 mt-4"
    prop.children [
      Html.text "You've successfully subscribed! Keep an eye out for my next post!" 
    ]
  ]

  Html.div [
    Html.div [
      prop.className "flex flex-col text-left backdrop-blur bg-white border dark:bg-zinc-900 rounded-lg border-gray-200 dark:border-gray-700 lg:p-8 max-w-5xl mx-auto p-4 shadow sm:p-6 w-full";
      prop.children [
        Html.form [
          prop.children [
            Html.div [
              prop.className "mb-6"
              prop.children [
                Html.label [
                  prop.className "block font-medium text-sm mb-2"
                  prop.htmlFor "email"
                  prop.text "Email (required)"
                ]

                Html.input [
                  prop.className "block bg-white border border-gray-200 dark:bg-zinc-900 dark:border-gray-700 px-4 py-3 rounded-lg text-md w-full mb-2"
                  prop.id "email"
                  prop.name "Email"
                  prop.type' "text"
                  prop.autoComplete "on"
                  prop.placeholder ""
                  prop.value state.Subscription.Email 
                  prop.onFocus (ignore >> konst true >> SetEmailIsFocused >> dispatch)
                  prop.onBlur (ignore >> konst false >> SetEmailIsFocused >> dispatch)
                  prop.onTextChange (fun text ->
                    SetEmailIsDirty |> dispatch
                    text |> SetEmail |> dispatch
                  )
                ]

                if state.Subscription.Email |> Constants.emailRegex.IsMatch |> not
                  && state.Subscription.EmailIsDirty && state.Subscription.EmailIsFocused |> not
                then
                  Html.p [
                    prop.className "block font-medium text-sm text-primary"
                    prop.text "Must provide a valid email address" 
                  ]
              ]
            ]

            Html.div [
              prop.className "mb-6"
              prop.children [
                Html.label [
                  prop.className "block font-medium text-sm mb-2"
                  prop.htmlFor "name"
                  prop.text "Name (optional)"
                ]

                Html.input [
                  prop.className "block bg-white border border-gray-200 dark:bg-zinc-900 dark:border-gray-700 px-4 py-3 rounded-lg text-md w-full mb-2"
                  prop.id "name"
                  prop.name "name"
                  prop.type' "text"
                  prop.autoComplete "on"
                  prop.placeholder ""
                  prop.value state.Subscription.Name
                  prop.onTextChange (SetName >> dispatch) 
                  prop.maxLength 256
                ]
              ]
            ]

            Html.div [
              prop.className "grid mt-10"
              prop.children [
                Html.button [
                  prop.className (
                    if isEmailValid state.Subscription.Email && not state.Subscription.Status.IsPending
                    then "btn-primary" 
                    else "btn-tertiary dark:hover:bg-zinc-800"
                  )
                  prop.text "Subscribe"
                  prop.type' "button"
                  prop.disabled state.Subscription.Status.IsPending
                  prop.onClick (fun _ ->
                    if state.Subscription.Email |> isEmailValid |> not
                    then SetEmailIsDirty |> dispatch
                    else Subscribe |> dispatch
                  )
                ]
              ]
            ]

            Html.p [
              prop.className "mt-4 text-sm text-center text-muted dark:text-slate-400"
              prop.children [
                Html.text "The site uses your email address only to send the newsletter. See the "
                Html.a [
                  prop.className "underline hover:text-gray-700 dark:hover:text-slate-200"
                  prop.href "/privacy"
                  prop.text "privacy policy"
                ]
                Html.text "."
              ]
            ]
          ]
        ]
      ] 
    ]

    match state.Subscription.Status with
    | NotStarted | Pending _ -> Html.none
    | Failed error -> errorMessage error
    | Succeeded _ -> success
  ]
