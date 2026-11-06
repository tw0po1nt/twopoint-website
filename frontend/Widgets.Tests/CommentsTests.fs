module TwoPoint.Site.Widgets.Tests.CommentsTests

open TwoPoint.Api
open TwoPoint.Api.Comments
open TwoPoint.Site.Widgets

open Expecto

let private pageUrl = "https://twopoint.dev/posts/a-post/"

let private known = Map [ "known@example.com", "validation-1" ]

let private start = Comments.init pageUrl known None |> fst

let private from (model: Comments.Model) (messages: Comments.Msg list) =
  messages |> List.fold (fun (model, _) msg -> Comments.update msg model) (model, None)

let private run (messages: Comments.Msg list) = from start messages

let private answer success message : ApiResponse<unit> =
  { Success = success
    Message = message
    Data = None }

let private comment id : Comment =
  { Id = id
    CreatedDate = "2026-10-17T09:30:00Z"
    Commenter = "Ada"
    Content = "First." }

let private postAnswer success message (posted: PostedComment option) : PostCommentAnswer =
  { Success = success
    Message = message
    Comment = posted }

let private posted approval : PostedComment =
  { Comment =
      { Id = "3"
        CreatedDate = "2026-10-18T10:00:00Z"
        Commenter = "Reader"
        Content = "Nice post & thanks" }
    Approval = approval }

let private filled email =
  [ Comments.NameChanged " Reader "
    Comments.EmailChanged $" {email} "
    Comments.CommentChanged " Nice post & thanks " ]

let private shown html : ApiResponse<string> =
  { Success = true
    Message = None
    Data = Some html }

let private refusedPreview : ApiResponse<string> =
  { Success = false
    Message = Some "content must be between 1 and 1000 characters long"
    Data = None }

let private request opening source : Comments.PreviewRequest = { Opening = opening; Source = source }

let private previewing = run [ Comments.CommentChanged " Use `List.map` "; Comments.PreviewSelected ] |> fst

