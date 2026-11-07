module TwoPoint.Site.Widgets.Tests.CommentsTests

open TwoPoint.Api
open TwoPoint.Api.Comments
open TwoPoint.Site.Widgets

open Expecto

let private pageUrl = "https://twopoint.dev/posts/a-post/"

let private known = Map [ "known@example.com", "validation-1" ]

let private page (edit: Comments.Edit) = Comments.Edited(Comments.Form.Page, edit)

let private previewAnswered (request, response) = page (Comments.PreviewAnswered(request, response))

let private previewUnanswered request = page (Comments.PreviewUnanswered request)

let private submitted = Comments.Submitted Comments.Form.Page

let private validationAnswered response = Comments.ValidationAnswered(Comments.Form.Page, response)

let private postAnswered answer = Comments.PostAnswered(Comments.Form.Page, answer)

let private unanswered = Comments.Unanswered Comments.Form.Page

let private pageValidate validation = Comments.Validate(Comments.Form.Page, validation)

let private start = Comments.init pageUrl known None None |> fst

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
    Content = "First."
    ReplyTo = None
    Writer = Commenter }

let private postAnswer success message (posted: PostedComment option) : PostCommentAnswer =
  { Success = success
    Message = message
    Comment = posted }

let private posted approval : PostedComment =
  { Comment =
      { Id = "3"
        CreatedDate = "2026-10-18T10:00:00Z"
        Commenter = "Reader"
        Content = "Nice post & thanks"
        ReplyTo = None
        Writer = Commenter }
    Approval = approval }

let private filled email =
  [ Comments.NameChanged " Reader "
    Comments.EmailChanged $" {email} "
    page (Comments.TextChanged " Nice post & thanks ") ]

let private shown html : ApiResponse<string> =
  { Success = true
    Message = None
    Data = Some html }

let private refusedPreview : ApiResponse<string> =
  { Success = false
    Message = Some "content must be between 1 and 1000 characters long"
    Data = None }

let private request opening source : Comments.PreviewRequest = { Opening = opening; Source = source }

let private previewing = run [ page (Comments.TextChanged " Use `List.map` "); page Comments.PreviewSelected ] |> fst

let private html = "<p>Use <code>List.map</code></p>\n"

let private returned: Comments.Return =
  { Email = "reader@example.com"
    Name = "Reader"
    Comment = "Nice post"
    ValidationId = "validation-2"
    ReplyTo = None }

let private at id (day: int) (replyTo: string option) (writer: Writer) : Comment =
  { Id = id
    CreatedDate = $"2026-10-%02d{day}T09:30:00.0000000Z"
    Commenter = (match writer with
                 | Commenter -> $"Reader {id}"
                 | Author -> "")
    Content = $"<p>{id}</p>\n"
    ReplyTo = replyTo
    Writer = writer }

let private topLevel id day = at id day None Commenter

let private reply id day parent = at id day (Some parent) Commenter

let private loaded (comments: Comment list) =
  Comments.ListAnswered { Success = true; Message = None; Data = Some comments }

let private shape (model: Comments.Model) =
  Comments.shownThreads model.Expanded model.List
  |> List.map (fun thread -> thread.Comment.Id, thread.HiddenEarlier, thread.Replies |> List.map _.Id)

/// A thread with 4 replies and a thread with 3 replies. The list mixes the dates.
let private longThreads =
  [ reply "a3" 7 "a"
    topLevel "b" 2
    reply "b2" 6 "b"
    reply "a1" 3 "a"
    topLevel "a" 1
    reply "b1" 4 "b"
    reply "a2" 5 "a"
    reply "a4" 9 "a"
    reply "b3" 8 "b" ]

let private linked (link: Comments.Link) = Comments.init pageUrl known None (Some link) |> fst

let private replyTo (thread: string) (edit: Comments.Edit) = Comments.Edited(Comments.Form.Reply thread, edit)

let private openForm (model: Comments.Model) =
  model.Reply |> Option.map (fun reply -> reply.Thread, reply.Editor.Text)

let private withThreads = run [ loaded [ topLevel "a" 1; reply "a1" 2 "a"; topLevel "b" 3 ] ] |> fst

let private repliedTo (approval: Approval) : PostedComment =
  { Comment = { reply "a2" 4 "a" with Commenter = "Reader" }
    Approval = approval }

let private returnedReply = { returned with Comment = "Me too"; ReplyTo = Some "a" }

let private verified =
  from withThreads [ Comments.NameChanged "Reader"; Comments.EmailChanged "known@example.com" ] |> fst

