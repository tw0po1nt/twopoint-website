module TwoPoint.Http.Tests.RemovalMappingTests

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Http.Endpoints.Admin

open Expecto
open System.Net

let failed: DependencyError =
  { Dependency = Dependency.BlobStorage
    Type = Unknown(exn "The write failed") }

let other = Slug.Unsafe.create "other"
let third = Slug.Unsafe.create "third"
let fourth = Slug.Unsafe.create "fourth"

let editReasons =
  [ PublishSteps.WriteFailed failed, HttpStatusCode.InternalServerError
    PublishSteps.RevisionTaken, HttpStatusCode.Conflict
    PublishSteps.PublishedConditionFailed, HttpStatusCode.Conflict ]

let expectKept (action: string) (message: string) =
  Expect.stringContains message "'other', 'third'" "The message names the published edits"
  Expect.stringContains message $"couldn't {action} 'soon'" "The message names the post"

[<Tests>]
let tests =
  testList "RemovalMapping" [
    test "a kept unpublish gives 409 for a blob that changed and 500 for a failed write, and names the published edits" {
      let reasons =
        [ yield! editReasons |> List.map (fun (failure, status) -> Unpublish.EditNotPublished(fourth, failure), status)
          Unpublish.WorkingBlobKept, HttpStatusCode.Conflict
          Unpublish.WorkingWriteFailed failed, HttpStatusCode.InternalServerError
          Unpublish.PublishedBlobKept, HttpStatusCode.Conflict
          Unpublish.PublishedDeleteFailed failed, HttpStatusCode.InternalServerError ]

      for reason, status in reasons do
        let message, statusCode = UnpublishMapping.kept "soon" [ other; third ] reason []
        Expect.equal statusCode status $"The status of {reason}"
        expectKept "unpublish" message
    }

    test "a kept unpublish after the draft write tells the author to reload the post" {
      let reload = "Reload the post before you continue."

      for reason in [ Unpublish.PublishedBlobKept; Unpublish.PublishedDeleteFailed failed ] do
        let message, _ = UnpublishMapping.kept "soon" [ other ] reason []
        Expect.stringContains message reload $"The message of {reason}"

      for reason in [ Unpublish.WorkingBlobKept; Unpublish.WorkingWriteFailed failed ] do
        let message, _ = UnpublishMapping.kept "soon" [ other ] reason []
        Expect.isFalse (message.Contains reload) $"No draft text was written for {reason}"
    }

    test "a kept delete gives 409 for a blob that changed and 500 for a failed write, and names the published edits" {
      let reasons =
        [ yield! editReasons |> List.map (fun (failure, status) -> DeletePost.EditNotPublished(fourth, failure), status)
          DeletePost.PublishedBlobKept, HttpStatusCode.Conflict
          DeletePost.PublishedDeleteFailed failed, HttpStatusCode.InternalServerError ]

      for reason, status in reasons do
        let message, statusCode = DeleteMapping.kept "soon" [ other; third ] reason []
        Expect.equal statusCode status $"The status of {reason}"
        expectKept "delete" message
    }

    test "a kept removal adds the text of each warning to the message" {
      let message, _ =
        UnpublishMapping.kept "soon" [ other ] Unpublish.PublishedBlobKept [ PublishSteps.NewerTextKept ]

      Expect.stringContains message "Edits saved in another tab during the unpublish are kept as a pending edit." "The warning text"
    }
  ]