let private html = "<p>Use <code>List.map</code></p>\n"

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

        let fixedModel, _ = from left [ Comments.EmailChanged "reader@example.com"; Comments.CommentChanged "Hi" ]

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

      test "the form is locked while a request is open, and after a validation goes through" {
        let validating, _ = run (filled "reader@example.com" @ [ Comments.Submitted ])
        let posting, _ = run (filled "known@example.com" @ [ Comments.Submitted ])
        let sent, _ = Comments.update (Comments.ValidationAnswered(answer true None)) validating

        for name, model in [ "Validating", validating; "Posting", posting; "Validation sent", sent ] do
          Expect.isTrue (Comments.isLocked model) $"{name}: the form is locked"
          let again, effect = Comments.update Comments.Submitted model
          Expect.isNone effect $"{name}: a submit sends nothing"
          Expect.equal again model $"{name}: the model does not change"

        let failed, _ = Comments.update Comments.Unanswered posting
        Expect.isFalse (Comments.isLocked failed) "After a failed request, the form is not locked"
        Expect.isFalse (Comments.isLocked start) "A new form is not locked"
      }

      test "a post that goes through empties the form, and keeps the list and the validations" {
        let model, effect =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ Comments.Submitted; Comments.PostAnswered(postAnswer true None None) ]
          )

        Expect.isNone effect "No request"

        Expect.equal
          model
          { start with
              List = Comments.Loaded [ comment "1" ]
              Status = Comments.Posted AwaitsApproval }
          "The form is empty"
      }

      test "a new comment waits for the approval and does not go in the list" {
        let model, _ =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ Comments.Submitted; Comments.PostAnswered(postAnswer true None (Some(posted AwaitsApproval))) ]
          )

        Expect.equal model.Status (Comments.Posted AwaitsApproval) "The status says that the comment shows after approval"
        Expect.equal model.List (Comments.Loaded [ comment "1" ]) "The list stays"
      }

      test "the approved comment of a trusted commenter goes in the list at once" {
        let model, effect =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ Comments.Submitted; Comments.PostAnswered(postAnswer true None (Some(posted Approved))) ]
          )

        Expect.isNone effect "No request"
        Expect.equal model.Status (Comments.Posted Approved) "The status says that the comment is posted"

        Expect.equal
          model.List
          (Comments.Loaded
            [ comment "1"
              { Id = "3"
                CreatedDate = "2026-10-18T10:00:00Z"
                Commenter = "Reader"
                Content = "Nice post & thanks" } ])
          "The comment is last in the list"

        Expect.equal model.Comment "" "The form is empty"
      }

      test "an approved comment while the list is not loaded keeps the list as it is" {
        let model, _ = run (filled "known@example.com" @ [ Comments.Submitted; Comments.PostAnswered(postAnswer true None (Some(posted Approved))) ])
        Expect.equal model.Status (Comments.Posted Approved) "The status says that the comment is posted"
        Expect.equal model.List Comments.Loading "The list is still loading"
      }

      test "a refused validation or post shows the message of the API, or a general message" {
        for email, answered in
          [ "reader@example.com", (fun message -> Comments.ValidationAnswered(answer false message))
            "known@example.com", (fun message -> Comments.PostAnswered(postAnswer false message None)) ] do
          let withMessage, _ = run (filled email @ [ Comments.Submitted; answered (Some "The commenter is banned") ])
          Expect.equal withMessage.Status (Comments.Failed "The commenter is banned") $"The message of the API for {email}"
          Expect.equal withMessage.Comment " Nice post & thanks " "The form keeps the comment"

          let general, _ = run (filled email @ [ Comments.Submitted; answered None ])
          Expect.equal general.Status (Comments.Failed "The comment did not go through. Try again later.") $"A general message for {email}"
      }

      test "a request with no answer shows that the site cannot get to the server, and the reader can submit again" {
        let model, _ = run (filled "known@example.com" @ [ Comments.Submitted; Comments.Unanswered ])
        Expect.equal model.Status (Comments.Failed "The site cannot get to the server. Try again later.") "No answer"

        let _, effect = Comments.update Comments.Submitted model
        Expect.isSome effect "A second submit sends again"
      }
    ]

    testList "preview" [
      test "a new form shows the write tab and hides the help" {
        Expect.equal start.Tab Comments.Write "The write tab"
        Expect.isFalse start.HelpShown "The help is closed"
      }

      test "the tabs change, and the return to write keeps the comment" {
        let model, effect = run [ Comments.CommentChanged "Hi"; Comments.PreviewSelected; Comments.WriteSelected ]
        Expect.isNone effect "No request"
        Expect.equal model.Tab Comments.Write "The write tab"
        Expect.equal model.Comment "Hi" "The comment stays"
      }

      test "an empty or blank text shows that there is nothing to preview, with no call" {
        for comment in [ ""; "  \n " ] do
          let model, effect = run [ Comments.CommentChanged comment; Comments.PreviewSelected ]
          Expect.isNone effect $"No request for '{comment}'"
          Expect.equal model.Tab (Comments.Preview Comments.PreviewState.Empty) "Nothing to preview"
      }

      test "the first open loads the preview of the trimmed text with one call" {
        let model, effect = run [ Comments.CommentChanged " Use `List.map` "; Comments.PreviewSelected ]
        Expect.equal model.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview is loading"
        Expect.equal effect (Some(Comments.PreviewComment(request model.Opening "Use `List.map`"))) "One call with the trimmed text"
      }

      test "a second select of the open preview tab makes no call" {
        let model, effect = from previewing [ Comments.PreviewSelected ]
        Expect.isNone effect "No request"
        Expect.equal model previewing "The model does not change"
      }

      test "the answer shows the HTML and keeps it" {
        let model, effect = from previewing [ Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", shown html) ]
        Expect.isNone effect "No request"
        Expect.equal model.Tab (Comments.Preview(Comments.PreviewState.Shown html)) "The preview shows the HTML"
        Expect.equal model.Kept (Some { Source = "Use `List.map`"; Html = html }) "The widget keeps the source and its HTML"
      }

      test "an open after no change to the text shows the kept HTML with no call" {
        let answered, _ = from previewing [ Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", shown html) ]

        for edit in [ []; [ Comments.CommentChanged "Use `List.map`  " ] ] do
          let model, effect = from answered ([ Comments.WriteSelected ] @ edit @ [ Comments.PreviewSelected ])
          Expect.isNone effect "No request"
          Expect.equal model.Tab (Comments.Preview(Comments.PreviewState.Shown html)) "The kept HTML"
      }

      test "an open after an edit calls again" {
        let answered, _ = from previewing [ Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", shown html) ]
        let model, effect = from answered [ Comments.WriteSelected; Comments.CommentChanged "Use `List.iter`"; Comments.PreviewSelected ]
        Expect.equal model.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview is loading"
        Expect.equal effect (Some(Comments.PreviewComment(request model.Opening "Use `List.iter`"))) "A call with the new text"
      }

      test "a refused or unanswered call fails, is not kept, and [try again] calls again" {
        for failure in
          [ Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", refusedPreview)
            Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", { shown html with Data = None })
            Comments.PreviewUnanswered(request previewing.Opening "Use `List.map`") ] do
          let failed, effect = from previewing [ failure ]
          Expect.isNone effect "No request"
          Expect.equal failed.Tab (Comments.Preview Comments.PreviewState.Failed) "The preview failed"
          Expect.isNone failed.Kept "The widget keeps no failed call"

          let retried, effect = from failed [ Comments.RetrySelected ]
          Expect.equal retried.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview is loading again"
          Expect.equal effect (Some(Comments.PreviewComment(request retried.Opening "Use `List.map`"))) "[try again] calls again"

          let reopened, effect = from failed [ Comments.WriteSelected; Comments.PreviewSelected ]
          Expect.equal effect (Some(Comments.PreviewComment(request reopened.Opening "Use `List.map`"))) "A new open calls again"
      }

      test "[try again] does nothing when the preview did not fail" {
        for model in [ start; previewing ] do
          let next, effect = Comments.update Comments.RetrySelected model
          Expect.isNone effect "No request"
          Expect.equal next model "The model does not change"
      }

      test "a late answer after a return to write does not replace the newer state" {
        let back, _ = from previewing [ Comments.WriteSelected ]

        for late in
          [ Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", shown html)
            Comments.PreviewUnanswered(request previewing.Opening "Use `List.map`") ] do
          let model, effect = from back [ late ]
          Expect.isNone effect "No request"
          Expect.equal model back "The write tab stays, and the widget keeps nothing"
      }

      test "a late answer for an older open does not replace the state of the newer open" {
        let reopened, _ = from previewing [ Comments.WriteSelected; Comments.CommentChanged "Use `List.iter`"; Comments.PreviewSelected ]
        let model, _ = from reopened [ Comments.PreviewAnswered(request previewing.Opening "Use `List.map`", shown html) ]
        Expect.equal model reopened "The newer open still loads"

        let answered, _ =
          from model [ Comments.PreviewAnswered(request reopened.Opening "Use `List.iter`", shown "<p>Use <code>List.iter</code></p>\n") ]

        Expect.equal answered.Tab (Comments.Preview(Comments.PreviewState.Shown "<p>Use <code>List.iter</code></p>\n")) "The newer answer shows"
      }

      test "a submit from the preview tab posts the comment, and the form goes back to write" {
        let sending, effect = from previewing [ Comments.EmailChanged "known@example.com"; Comments.Submitted ]
        Expect.equal sending.Status Comments.Posting "The form is sending"
        Expect.equal effect (Some(Comments.Post { ValidationId = "validation-1"; Comment = "Use `List.map`" })) "The post has the comment"

        let posted, _ = from sending [ Comments.PostAnswered(postAnswer true None None) ]
        Expect.equal posted.Tab Comments.Write "The empty form shows the write tab"
        Expect.equal posted.Comment "" "The form is empty"
      }

      test "[markdown help] opens and closes the help with no call" {
        let opened, effect = run [ Comments.HelpToggled ]
        Expect.isNone effect "No request"
        Expect.isTrue opened.HelpShown "The help is open"

        let closed, effect = from opened [ Comments.HelpToggled ]
        Expect.isNone effect "No request"
        Expect.isFalse closed.HelpShown "The help is closed"
      }

      test "each row of the help shows the HTML of the comment renderer" {
        for row in Comments.helpRows do
          Expect.equal
            row.ReadersSee
            (TwoPoint.Site.Render.CommentBody.htmlWithin
              TwoPoint.Site.Render.Highlight.NoLimit
              TwoPoint.Site.Render.Stage.Production
              row.YouType)
            $"The HTML of '{row.YouType}'"
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