[<Tests>]
let tests =
  testList "Comments" [
    testList "init" [
      test "a new widget loads the comments, and shows no error and no result" {
        let model, effect = Comments.init pageUrl known None None
        Expect.equal effect (Some Comments.Load) "The widget loads the comments"
        Expect.equal model.List Comments.Loading "The list is loading"
        Expect.equal model.PageStatus Comments.Editing "No result"
        Expect.isFalse (Comments.showsEmailError model || Comments.showsTextError model.Page) "No error"
      }

      test "the return from the validation email fills the form and adds the validation ID" {
        let model, effect = Comments.init pageUrl known (Some returned) None
        Expect.equal effect (Some Comments.Load) "The widget loads the comments"
        Expect.equal (model.Email, model.Name, model.Page.Text) ("reader@example.com", "Reader", "Nice post") "The form has the comment"
        Expect.equal model.Validations (known.Add("reader@example.com", "validation-2")) "The validation ID is beside the others"
        Expect.equal model.PageStatus Comments.Validated "The status tells the reader to post the comment"
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
        Expect.equal general.List (Comments.NotLoaded "Couldn't load comments. Try again later.") "A general message"

        let unanswered, _ = run [ Comments.ListUnanswered ]
        Expect.equal unanswered.List (Comments.NotLoaded "Couldn't reach the server. Try again later.") "No answer"
      }

      test "a submit with a new email address sends the validation with a link back to the panel" {
        let model, effect = run (filled "reader@example.com" @ [ submitted ])
        Expect.equal model.PageStatus Comments.Validating "The form is sending"

        Expect.equal
          effect
          (Some(
            pageValidate
              { EmailAddress = "reader@example.com"
                Name = Some "Reader"
                RedirectUri = "https://twopoint.dev/posts/a-post/?email=reader%40example.com&name=Reader&comment=Nice%20post%20%26%20thanks#comments" }
          ))
          "The validation has the trimmed values and the redirect URI"
      }

      test "the link back omits an empty name" {
        let _, effect = run [ Comments.EmailChanged "reader@example.com"; page (Comments.TextChanged "Hi"); submitted ]

        Expect.equal
          effect
          (Some(
            pageValidate
              { EmailAddress = "reader@example.com"
                Name = None
                RedirectUri = "https://twopoint.dev/posts/a-post/?email=reader%40example.com&comment=Hi#comments" }
          ))
          "No name in the validation or the redirect URI"
      }

      test "a submit with a known email address posts the comment with its validation ID" {
        let model, effect = run (filled "known@example.com" @ [ submitted ])
        Expect.equal model.PageStatus Comments.Posting "The form is sending"
        Expect.equal effect (Some(Comments.Post(Comments.Form.Page, { ValidationId = "validation-1"; Comment = "Nice post & thanks"; ReplyTo = None }))) "The post has the trimmed comment"
      }

      test "a submit with an email address that is not valid or no comment sends nothing and shows each error" {
        let model, effect = run [ Comments.EmailChanged "reader@"; page (Comments.TextChanged "  "); submitted ]
        Expect.isNone effect "No request"
        Expect.isTrue (Comments.showsEmailError model) "The email error shows"
        Expect.isTrue (Comments.showsTextError model.Page) "The comment error shows"
        Expect.equal model.PageStatus Comments.Editing "The form is not sending"
      }

      test "the errors show after the reader leaves a field that is not valid, and go when the field is valid" {
        let left, _ = run [ Comments.EmailChanged "reader@"; Comments.EmailLeft; page (Comments.TextChanged " "); page Comments.TextLeft ]
        Expect.isTrue (Comments.showsEmailError left) "The email error shows"
        Expect.isTrue (Comments.showsTextError left.Page) "The comment error shows"

        let fixedModel, _ = from left [ Comments.EmailChanged "reader@example.com"; page (Comments.TextChanged "Hi") ]

        Expect.isFalse (Comments.showsEmailError fixedModel || Comments.showsTextError fixedModel.Page) "No error"
      }

      test "empty fields that the reader leaves show no error" {
        let model, _ = run [ Comments.EmailLeft; page Comments.TextLeft ]
        Expect.isFalse (Comments.showsEmailError model || Comments.showsTextError model.Page) "No error"
      }

      test "a second submit while the form is sending sends nothing" {
        for email in [ "reader@example.com"; "known@example.com" ] do
          let sending, _ = run (filled email @ [ submitted ])
          let model, effect = Comments.update submitted sending
          Expect.isNone effect $"No second request for {email}"
          Expect.equal model sending "The model does not change"
      }

      test "a validation that goes through keeps the form and tells the reader to open the email" {
        let model, effect = run (filled "reader@example.com" @ [ submitted; validationAnswered(answer true None) ])
        Expect.isNone effect "No request"
        Expect.equal model.PageStatus (Comments.ValidationSent "reader@example.com") "The status names the address"
        Expect.equal model.Page.Text " Nice post & thanks " "The form keeps the comment"
      }

      test "the form is locked while a request is open, and after a validation goes through" {
        let validating, _ = run (filled "reader@example.com" @ [ submitted ])
        let posting, _ = run (filled "known@example.com" @ [ submitted ])
        let sent, _ = Comments.update (validationAnswered(answer true None)) validating

        for name, model in [ "Validating", validating; "Posting", posting; "Validation sent", sent ] do
          Expect.isTrue (Comments.isLocked model.PageStatus) $"{name}: the form is locked"
          let again, effect = Comments.update submitted model
          Expect.isNone effect $"{name}: a submit sends nothing"
          Expect.equal again model $"{name}: the model does not change"

        let failed, _ = Comments.update unanswered posting
        Expect.isFalse (Comments.isLocked failed.PageStatus) "After a failed request, the form is not locked"
        Expect.isFalse (Comments.isLocked start.PageStatus) "A new form is not locked"
      }

      test "a post that goes through empties the text, and keeps the name, the email address, the list, and the validations" {
        let model, effect =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ submitted; postAnswered(postAnswer true None None) ]
          )

        Expect.isNone effect "No request"

        Expect.equal
          model
          { start with
              List = Comments.Loaded [ comment "1" ]
              PageStatus = Comments.Posted AwaitsApproval
              Name = " Reader "
              Email = " known@example.com " }
          "The text is empty"
      }

      test "a new comment waits for the approval and does not go in the list" {
        let model, _ =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ submitted; postAnswered(postAnswer true None (Some(posted AwaitsApproval))) ]
          )

        Expect.equal model.PageStatus (Comments.Posted AwaitsApproval) "The status says that the comment shows after approval"
        Expect.equal model.List (Comments.Loaded [ comment "1" ]) "The list stays"
      }

      test "the approved comment of a trusted commenter goes in the list at once" {
        let model, effect =
          run (
            Comments.ListAnswered { Success = true; Message = None; Data = Some [ comment "1" ] }
            :: filled "known@example.com"
            @ [ submitted; postAnswered(postAnswer true None (Some(posted Approved))) ]
          )

        Expect.isNone effect "No request"
        Expect.equal model.PageStatus (Comments.Posted Approved) "The status says that the comment is posted"

        Expect.equal
          model.List
          (Comments.Loaded
            [ comment "1"
              { Id = "3"
                CreatedDate = "2026-10-18T10:00:00Z"
                Commenter = "Reader"
                Content = "Nice post & thanks"
                ReplyTo = None
                Writer = Commenter } ])
          "The comment is last in the list"

        Expect.equal model.Page.Text "" "The form is empty"
      }

      test "an approved comment while the list is not loaded keeps the list as it is" {
        let model, _ = run (filled "known@example.com" @ [ submitted; postAnswered(postAnswer true None (Some(posted Approved))) ])
        Expect.equal model.PageStatus (Comments.Posted Approved) "The status says that the comment is posted"
        Expect.equal model.List Comments.Loading "The list is still loading"
      }

      test "a refused validation or post shows the message of the API, or a general message" {
        for email, answered in
          [ "reader@example.com", (fun message -> validationAnswered(answer false message))
            "known@example.com", (fun message -> postAnswered(postAnswer false message None)) ] do
          let withMessage, _ = run (filled email @ [ submitted; answered (Some "The commenter is banned") ])
          Expect.equal withMessage.PageStatus (Comments.Failed "The commenter is banned") $"The message of the API for {email}"
          Expect.equal withMessage.Page.Text " Nice post & thanks " "The form keeps the comment"

          let general, _ = run (filled email @ [ submitted; answered None ])
          Expect.equal general.PageStatus (Comments.Failed "Couldn't post your comment. Try again later.") $"A general message for {email}"
      }

      test "a request with no answer shows that the site cannot get to the server, and the reader can submit again" {
        let model, _ = run (filled "known@example.com" @ [ submitted; unanswered ])
        Expect.equal model.PageStatus (Comments.Failed "Couldn't reach the server. Try again later.") "No answer"

        let _, effect = Comments.update submitted model
        Expect.isSome effect "A second submit sends again"
      }
    ]

    testList "preview" [
      test "a new form shows the write tab and hides the help" {
        Expect.equal start.Page.Tab Comments.Write "The write tab"
        Expect.isFalse start.Page.HelpShown "The help is closed"
      }

      test "the tabs change, and the return to write keeps the comment" {
        let model, effect = run [ page (Comments.TextChanged "Hi"); page Comments.PreviewSelected; page Comments.WriteSelected ]
        Expect.isNone effect "No request"
        Expect.equal model.Page.Tab Comments.Write "The write tab"
        Expect.equal model.Page.Text "Hi" "The comment stays"
      }

      test "an empty or blank text shows that there is nothing to preview, with no call" {
        for comment in [ ""; "  \n " ] do
          let model, effect = run [ page (Comments.TextChanged comment); page Comments.PreviewSelected ]
          Expect.isNone effect $"No request for '{comment}'"
          Expect.equal model.Page.Tab (Comments.Preview Comments.PreviewState.Empty) "Nothing to preview"
      }

      test "the first open loads the preview of the trimmed text with one call" {
        let model, effect = run [ page (Comments.TextChanged " Use `List.map` "); page Comments.PreviewSelected ]
        Expect.equal model.Page.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview is loading"
        Expect.equal effect (Some(Comments.PreviewComment(Comments.Form.Page, request model.Page.Opening "Use `List.map`"))) "One call with the trimmed text"
      }

      test "a second select of the open preview tab makes no call" {
        let model, effect = from previewing [ page Comments.PreviewSelected ]
        Expect.isNone effect "No request"
        Expect.equal model previewing "The model does not change"
      }

      test "the answer shows the HTML and keeps it" {
        let model, effect = from previewing [ previewAnswered(request previewing.Page.Opening "Use `List.map`", shown html) ]
        Expect.isNone effect "No request"
        Expect.equal model.Page.Tab (Comments.Preview(Comments.PreviewState.Shown html)) "The preview shows the HTML"
        Expect.equal model.Page.Kept (Some { Source = "Use `List.map`"; Html = html }) "The widget keeps the source and its HTML"
      }

      test "an open after no change to the text shows the kept HTML with no call" {
        let answered, _ = from previewing [ previewAnswered(request previewing.Page.Opening "Use `List.map`", shown html) ]

        for edit in [ []; [ page (Comments.TextChanged "Use `List.map`  ") ] ] do
          let model, effect = from answered ([ page Comments.WriteSelected ] @ edit @ [ page Comments.PreviewSelected ])
          Expect.isNone effect "No request"
          Expect.equal model.Page.Tab (Comments.Preview(Comments.PreviewState.Shown html)) "The kept HTML"
      }

      test "an open after an edit calls again" {
        let answered, _ = from previewing [ previewAnswered(request previewing.Page.Opening "Use `List.map`", shown html) ]
        let model, effect = from answered [ page Comments.WriteSelected; page (Comments.TextChanged "Use `List.iter`"); page Comments.PreviewSelected ]
        Expect.equal model.Page.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview is loading"
        Expect.equal effect (Some(Comments.PreviewComment(Comments.Form.Page, request model.Page.Opening "Use `List.iter`"))) "A call with the new text"
      }

      test "a refused or unanswered call fails, is not kept, and [try again] calls again" {
        for failure in
          [ previewAnswered(request previewing.Page.Opening "Use `List.map`", refusedPreview)
            previewAnswered(request previewing.Page.Opening "Use `List.map`", { shown html with Data = None })
            previewUnanswered(request previewing.Page.Opening "Use `List.map`") ] do
          let failed, effect = from previewing [ failure ]
          Expect.isNone effect "No request"
          Expect.equal failed.Page.Tab (Comments.Preview Comments.PreviewState.Failed) "The preview failed"
          Expect.isNone failed.Page.Kept "The widget keeps no failed call"

          let retried, effect = from failed [ page Comments.RetrySelected ]
          Expect.equal retried.Page.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview is loading again"
          Expect.equal effect (Some(Comments.PreviewComment(Comments.Form.Page, request retried.Page.Opening "Use `List.map`"))) "[try again] calls again"

          let reopened, effect = from failed [ page Comments.WriteSelected; page Comments.PreviewSelected ]
          Expect.equal effect (Some(Comments.PreviewComment(Comments.Form.Page, request reopened.Page.Opening "Use `List.map`"))) "A new open calls again"
      }

      test "[try again] does nothing when the preview did not fail" {
        for model in [ start; previewing ] do
          let next, effect = Comments.update (page Comments.RetrySelected) model
          Expect.isNone effect "No request"
          Expect.equal next model "The model does not change"
      }

      test "a late answer after a return to write does not replace the newer state" {
        let back, _ = from previewing [ page Comments.WriteSelected ]

        for late in
          [ previewAnswered(request previewing.Page.Opening "Use `List.map`", shown html)
            previewUnanswered(request previewing.Page.Opening "Use `List.map`") ] do
          let model, effect = from back [ late ]
          Expect.isNone effect "No request"
          Expect.equal model back "The write tab stays, and the widget keeps nothing"
      }

      test "a late answer for an older open does not replace the state of the newer open" {
        let reopened, _ = from previewing [ page Comments.WriteSelected; page (Comments.TextChanged "Use `List.iter`"); page Comments.PreviewSelected ]
        let model, _ = from reopened [ previewAnswered(request previewing.Page.Opening "Use `List.map`", shown html) ]
        Expect.equal model reopened "The newer open still loads"

        let answered, _ =
          from model [ previewAnswered(request reopened.Page.Opening "Use `List.iter`", shown "<p>Use <code>List.iter</code></p>\n") ]

        Expect.equal answered.Page.Tab (Comments.Preview(Comments.PreviewState.Shown "<p>Use <code>List.iter</code></p>\n")) "The newer answer shows"
      }

      test "a submit from the preview tab posts the comment, and the form goes back to write" {
        let sending, effect = from previewing [ Comments.EmailChanged "known@example.com"; submitted ]
        Expect.equal sending.PageStatus Comments.Posting "The form is sending"
        Expect.equal effect (Some(Comments.Post(Comments.Form.Page, { ValidationId = "validation-1"; Comment = "Use `List.map`"; ReplyTo = None }))) "The post has the comment"

        let posted, _ = from sending [ postAnswered(postAnswer true None None) ]
        Expect.equal posted.Page.Tab Comments.Write "The empty form shows the write tab"
        Expect.equal posted.Page.Text "" "The form is empty"
      }

      test "[markdown help] opens and closes the help with no call" {
        let opened, effect = run [ page Comments.HelpToggled ]
        Expect.isNone effect "No request"
        Expect.isTrue opened.Page.HelpShown "The help is open"

        let closed, effect = from opened [ page Comments.HelpToggled ]
        Expect.isNone effect "No request"
        Expect.isFalse closed.Page.HelpShown "The help is closed"
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

    testList "threads" [
      test "the threads are in the order of their top-level comments, and the replies of a thread are oldest first" {
        let model, effect = run [ loaded [ reply "a2" 5 "a"; topLevel "b" 2; reply "b1" 4 "b"; reply "a1" 3 "a"; topLevel "a" 1 ] ]
        Expect.isNone effect "No request"
        Expect.equal (shape model) [ "a", 0, [ "a1"; "a2" ]; "b", 0, [ "b1" ] ] "Oldest first"
      }

      test "the replies in one second are in time order, with a whole second first" {
        let atTime id time = { reply id 1 "a" with CreatedDate = $"2026-10-01T09:31:{time}Z" }
        let model, _ = run [ loaded [ topLevel "a" 1; atTime "r3" "00.997"; atTime "r2" "00.99"; atTime "r1" "00" ] ]
        Expect.equal (shape model) [ "a", 0, [ "r1"; "r2"; "r3" ] ] "Oldest first by time"
      }

      test "a reply with no top-level comment in the list shows in no thread" {
        let model, _ = run [ loaded [ topLevel "a" 1; reply "x1" 2 "gone" ] ]
        Expect.equal (shape model) [ "a", 0, [] ] "No thread for the reply"
      }

      test "a thread with more than 3 replies shows the 2 newest and the count of the earlier replies" {
        let model, _ = run [ loaded longThreads ]
        Expect.equal (shape model) [ "a", 2, [ "a3"; "a4" ]; "b", 0, [ "b1"; "b2"; "b3" ] ] "Only the thread with 4 replies collapses"
        Expect.equal (Comments.earlierText 2) "[show 2 earlier replies]" "The control names the count"
      }

      test "[show N earlier replies] shows each reply of its thread only, with no call" {
        let collapsed, _ = run [ loaded (longThreads @ [ topLevel "c" 10; reply "c1" 11 "c"; reply "c2" 12 "c"; reply "c3" 13 "c"; reply "c4" 14 "c" ]) ]
        let model, effect = from collapsed [ Comments.EarlierShown "a" ]
        Expect.isNone effect "No request"

        Expect.equal
          (shape model)
          [ "a", 0, [ "a1"; "a2"; "a3"; "a4" ]; "b", 0, [ "b1"; "b2"; "b3" ]; "c", 2, [ "c3"; "c4" ] ]
          "The other thread stays collapsed"
      }

      test "an approved comment that the reader posts keeps the open threads" {
        let model, _ =
          run (
            [ loaded longThreads; Comments.EarlierShown "a" ]
            @ filled "known@example.com"
            @ [ submitted; postAnswered(postAnswer true None (Some(posted Approved))) ]
          )

        Expect.equal (Comments.shownThreads model.Expanded model.List |> List.head |> _.HiddenEarlier) 0 "The thread stays open"
      }

      test "a reply from the author shows the author name of the post and the badge, and a reply from a reader shows the reader" {
        let model, _ = run [ loaded [ topLevel "a" 1; at "a1" 2 (Some "a") Author; reply "a2" 3 "a" ] ]
        let thread = Comments.shownThreads model.Expanded model.List |> List.exactlyOne

        Expect.equal
          (thread.Comment :: thread.Replies |> List.map (Comments.byline "Matthew Watt"))
          [ Comments.Reader "Reader a"; Comments.PostAuthor "Matthew Watt"; Comments.Reader "Reader a2" ]
          "The author name from the props"
      }
    ]

    testList "reply" [
      test "[reply] opens the reply form of its thread with no call" {
        let model, effect = from withThreads [ Comments.ReplyOpened "a" ]
        Expect.isNone effect "No request"
        Expect.equal (openForm model) (Some("a", "")) "The form of the thread is open and empty"
        Expect.equal model.Reply.Value.Status Comments.Editing "No result"
      }

      test "the form says which comment the reply answers" {
        Expect.equal (Comments.replyToText "Matthew Watt" (topLevel "a" 1)) "reply to Reader a" "The name of the commenter"
      }

      test "one reply form is open at a time, and each closed form keeps its text" {
        let model, _ =
          from withThreads [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "First draft"); Comments.ReplyOpened "b" ]

        Expect.equal (openForm model) (Some("b", "")) "Only the form of the second thread is open"

        let back, _ = from model [ replyTo "b" (Comments.TextChanged "Second draft"); Comments.ReplyOpened "a" ]
        Expect.equal (openForm back) (Some("a", "First draft")) "The text of the first form comes back"

        let again, _ = from back [ Comments.ReplyOpened "b" ]
        Expect.equal (openForm again) (Some("b", "Second draft")) "The text of the second form comes back"
      }

      test "[cancel] closes the form and keeps its text for the next open" {
        let canceled, effect = from withThreads [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Draft"); Comments.ReplyCanceled ]
        Expect.isNone effect "No request"
        Expect.isNone canceled.Reply "No form is open"

        let reopened, _ = from canceled [ Comments.ReplyOpened "a" ]
        Expect.equal (openForm reopened) (Some("a", "Draft")) "The text comes back"
      }

      test "a closed form keeps its status, so a form that waits for the verification link stays locked at the next open" {
        let sent, _ =
          from
            withThreads
            [ Comments.ReplyOpened "a"
              Comments.EmailChanged "reader@example.com"
              replyTo "a" (Comments.TextChanged "Me too")
              Comments.Submitted(Comments.Form.Reply "a")
              Comments.ValidationAnswered(Comments.Form.Reply "a", answer true None) ]

        for closing in [ Comments.ReplyCanceled; Comments.ReplyOpened "b" ] do
          let model, effect = from sent [ closing; Comments.ReplyOpened "a"; Comments.Submitted(Comments.Form.Reply "a") ]
          Expect.isNone effect $"No second verification email after %A{closing}"
          Expect.equal model.Reply.Value.Status (Comments.ValidationSent "reader@example.com") "The form still names the address"
          Expect.equal (openForm model) (Some("a", "Me too")) "The text comes back"
      }

      test "a [reply] on the open form keeps its text" {
        let opened, _ = from withThreads [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Draft") ]
        let model, _ = from opened [ Comments.ReplyOpened "a" ]
        Expect.equal model opened "The model does not change"
      }

      test "while a reply is sending, [reply] on another thread and [cancel] do nothing" {
        let sending, _ =
          from verified [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Me too"); Comments.Submitted(Comments.Form.Reply "a") ]

        for msg in [ Comments.ReplyOpened "b"; Comments.ReplyCanceled ] do
          let model, effect = from sending [ msg ]
          Expect.isNone effect "No request"
          Expect.equal model sending $"The model does not change for %A{msg}"
      }

      test "a message for a closed reply form changes nothing" {
        let opened, _ = from withThreads [ Comments.ReplyOpened "a" ]

        for msg in
          [ replyTo "b" (Comments.TextChanged "Lost")
            replyTo "b" Comments.PreviewSelected
            Comments.Submitted(Comments.Form.Reply "b")
            Comments.PostAnswered(Comments.Form.Reply "b", postAnswer false (Some "Refused") None)
            Comments.Unanswered(Comments.Form.Reply "b") ] do
          let model, effect = from opened [ msg ]
          Expect.isNone effect $"No request for %A{msg}"
          Expect.equal model opened $"The model does not change for %A{msg}"
      }

      test "the reply form has its own tabs and preview" {
        let model, effect = from withThreads [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged " Use `List.map` "); replyTo "a" Comments.PreviewSelected ]
        let editor = model.Reply.Value.Editor
        Expect.equal editor.Tab (Comments.Preview Comments.PreviewState.Loading) "The preview of the reply is loading"
        Expect.equal effect (Some(Comments.PreviewComment(Comments.Form.Reply "a", request editor.Opening "Use `List.map`"))) "One call for the reply"
        Expect.equal model.Page.Tab Comments.Write "The page form stays on write"

        let answered, _ = from model [ replyTo "a" (Comments.PreviewAnswered(request editor.Opening "Use `List.map`", shown html)) ]
        Expect.equal answered.Reply.Value.Editor.Tab (Comments.Preview(Comments.PreviewState.Shown html)) "The reply shows its preview"
      }

      test "a preview answer after the form closes does not show in the next open" {
        let loading, _ = from withThreads [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Hi"); replyTo "a" Comments.PreviewSelected ]
        let late = request loading.Reply.Value.Editor.Opening "Hi"
        let model, _ = from loading [ Comments.ReplyCanceled; Comments.ReplyOpened "a"; replyTo "a" (Comments.PreviewAnswered(late, shown html)) ]
        Expect.equal model.Reply.Value.Editor.Tab Comments.Write "The reopened form shows the write tab"
        Expect.isNone model.Reply.Value.Editor.Kept "The form keeps no late answer"
      }
    ]

    testList "reply identity" [
      test "a new reader gets the name and email fields" {
        let model, _ = from withThreads [ Comments.ReplyOpened "a" ]
        Expect.equal model.Reply.Value.Identity Comments.Fields "The fields"
      }

      test "a reader with a verified email address gets one line, and [change] opens the fields" {
        let model, _ = from verified [ Comments.ReplyOpened "a" ]
        Expect.equal model.Reply.Value.Identity Comments.Verified "One line"
        Expect.equal (Comments.shownName model, model.Email) ("Reader", "known@example.com") "The line has the name and the email address"

        let changed, effect = from model [ Comments.IdentityChanged ]
        Expect.isNone effect "No request"
        Expect.equal changed.Reply.Value.Identity Comments.Fields "The fields"
        Expect.isFalse (Comments.showsIdentityLine changed) "No line"
      }

      test "the one line shows only while the shared email address has a validation ID" {
        let model, _ = from verified [ Comments.ReplyOpened "a" ]
        Expect.isTrue (Comments.showsIdentityLine model) "One line"

        let changed, _ = from model [ Comments.EmailChanged "other@example.com" ]
        Expect.isFalse (Comments.showsIdentityLine changed) "The fields for an address with no validation ID"

        let back, _ = from changed [ Comments.EmailChanged "known@example.com" ]
        Expect.isTrue (Comments.showsIdentityLine back) "One line again"
      }

      test "a verified reader with no name shows as Anonymous" {
        let model, _ = from withThreads [ Comments.EmailChanged "known@example.com"; Comments.ReplyOpened "a" ]
        Expect.equal model.Reply.Value.Identity Comments.Verified "One line"
        Expect.equal (Comments.shownName model) "Anonymous" "The name of a commenter with no name"
      }

      test "a post from the page form keeps the name and the email address for the reply form" {
        let model, _ =
          from withThreads (filled "known@example.com" @ [ submitted; postAnswered (postAnswer true None None); Comments.ReplyOpened "a" ])

        Expect.equal model.Reply.Value.Identity Comments.Verified "One line"
        Expect.equal (model.Name, model.Email) (" Reader ", " known@example.com ") "The name and the email address of the page form"
      }

      test "the reply form and the page form share the name and the email address" {
        let model, effect =
          from
            withThreads
            [ Comments.ReplyOpened "a"
              Comments.NameChanged "Reader"
              Comments.EmailChanged "known@example.com"
              replyTo "a" (Comments.TextChanged " Me too ")
              Comments.Submitted(Comments.Form.Reply "a") ]

        Expect.equal (model.Name, model.Email) ("Reader", "known@example.com") "The page form shows the values of the reply form"

        Expect.equal
          effect
          (Some(Comments.Post(Comments.Form.Reply "a", { ValidationId = "validation-1"; Comment = "Me too"; ReplyTo = Some "a" })))
          "The reply posts with the shared email address"
      }
    ]

    testList "reply post" [
      test "a reply posts only the validation ID, the text, and its top-level comment, with no reply-email choice" {
        let model, effect = from verified [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged " Me too "); Comments.Submitted(Comments.Form.Reply "a") ]
        Expect.equal model.Reply.Value.Status Comments.Posting "The reply form is sending"
        Expect.equal model.PageStatus Comments.Editing "The page form is not sending"

        Expect.equal
          effect
          (Some(Comments.Post(Comments.Form.Reply "a", { ValidationId = "validation-1"; Comment = "Me too"; ReplyTo = Some "a" })))
          "The post of the reply"
      }

      test "a reply from a new email address sends the validation with the text and the thread in the link back" {
        let model, effect =
          from
            withThreads
            [ Comments.ReplyOpened "a"
              Comments.NameChanged "Reader"
              Comments.EmailChanged "reader@example.com"
              replyTo "a" (Comments.TextChanged "Me & you")
              Comments.Submitted(Comments.Form.Reply "a")
              Comments.ValidationAnswered(Comments.Form.Reply "a", answer true None) ]

        Expect.isNone effect "No request after the answer"
        Expect.equal model.Reply.Value.Status (Comments.ValidationSent "reader@example.com") "The status names the address"

        let _, validation =
          from withThreads [ Comments.ReplyOpened "a"; Comments.NameChanged "Reader"; Comments.EmailChanged "reader@example.com"; replyTo "a" (Comments.TextChanged "Me & you"); Comments.Submitted(Comments.Form.Reply "a") ]

        Expect.equal
          validation
          (Some(
            Comments.Validate(
              Comments.Form.Reply "a",
              { EmailAddress = "reader@example.com"
                Name = Some "Reader"
                RedirectUri = "https://twopoint.dev/posts/a-post/?email=reader%40example.com&name=Reader&comment=Me%20%26%20you&replyTo=a#comments" }
            )
          ))
          "The redirect URI has the thread"
      }

      test "a reply with no text or a bad email address sends nothing and shows each error" {
        let model, effect = from withThreads [ Comments.ReplyOpened "a"; Comments.EmailChanged "reader@"; Comments.Submitted(Comments.Form.Reply "a") ]
        Expect.isNone effect "No request"
        Expect.isTrue (Comments.showsEmailError model) "The email error shows"
        Expect.isTrue (Comments.showsTextError model.Reply.Value.Editor) "The text error shows"
        Expect.isFalse (Comments.showsTextError model.Page) "The page form shows no error"
      }

      test "an approved reply goes at the end of its thread, and the form closes and forgets its text" {
        let model, effect =
          from verified [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Me too"); Comments.Submitted(Comments.Form.Reply "a"); Comments.PostAnswered(Comments.Form.Reply "a", postAnswer true None (Some(repliedTo Approved))) ]

        Expect.isNone effect "No request"
        Expect.isNone model.Reply "The form is closed"
        Expect.equal (shape model) [ "a", 0, [ "a1"; "a2" ]; "b", 0, [] ] "The reply is last in its thread"
        Expect.isEmpty model.Awaiting "No thread shows the approval note"

        let reopened, _ = from model [ Comments.ReplyOpened "a" ]
        Expect.equal (openForm reopened) (Some("a", "")) "The next open is empty"
      }

      test "a new reply shows nothing, and its thread shows that it waits for approval" {
        for answered in [ Some(repliedTo AwaitsApproval); None ] do
          let model, _ =
            from verified [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Me too"); Comments.Submitted(Comments.Form.Reply "a"); Comments.PostAnswered(Comments.Form.Reply "a", postAnswer true None answered) ]

          Expect.isNone model.Reply "The form is closed"
          Expect.equal (shape model) [ "a", 0, [ "a1" ]; "b", 0, [] ] "The list stays"
          Expect.equal model.Awaiting (set [ "a" ]) "The thread shows the note"
          Expect.equal Comments.awaitingText "Thanks! Your reply will appear here once it's approved." "The note"

          let reopened, _ = from model [ Comments.ReplyOpened "a" ]
          Expect.isEmpty reopened.Awaiting "A new open hides the note"
      }

      test "a refused reply keeps the form and its text, and shows why it didn't post" {
        let refusal = "This comment isn't on the post anymore, so you can't reply to it."
        let sending, _ = from verified [ Comments.ReplyOpened "a"; replyTo "a" (Comments.TextChanged "Me too"); Comments.Submitted(Comments.Form.Reply "a") ]

        for answered, status in
          [ Comments.PostAnswered(Comments.Form.Reply "a", postAnswer false (Some refusal) None), refusal
            Comments.PostAnswered(Comments.Form.Reply "a", postAnswer false None None), "Couldn't post your reply. Try again later."
            Comments.Unanswered(Comments.Form.Reply "a"), "Couldn't reach the server. Try again later." ] do
          let model, _ = from sending [ answered ]
          Expect.equal (openForm model) (Some("a", "Me too")) "The form stays with its text"
          Expect.equal model.Reply.Value.Status (Comments.Failed status) "The reason"
          Expect.equal (Comments.replyStatusText model.Reply.Value.Status) status "The status line"
          Expect.equal model.PageStatus Comments.Editing "The page form has no result"
      }
    ]

    testList "reply return" [
      test "the query of the link in the validation email gives the thread of a reply" {
        Expect.equal
          (Comments.parse [ "email", "reader@example.com"; "comment", "Me too"; "commenterValidationId", "validation-2"; "replyTo", " a " ])
          (Some { returnedReply with Name = "" })
          "The thread"

        Expect.equal
          (Comments.parse [ "email", "reader@example.com"; "comment", "Nice post"; "commenterValidationId", "validation-2"; "replyTo", " " ])
          (Some { returned with Name = "" })
          "No thread"
      }

      test "the return of a reply opens its form with the text, and the page form stays empty" {
        let model, effect = Comments.init pageUrl known (Some returnedReply) None
        Expect.equal effect (Some Comments.Load) "The widget loads the comments"
        Expect.equal (openForm model) (Some("a", "Me too")) "The reply form of the thread has the text"
        Expect.equal model.Reply.Value.Identity Comments.Verified "One line for the verified address"
        Expect.equal model.Reply.Value.Status Comments.Validated "The reply form tells the reader to post"
        Expect.equal (Comments.replyStatusText model.Reply.Value.Status) "Email verified. Click [post reply] to post." "The message"
        Expect.equal (model.Page.Text, model.PageStatus) ("", Comments.Editing) "The page form is empty, with no result"
        Expect.equal model.Validations (known.Add("reader@example.com", "validation-2")) "The validation ID is beside the others"
      }

      test "after the comments load, the widget scrolls to the reply form, and the reader can post" {
        let start = Comments.init pageUrl known (Some returnedReply) None |> fst
        let model, effect = from start [ loaded [ topLevel "a" 1; topLevel "b" 2 ] ]
        Expect.equal effect (Some(Comments.ScrollTo(Comments.ToReply "a"))) "The scroll to the reply form"
        Expect.equal (openForm model) (Some("a", "Me too")) "The form stays open"

        let _, posted = from model [ Comments.Submitted(Comments.Form.Reply "a") ]

        Expect.equal
          posted
          (Some(Comments.Post(Comments.Form.Reply "a", { ValidationId = "validation-2"; Comment = "Me too"; ReplyTo = Some "a" })))
          "The post of the reply"
      }

      test "when the thread is not in the list, the widget scrolls to the comments section and keeps the text" {
        let start = Comments.init pageUrl known (Some returnedReply) None |> fst
        let model, effect = from start [ loaded [ topLevel "b" 2 ] ]
        Expect.equal effect (Some(Comments.ScrollTo Comments.ToSection)) "The scroll to the section"
        Expect.isNone model.Reply "No form is open"
        Expect.equal (model.Drafts.TryFind "a" |> Option.map _.Editor.Text) (Some "Me too") "The text is kept"

        let failed, scroll = from start [ Comments.ListUnanswered ]
        Expect.equal scroll (Some(Comments.ScrollTo Comments.ToSection)) "The scroll to the section when the list does not load"
        Expect.isNone failed.Reply "No form is open when the list does not load"
        Expect.equal (failed.Drafts.TryFind "a" |> Option.map _.Editor.Text) (Some "Me too") "The text is kept when the list does not load"
      }
    ]

    testList "link" [
      test "a #comment-<id> fragment gives the link, with the thread query if the URL has it" {
        Expect.equal
          (Comments.parseLink "#comment-a1" [ "thread", " a " ])
          (Some { Comment = "a1"; Thread = Some "a" })
          "The comment and the thread"

        Expect.equal (Comments.parseLink "#comment-a1" [ "thread", " " ]) (Some { Comment = "a1"; Thread = None }) "An empty thread"

        for hash in [ ""; "#comments"; "#comment-"; "#top" ] do
          Expect.isNone (Comments.parseLink hash [ "thread", "a" ]) $"No link for '{hash}'"
      }

      test "a URL with no link scrolls to nothing after the comments load" {
        let model, effect = run [ loaded longThreads ]
        Expect.isNone effect "No scroll"
        Expect.isNone model.Arrival "No link"
      }

      test "the comment of the link in the list gets the scroll and the mark" {
        let model, effect = from (linked { Comment = "a4"; Thread = Some "a" }) [ loaded longThreads ]
        Expect.equal effect (Some(Comments.ScrollTo(Comments.ToComment "a4"))) "The scroll to the comment"
        Expect.equal (shape model |> List.head) ("a", 2, [ "a3"; "a4" ]) "The thread stays collapsed"
        Expect.isNone model.Arrival "The widget reads the link one time"
      }

      test "a comment of the link in the hidden earlier replies opens them" {
        let model, effect = from (linked { Comment = "a1"; Thread = None }) [ loaded longThreads ]
        Expect.equal effect (Some(Comments.ScrollTo(Comments.ToComment "a1"))) "The scroll to the comment"
        Expect.equal (shape model |> List.head) ("a", 0, [ "a1"; "a2"; "a3"; "a4" ]) "The earlier replies show"
      }

      test "a comment of the link that is not in the list scrolls to the top-level comment of the thread query" {
        let model, effect = from (linked { Comment = "gone"; Thread = Some "b" }) [ loaded longThreads ]
        Expect.equal effect (Some(Comments.ScrollTo(Comments.ToThread "b"))) "The scroll to the thread"
        Expect.equal (shape model |> List.head) ("a", 2, [ "a3"; "a4" ]) "No thread opens"
      }

      test "with no comment and no thread of the link in the list, the widget scrolls to the comments section" {
        let links: Comments.Link list = [ { Comment = "gone"; Thread = Some "also-gone" }; { Comment = "gone"; Thread = None } ]

        for link in links do
          let _, effect = from (linked link) [ loaded longThreads ]
          Expect.equal effect (Some(Comments.ScrollTo Comments.ToSection)) $"The scroll to the section for %A{link}"

        for failed in [ Comments.ListAnswered { Success = false; Message = None; Data = None }; Comments.ListUnanswered ] do
          let _, effect = from (linked { Comment = "a1"; Thread = Some "a" }) [ failed ]
          Expect.equal effect (Some(Comments.ScrollTo Comments.ToSection)) "The scroll to the section when the list does not load"
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
