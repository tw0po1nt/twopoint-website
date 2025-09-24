module Comments

open TwoPoint.Api
open DateFormatter
open NewComment

open Fable.Core
open Feliz
open Feliz.UseElmish
open Elmish

open System

type Email = string

type Msg =
  | LoadComments
  | CommentsLoaded of Comment list
  | VerifyEmail of Email
  | EmailVerificationSent of Email
  | EmailVerificationComplete of Guid

type CommentsState =
  | Loading
  | NoComments
  | Comments of Comment list

type CommenterState =
  | Unverified
  | Verified of Guid
  | VerificationPending of Email

type State =
  { Uri : string
    Slug : string
    Comments : CommentsState
    Commenter : CommenterState }

let init uri slug () = 
  { Uri = uri; Slug = slug; Comments = Loading; Commenter = Unverified }, Cmd.ofMsg LoadComments

let update msg state =
  match msg with
  | LoadComments -> 
    let getComments = async {
      let! result = 
        state.Slug
        |> Comments.getCommentsForPost state.Uri
        |> Async.AwaitPromise
      return CommentsLoaded (result.Data |> Option.defaultValue [])
    }

    { state with Comments = Loading }, 
    Cmd.fromAsync getComments
  | CommentsLoaded [] -> { state with Comments = NoComments }, Cmd.none
  | CommentsLoaded comments -> { state with Comments = Comments comments }, Cmd.none
  | VerifyEmail email ->
    // todo: call api to send email verification
    state, Cmd.none
  | EmailVerificationSent pendingEmail ->
    { state with Commenter = VerificationPending pendingEmail }, Cmd.none
  | EmailVerificationComplete commenterId ->
    { state with Commenter = Verified commenterId }, Cmd.none

[<ReactComponent(exportDefault=true)>]
let Comments (uri: string, slug: string) =

  let state, _ = React.useElmish(init uri slug, update, [| box uri; box slug |])

  let loading = Html.div [
    prop.className "flex flex-row w-full justify-center animate-pulse mb-8"
    prop.children [
      Html.p [
        prop.className "text-muted"
        prop.text "Loading comments..."
      ]
    ]
  ]

  let noComments = Html.h2 [
    prop.className "leading-tighter tracking-tighter text-muted text-lg md:text-xl mb-8"
    prop.text "No comments yet. Be the first one!"
  ]

  let singleComment comment = Html.section [
    prop.className "mx-auto"
    prop.children [
      Html.article [
        prop.children [
          Html.header [
            prop.children [
              Html.div [
                prop.className "flex justify-between flex-row max-w-3xl mx-auto mt-0 mb-2"
                prop.children [
                  Html.p [
                    prop.className "text-sm md:text-base text-muted dark:text-slate-400"
                    prop.children [
                      Html.time [
                        prop.className "inline-block"
                        prop.dateTime comment.CreatedDate
                        prop.text (formatDate comment.CreatedDate)
                      ]

                      Html.text " · "

                      Html.text comment.Commenter
                    ]
                  ]
                ]
              ]

              Html.p [
                prop.className "max-w-3xl mx-auto mt-4 mb-8 text-base md:text-lg text-black dark:text-white text-justify"
                prop.text comment.Content
              ]
            ]
          ]
        ]
      ]
    ]
  ]

  let comments cs = Html.div [
    for comment in cs ->
      singleComment comment
  ]

  let verifyEmail (email: string) = Html.p [
    prop.className "bg-blue-100 text-blue-900 rounded-lg border border-blue-900 p-4 mt-4"
    prop.children [
      Html.text "We sent a verification email to "

      Html.strong [
        prop.text email
      ]

      Html.text ". Please click on the link in that email to verify your email address and post your comment!"
    ]
  ]

  Html.section [
    prop.className "relative not-prose scroll-mt-[72px]"
    prop.children [
      Html.div [
        prop.className "intersect-once motion-safe:md:intersect:animate-fade motion-safe:md:opacity-0 intersect-quarter mx-auto intercept-no-queue px-4 relative lg:pb-20 md:px-6 md:pb-16 pb-12 text-default max-w-7xl"
        prop.children [
          Html.div [
            prop.className "md:mx-auto max-w-3xl"
            prop.children [
              Html.p [
                prop.className "font-bold font-heading leading-tighter tracking-tighter text-muted text-xl md:text-2xl mb-8"
                prop.text "Comments"
              ]

              Html.div [
                prop.className "border-t dark:border-slate-700 mb-8"
              ]

              match state.Comments with
              | Loading -> loading
              | NoComments -> noComments
              | Comments cs -> comments cs

              Html.div [
                prop.className "border-t dark:border-slate-700 mb-8"
              ]

              Html.h2 [
                prop.className "font-bold font-heading leading-tighter tracking-tighter text-muted text-xl md:text-2xl mb-8"
                prop.text "Leave a comment"
              ]

              Html.p [
                prop.text "I want to hear what you think! If you have thoughts, opinions, or questions, please post them here!*"
              ]

              Html.br []

              NewComment ignore

              Html.br []

              Html.p [
                prop.className "text-sm text-muted"
                prop.text "* To prevent spam, you'll be asked to verify your email address. I also personally approve all comments posted."
              ]
              
              match state.Commenter with
              | Unverified | Verified _ -> Html.none
              | VerificationPending pendingEmail -> verifyEmail pendingEmail
            ]
          ]
        ]
      ]
    ]
  ]