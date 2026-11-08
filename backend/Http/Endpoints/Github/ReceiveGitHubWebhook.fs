namespace TwoPoint.Http.Endpoints.Github

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Shared
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
open TwoPoint.Dependencies.Notifications
open TwoPoint.Dependencies.Util
open TwoPoint.Http

open Azure.Core
open Azure.Data.Tables
open Azure.Security.KeyVault.Secrets
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.IO
open System.Net
open System.Text
open System.Threading

type WebhookConfigProblem =
  | DeploySectionMissing
  | WebhookSecretNameMissing
  | KeyVaultMissing

module WebhookConfigProblem =

  let text (problem : WebhookConfigProblem) =
    match problem with
    | DeploySectionMissing -> "The 'Deploy' configuration section is missing"
    | WebhookSecretNameMissing -> "The setting 'Deploy:GitHubApp:WebhookSecretName' is missing"
    | KeyVaultMissing -> "The setting 'Azure:KeyVaultUri' is missing"

module MalformedPayload =

  let text (malformed : ReceiveWorkflowRun.Malformed) =
    match malformed with
    | ReceiveWorkflowRun.NoWorkflowRun -> "The payload has no workflow_run or no repository"
    | ReceiveWorkflowRun.MissingField name -> $"The workflow_run has no {name}"
    | ReceiveWorkflowRun.NoRepoName -> "The repository has no full name"
    | ReceiveWorkflowRun.BadJson error -> error.Message

type WebhookSettings =
  { Repos : SiteRepos
    SecretName : string
    Vault : Uri }

type ReceiveGitHubWebhook(
  config : Config,
  credential : TokenCredential,
  logger : ILogger<ReceiveGitHubWebhook>,
  pushSetup : WebPushSetup,
  tableServiceClient : TableServiceClient
) =

  let settings () : Result<WebhookSettings, WebhookConfigProblem> =
    match config.Deploy with
    | None -> Error DeploySectionMissing
    | Some deploy ->
      match deploy.GitHubApp |> Option.bind _.WebhookSecretName, config.Azure.KeyVaultUri with
      | None, _ -> Error WebhookSecretNameMissing
      | Some _, None -> Error KeyVaultMissing
      | Some name, Some vault ->
        Ok
          { Repos = { Production = deploy.ProductionRepo; Staging = deploy.StagingRepo }
            SecretName = name
            Vault = vault }

  let secret (settings : WebhookSettings) : DependencyResult<byte array> =
    runDependencyWithLogging Dependency.KeyVault logger "ReceiveGitHubWebhook.GetWebhookSecret"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      let! secret = SecretClient(settings.Vault, credential).GetSecretAsync(settings.SecretName, cancellationToken = ct)
      return Encoding.UTF8.GetBytes secret.Value.Value
    }

  [<Function("Github-Webhook-Receive")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "webhooks/github")>] req : HttpRequestData,
    ct : CancellationToken
  ) =
    let header (name : string) =
      match req.Headers.TryGetValues name with
      | true, values -> Seq.tryHead values
      | false, _ -> None

    let handle = cancellableTask {
      match settings () with
      | Error problem ->
        logger.LogError("The webhook of GitHub is not configured. {problem}", WebhookConfigProblem.text problem)
        return req.CreateResponse HttpStatusCode.InternalServerError
      | Ok settings ->
        let! ct = CancellableTask.getCancellationToken()
        use buffer = new MemoryStream()
        do! req.Body.CopyToAsync(buffer, ct)

        let db = DeployDb(tableServiceClient, logger)
        let devices = DeviceDependencies(tableServiceClient, SystemDependencies.live, logger)
        let push = PushDependencies(pushSetup, tableServiceClient, SystemDependencies.live, logger)

        let receive = ReceiveWorkflowRun.withDependencies { new ReceiveWorkflowRun.Dependencies with
          member _.GetSiteRepos () = settings.Repos
          member _.GetWebhookSecret () = secret settings
          member _.SaveRun run = db.SaveRun run
          member _.GetDevicesOfAuthor () = devices.GetDevicesOfUser Access.authorUserId
          member _.SendPush device payload = push.SendPush device payload
          member _.RecordPush device run = push.RecordRunPush device run
          member _.DeleteDevice device = devices.DeleteDevice device
        }

        let! result =
          receive
            { Event = header "X-GitHub-Event" |> Option.defaultValue ""
              Signature = header "X-Hub-Signature-256"
              Body = buffer.ToArray() }

        let statusCode =
          match result with
          | Ok (ReceiveWorkflowRun.Recorded(run, notification)) ->
            logger.LogInformation("Recorded run {number} of site.yml in {repo}: {state}", run.Number.Value, run.Repo, run.State)

            match notification with
            | ReceiveWorkflowRun.NoNotification -> ()
            | ReceiveWorkflowRun.Notified pushed ->
              logger.LogInformation(
                "Notified the author of the failed run {number}: {sent} sent, {removed} removed",
                run.Number.Value,
                pushed.Sent.Length,
                pushed.Removed.Length
              )
              for device, error in pushed.Failed do
                logger.LogError("Unable to push to device '{deviceId}': {error}", device.Id, error.DebugMessage)
            | ReceiveWorkflowRun.DevicesNotRead error ->
              logger.LogError("Unable to notify the author of the failed run {number}: {error}", run.Number.Value, error.DebugMessage)

            HttpStatusCode.OK
          | Ok (ReceiveWorkflowRun.Skipped skipped) ->
            logger.LogInformation("The webhook of GitHub was not recorded: {skipped}", skipped)
            HttpStatusCode.OK
          | Ok ReceiveWorkflowRun.Event.OlderThanStored ->
            logger.LogInformation "The webhook of GitHub is older than the stored run"
            HttpStatusCode.OK
          | Error (ActionError.Logic ReceiveWorkflowRun.BadSignature) ->
            logger.LogWarning "The webhook of GitHub has a bad or missing signature"
            HttpStatusCode.Unauthorized
          | Error (ActionError.Logic (ReceiveWorkflowRun.MalformedPayload malformed)) ->
            logger.LogWarning("The webhook of GitHub has a malformed payload: {reason}", MalformedPayload.text malformed)
            HttpStatusCode.BadRequest
          | Error (ActionError.Validation _)
          | Error (ActionError.Dependency _) ->
            HttpStatusCode.InternalServerError

        return req.CreateResponse statusCode
    }

    handle ct
