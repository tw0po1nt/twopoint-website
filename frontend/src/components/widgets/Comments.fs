module Comments

open TwoPoint.Api
open DateFormatter
open NewComment

open Browser.Dom
open Browser.WebStorage
open Browser.Url

open Fable.Core
open Feliz
open Feliz.UseElmish
open Elmish
open FsToolkit.ErrorHandling

open System

// Types

type Email = string

type ClearFormFn = unit -> unit

type Msg =
  | LoadComments
  | CommentsLoaded of Comment list
  | CommenterValidationSent of CommenterValidation
  | CommenterValidationFailed of string
  | PostComment of NewCommentData * ClearFormFn
  | CommentPosted
  | CommentPostFailed of string

type CommentsState =
  | Loading
  | NoComments
  | Comments of Comment list

type CommenterState =
  | VerificationNotLoading
  | VerificationPending of Email
  | VerificationFailed of string

type PostCommentState =
  | NotStarted
  | Posting
  | Posted
  | PostFailed of string

type State =
  { Uri : string
    Slug : string
    Comments : CommentsState
    Commenter : CommenterState
    InitialCommentData : NewCommentData option
    PostComment : PostCommentState
    ClearForm : unit -> unit }

// Helpers
let validationStorageKey = "commenterValidations"

let getValidationId (email: string) : string option =
  validationStorageKey
  |> localStorage.getItem
  |> Option.ofObj
  |> Option.bind (fun json ->
    try
      let parsed = JS.JSON.parse json
      let value = JsInterop.(?) parsed email
      value |> Option.ofObj |> Option.map string
    with
    | _ -> None
  )

let setValidationId (email: string) (validationId: string) =
  let existingData =
    validationStorageKey
    |> localStorage.getItem
    |> Option.ofObj
    |> Option.bind (fun json ->
      try
        Some (JS.JSON.parse json)
      with
      | _ -> None
    )
    |> Option.defaultValue (JsInterop.createObj [])
  
  JsInterop.(?<-) existingData email validationId
  localStorage.setItem(validationStorageKey, JS.JSON.stringify existingData)

let appendQuery kvps (uri: Uri) =
  let search = URLSearchParams.Create uri.Query
  for k, v in kvps do
    search.append(k, v)
  Uri $"{uri}?{search}"

let appendFragment (fragment: string) (uri: Uri) =
  if not (String.IsNullOrEmpty uri.Fragment) then
    uri
  else
    let frag =
      if fragment.StartsWith "#" 
      then fragment
      else "#" + fragment
    
    // Build URL as: scheme://host/path?query#fragment
    // Use AbsoluteUri and just append the fragment since query is already there
    let uriString = uri.AbsoluteUri + frag
    Uri uriString

let clearQueryParams () =
  let currentUri = Uri window.location.href
  let fragment = if String.IsNullOrEmpty currentUri.Fragment then "" else currentUri.Fragment
  let pathWithFragment = currentUri.PathAndQuery.Split('?').[0] + fragment
  history.replaceState(null, "", pathWithFragment)

let getCleanUri () =
  // Get current URI without query params or fragment
  // Use window.location directly to ensure we get the correct host:port
  let cleanUrl = window.location.protocol + "//" + window.location.host + window.location.pathname
  Uri cleanUrl

// Elmish

