module TwoPoint.Site.Widgets.Tests.CommentsTests

open TwoPoint.Api
open TwoPoint.Api.Comments
open TwoPoint.Site.Widgets

open Expecto

let private pageUrl = "https://twopoint.dev/posts/a-post/"

let private known = Map [ "known@example.com", "validation-1" ]

let private start = Comments.init pageUrl known None |> fst

let private run (messages: Comments.Msg list) =
  messages |> List.fold (fun (model, _) msg -> Comments.update msg model) (start, None)

let private answer success message : ApiResponse<unit> =
  { Success = success
    Message = message
    Data = None }

let private comment id : Comment =
  { Id = id
    CreatedDate = "2026-10-17T09:30:00Z"
    Commenter = "Ada"
    Content = "First." }

let private filled email =
  [ Comments.NameChanged " Reader "
    Comments.EmailChanged $" {email} "
    Comments.CommentChanged " Nice post & thanks " ]

let private returned: Comments.Return =
  { Email = "reader@example.com"
    Name = "Reader"
    Comment = "Nice post"
    ValidationId = "validation-2" }

[<Tests>]
let tests =
  testList "Comments" [
    testList "init" [
      test "a new widget loads the comments, and shows no error and no result" {
        let model, effect = Comments.init pageUrl known None
        Expect.equal effect (Some Comments.Load) "The widget loads the comments"
        Expect.equal model.List Comments.Loading "The list is loading"
        Expect.equal model.Status Comments.Editing "No result"
        Expect.isFalse (Comments.showsEmailError model || Comments.showsCommentError model) "No error"
      }

      test "the return from the validation email fills the form and adds the validation ID" {
        let model, effect = Comments.init pageUrl known (Some returned)
        Expect.equal effect (Some Comments.Load) "The widget loads the comments"
        Expect.equal (model.Email, model.Name, model.Comment) ("reader@example.com", "Reader", "Nice post") "The form has the comment"
        Expect.equal model.Validations (known.Add("reader@example.com", "validation-2")) "The validation ID is beside the others"
        Expect.equal model.Status Comments.Validated "The status tells the reader to post the comment"
      }
    ]

    testList "parse" [
      test "the query of the link in the validation email gives the comment back" {
        Expect.equal
          (Comments.parse
            [ "email", " reader@example.com "
              "name", "Reader"
              "comment", "Nice post"
              "commenterValidationId", "validation-2" ])
          (Some returned)
          "Each value of the query"
      }

      test "a query with no name gives an empty name" {
        let parsed =
          Comments.parse [ "email", "reader@example.com"; "comment", "Nice post"; "commenterValidationId", "validation-2" ]

        Expect.equal parsed (Some { returned with Name = "" }) "The name is empty"
      }

      test "a query with no email address, comment, or validation ID gives nothing" {
        let full =
          [ "email", "reader@example.com"; "comment", "Nice post"; "commenterValidationId", "validation-2" ]

        Expect.isNone (Comments.parse []) "No query"

        for key, _ in full do
          Expect.isNone (Comments.parse (full |> List.filter (fun (name, _) -> name <> key))) $"No '{key}'"
          Expect.isNone (Comments.parse (full |> List.map (fun (name, value) -> name, (if name = key then " " else value)))) $"An empty '{key}'"
      }
    ]

    testList "update" [
      test "the comments of the API go in the list" {
        let model, effect = run [ Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1"; comment "2" ] } ]
        Expect.isNone effect "No request"
        Expect.equal model.List (Comments.Loaded [ comment "1"; comment "2" ]) "The list has the comments"
      }

      test "an answer with no data gives an empty list" {
        let model, _ = run [ Comments.ListAnswered { Success = true; Message = None; Data = None } ]
        Expect.equal model.List (Comments.Loaded []) "The list is empty"
      }

      test "a refused list or no answer shows the reason" {
        let refused, _ = run [ Comments.ListAnswered { Success = false; Message = Some "No such post"; Data = None } ]
        Expect.equal refused.List (Comments.NotLoaded "No such post") "The message of the API"

        let general, _ = run [ Comments.ListAnswered { Success = false; Message = None; Data = None } ]
        Expect.equal general.List (Comments.NotLoaded "The comments did not load. Try again later.") "A general message"

        let unanswered, _ = run [ Comments.ListUnanswered ]
        Expect.equal unanswered.List (Comments.NotLoaded "The site cannot get to the server. Try again later.") "No answer"
      }

      test "a submit with a new email address sends the validation with a link back to the panel" {
        let model, effect = run (filled "reader@example.com" @ [ Comments.Submitted ])
        Expect.equal model.Status Comments.Validating "The form is sending"

        Expect.equal
          effect
          (Some(
            Comments.Validate
              { EmailAddress = "reader@example.com"
                Name = Some "Reader"
                RedirectUri = "https://twopoint.dev/posts/a-post/?email=reader%40example.com&name=Reader&comment=Nice%20post%20%26%20thanks#comments" }
          ))
          "The validation has the trimmed values and the redirect URI"
      }

      test "the link back omits an empty name" {
        let _, effect = run [ Comments.EmailChanged "reader@example.com"; Comments.CommentChanged "Hi"; Comments.Submitted ]

        Expect.equal
          effect
          (Some(
            Comments.Validate
              { EmailAddress = "reader@example.com"
                Name = None
                RedirectUri = "https://twopoint.dev/posts/a-post/?email=reader%40example.com&comment=Hi#comments" }
          ))
          "No name in the validation or the redirect URI"
      }

      test "a submit with a known email address posts the comment with its validation ID" {
        let model, effect = run (filled "known@example.com" @ [ Comments.Submitted ])
        Expect.equal model.Status Comments.Posting "The form is sending"
        Expect.equal effect (Some(Comments.Post { ValidationId = "validation-1"; Comment = "Nice post & thanks" })) "The post has the trimmed comment"
      }

      test "a submit with an email address that is not valid or no comment sends nothing and shows each error" {
        let model, effect = run [ Comments.EmailChanged "reader@"; Comments.CommentChanged "  "; Comments.Submitted ]
        Expect.isNone effect "No request"
        Expect.isTrue (Comments.showsEmailError model) "The email error shows"
        Expect.isTrue (Comments.showsCommentError model) "The comment error shows"
        Expect.equal model.Status Comments.Editing "The form is not sending"
      }

      test "the errors show after the reader leaves a field that is not valid, and go when the field is valid" {
        let left, _ = run [ Comments.EmailChanged "reader@"; Comments.EmailLeft; Comments.CommentChanged " "; Comments.CommentLeft ]
        Expect.isTrue (Comments.showsEmailError left) "The email error shows"
        Expect.isTrue (Comments.showsCommentError left) "The comment error shows"

        let fixedModel, _ =
          [ Comments.EmailChanged "reader@example.com"; Comments.CommentChanged "Hi" ]
          |> List.fold (fun (model, _) msg -> Comments.update msg model) (left, None)

        Expect.isFalse (Comments.showsEmailError fixedModel || Comments.showsCommentError fixedModel) "No error"
      }

      test "empty fields that the reader leaves show no error" {
        let model, _ = run [ Comments.EmailLeft; Comments.CommentLeft ]
        Expect.isFalse (Comments.showsEmailError model || Comments.showsCommentError model) "No error"
      }

      test "a second submit while the form is sending sends nothing" {
        for email in [ "reader@example.com"; "known@example.com" ] do
          let sending, _ = run (filled email @ [ Comments.Submitted ])
          let model, effect = Comments.update Comments.Submitted sending
          Expect.isNone effect $"No second request for {email}"
          Expect.equal model sending "The model does not change"
      }

      test "a validation that goes through keeps the form and tells the reader to open the email" {
        let model, effect = run (filled "reader@example.com" @ [ Comments.Submitted; Comments.ValidationAnswered(answer true None) ])
        Expect.isNone effect "No request"
        Expect.equal model.Status (Comments.ValidationSent "reader@example.com") "The status names the address"
        Expect.equal model.Comment " Nice post & thanks " "The form keeps the comment"
      }

      test "a post that goes through empties the form, and keeps the list and the validations" {
        let model, effect =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ Comments.Submitted; Comments.PostAnswered(answer true None) ]
          )

        Expect.isNone effect "No request"

        Expect.equal
          model
          { start with
              List = Comments.Loaded [ comment "1" ]
              Status = Comments.Posted }
          "The form is empty"
      }

      test "a refused validation or post shows the message of the API, or a general message" {
        for email, answered in
          [ "reader@example.com", Comments.ValidationAnswered
            "known@example.com", Comments.PostAnswered ] do
          let withMessage, _ = run (filled email @ [ Comments.Submitted; answered (answer false (Some "The commenter is banned")) ])
          Expect.equal withMessage.Status (Comments.Failed "The commenter is banned") $"The message of the API for {email}"
          Expect.equal withMessage.Comment " Nice post & thanks " "The form keeps the comment"

          let general, _ = run (filled email @ [ Comments.Submitted; answered (answer false None) ])
          Expect.equal general.Status (Comments.Failed "The comment did not go through. Try again later.") $"A general message for {email}"
      }

      test "a request with no answer shows that the site cannot get to the server, and the reader can submit again" {
        let model, _ = run (filled "known@example.com" @ [ Comments.Submitted; Comments.Unanswered ])
        Expect.equal model.Status (Comments.Failed "The site cannot get to the server. Try again later.") "No answer"

        let _, effect = Comments.update Comments.Submitted model
        Expect.isSome effect "A second submit sends again"
      }
    ]

    test "a comment shows the date part of its time" {
      Expect.equal (Comments.shownDate "2026-10-17T09:30:00.1234567+00:00") "2026-10-17" "The date part"
      Expect.equal (Comments.shownDate "today") "today" "A short value stays"
    }

    test "the count tells the number of comments" {
      Expect.equal (Comments.countText 0) "0 comments. Be the first." "No comment"
      Expect.equal (Comments.countText 1) "1 comment" "One comment"
      Expect.equal (Comments.countText 12) "12 comments" "Comments"
    }
  ]
