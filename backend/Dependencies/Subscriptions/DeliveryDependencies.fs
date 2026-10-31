namespace TwoPoint.Dependencies.Subscriptions

open TwoPoint.Dependencies.Comms
open TwoPoint.Dependencies.Posts
open TwoPoint.Dependencies.Util
open TwoPoint.Core.Posts
open TwoPoint.Core.Shared
open TwoPoint.Core.Subscriptions
open TwoPoint.Core.Util

open Azure
open Azure.Data.Tables
open Azure.Storage.Blobs
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System
open System.Net
open System.Web

type DeliveryDependencies(
  emailClient : Azure.Communication.Email.EmailClient,
  emailSender : EmailAddress,
  newsletters : BlobContainerClient,
  unsubscribeUri : Uri,
  tableServiceClient : TableServiceClient,
  logger : ILogger
) =

  let postDb = PostDb.live tableServiceClient logger
  let commsService = CommsService.live emailClient tableServiceClient logger emailSender

  let unsubscribeUrlFor (subscription : Subscription) =
    let builder = UriBuilder unsubscribeUri
    let query = HttpUtility.ParseQueryString builder.Query
    query["subscriptionId"] <- subscription.Id.ToString()
    builder.Query <- query.ToString()
    builder.Uri

  member _.GetAllPosts () = cancellableTaskResult {
    let! (dbPosts: DbPost list) = postDb.GetAllPosts()

    return
      dbPosts
      |> List.choose (fun dbo ->
        match dbo.ToPost PostCommentStats.Zero with
        | Ok post -> Some post
        | Error errors ->
          logger.LogWarning("The row of post '{slug}' is not a post: {errors}", dbo.Slug, errors)
          None)
  }

  member _.GetPostBySlug (slug : Slug) = cancellableTaskResult {
    let! (dbPost: DbPost option) = postDb.GetPostBySlug (Slug.value slug)

    return!
      dbPost
      |> Option.traverseResult (fun dbo -> dbo.ToPost PostCommentStats.Zero)
      |> DependencyError.ofValidation Dependency.Database
  }

  /// The `upload` command of the frontend generator stores the newsletter of each post at this path.
  member _.GetNewsletter (post : Post) =
    let blobName = $"posts/{Slug.value post.Id}/index"
    runDependencyWithLogging Dependency.BlobStorage logger $"{nameof DeliveryDependencies}.GetNewsletter"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      try
        let! content = newsletters.GetBlobClient(blobName).DownloadContentAsync(ct)
        return Some (content.Value.Content.ToString())
      with
        | :? RequestFailedException as ex when ex.Status = int HttpStatusCode.NotFound ->
          logger.LogWarning("Newsletter blob '{blobName}' does not exist", blobName)
          return None
    }

  member _.SendWelcome (subscription : Subscription) (newsletter : Newsletter) = cancellableTaskResult {
    let intro = subscription.Name |> Option.map NonEmptyString.value |> SubscriptionEmails.welcomeIntro
    let content = newsletter |> Newsletter.fill intro (unsubscribeUrlFor subscription)

    let! (message: EmailMessage) =
      EmailMessage.create
        SubscriptionEmails.welcomeSubject
        content
        (subscription.EmailAddress |> EmailAddress.value)
      |> DependencyError.ofValidation Dependency.Communications

    return! commsService.SendEmail message
  }

  member _.SendNewPost (subscription : Subscription) (post : Post) (intro : NonEmptyString option) (newsletter : Newsletter) =
    cancellableTaskResult {
      let content = newsletter |> Newsletter.fill (SubscriptionEmails.newPostIntro intro) (unsubscribeUrlFor subscription)

      let! (message: EmailMessage) =
        EmailMessage.create
          (SubscriptionEmails.newPostSubject post)
          content
          (subscription.EmailAddress |> EmailAddress.value)
        |> DependencyError.ofValidation Dependency.Communications

      return! commsService.SendEmail message
    }