let init uri slug () =

  let currentUri = Uri window.location.href
  let search = URLSearchParams.Create currentUri.Query

  let initialCommentData = option {
    let! email = search.get "email"
    let name = search.get "name" |> Option.defaultValue ""
    let! comment = search.get "comment"
    let! validationId = search.get "commenterValidationId"
    return
      { Email = email
        Name = name
        Comment = comment }, validationId
  }

  let initialState =
    { Uri = uri
      Slug = slug
      Comments = Loading
      Commenter = VerificationNotLoading
      InitialCommentData = None
      PostComment = NotStarted
      ClearForm = ignore }

  // Clear query params from URL if we found any comment-related data
  let initialState =
    match initialCommentData with
    | Some (initialCommentData, commenterValidationId) ->
      clearQueryParams()
      setValidationId initialCommentData.Email commenterValidationId
      { initialState with InitialCommentData = Some initialCommentData }
    | None -> initialState

  initialState,
  Cmd.ofMsg LoadComments

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
  | CommenterValidationSent validation ->
    { state with Commenter = VerificationPending validation.EmailAddress }, Cmd.none
  | CommenterValidationFailed error ->
    { state with Commenter = VerificationFailed error }, Cmd.none
  | PostComment (newCommentData, clearForm) ->
    let postComment newComment = async {
      let! result =
        (state.Slug, newComment)
        ||> Comments.postComment state.Uri
        |> Async.AwaitPromise
      
      if result.Success 
      then
        return CommentPosted 
      else
        return CommentPostFailed (result.Message |> Option.defaultValue "Unable to validate your information. Please try again later.")
    }

    let validateCommenter commenterValidation = async {
      let! result = 
        commenterValidation
        |> Comments.validateCommenter state.Uri
        |> Async.AwaitPromise
      
      if result.Success 
      then
        return CommenterValidationSent commenterValidation
      else
        return CommenterValidationFailed (result.Message |> Option.defaultValue "Unable to validate your information. Please try again later.")
    }

    // Look up the validation ID for this specific email
    match getValidationId newCommentData.Email with
    | Some validationId ->
      let newCommentData =
        { NewComment.ValidationId = validationId
          Comment = newCommentData.Comment }

      { state with PostComment = Posting; ClearForm = clearForm }, 
      Cmd.fromAsync (postComment newCommentData)
    | None ->
      // If no validation ID exists for this email, treat as unverified
      let name =
        match newCommentData.Name with
        | "" -> None
        | n -> Some n

      let redirectUri =
        getCleanUri()
        |> appendQuery [
          yield "email", newCommentData.Email
          yield "comment", newCommentData.Comment
          match name with
          | Some name -> yield "name", name
          | _ -> ()
        ]
        |> appendFragment "leave-a-comment"
      let validation =
        { CommenterValidation.EmailAddress = newCommentData.Email
          Name = name
          RedirectUri = redirectUri.ToString() }

      { state with Commenter = VerificationPending newCommentData.Email },
      Cmd.fromAsync (validateCommenter validation)
  | CommentPosted  ->
    state.ClearForm()
    { state with 
        PostComment = Posted
        InitialCommentData = None },
    Cmd.none
  
  | CommentPostFailed error ->
    { state with PostComment = PostFailed error },
    Cmd.none

[<ReactComponent(exportDefault=true)>]
let Comments (uri: string, slug: string) =

  let state, dispatch = React.useElmish(init uri slug, update, [| box uri; box slug |])

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
    prop.className "text-muted text-lg md:text-xl mb-8"
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
                prop.className "flex justify-between flex-row max-w-5xl mx-auto mt-0 mb-2"
                prop.children [
                  Html.p [
                    prop.className "text-sm md:text-base text-muted dark:text-slate-400"
                    prop.children [
                      Html.time [
                        prop.className "inline-block"
                        prop.dateTime comment.CreatedDate
                        prop.text (formatRelativeDate comment.CreatedDate)
                      ]

                      Html.text " · "

                      Html.text comment.Commenter
                    ]
                  ]
                ]
              ]

              Html.p [
                prop.className "max-w-5xl mx-auto mt-4 mb-8 text-base md:text-lg text-black dark:text-white text-justify"
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

  let errorMessage (msg: string) = Html.p [
    prop.className "bg-red-100 text-red-900 rounded-lg border border-red-900 p-4 mt-4"
    prop.children [
      Html.text msg
    ]
  ]

  let commentPosted = Html.p [
    prop.className "bg-green-100 text-green-900 rounded-lg border border-green-900 p-4 mt-4"
    prop.children [
      Html.text "Your comment has been posted! It will appear here once I have have reviewed and approved it."
    ]
  ]

  Html.section [
    prop.className "relative not-prose scroll-mt-[72px]"
    prop.children [
      Html.div [
        prop.className "intersect-once motion-safe:md:intersect:animate-fade motion-safe:md:opacity-0 intersect-quarter mx-auto intercept-no-queue relative lg:pb-20 md:pb-16 pb-12 text-default max-w-7xl"
        prop.children [
          Html.div [
            prop.className "md:mx-auto max-w-5xl px-6"
            prop.children [
              Html.p [
                prop.className "font-bold font-heading text-xl md:text-2xl mb-8"
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
                prop.id "leave-a-comment"
                prop.className "font-bold font-heading text-xl md:text-2xl mb-8"
                prop.text "Leave a comment"
              ]

              Html.p [
                prop.className "mb-8"
                prop.text "I want to hear what you think! If you have thoughts, opinions, or questions, please post them here!*"
              ]

              NewComment(
                state.PostComment.IsPosting || state.Commenter.IsVerificationPending, 
                state.InitialCommentData,
                fun (data, clearForm) -> PostComment(data, clearForm) |> dispatch
              )

              Html.p [
                prop.className "text-sm text-muted mt-8"
                prop.text "* To prevent spam, you'll be asked to verify your email address. I also personally approve all comments posted."
              ]
              
              match state.Commenter with
              | VerificationNotLoading -> Html.none
              | VerificationPending pendingEmail -> verifyEmail pendingEmail
              | VerificationFailed error -> errorMessage error

              match state.PostComment with
              | NotStarted | Posting -> Html.none
              | Posted -> commentPosted
              | PostFailed error -> errorMessage error
            ]
          ]
        ]
      ]
    ]
  ]