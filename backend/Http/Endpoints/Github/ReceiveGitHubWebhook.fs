namespace TwoPoint.Http.Endpoints.Github

open TwoPoint.Core.Deploys
open TwoPoint.Core.Deploys.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Deploys
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

type ReceiveGitHubWebhook(
  config : Config,
  credential : TokenCredential,
  logger : ILogger<ReceiveGitHubWebhook>,
  tableServiceClient : TableServiceClient
) =

  let secretSource () =
    match config.Deploy |> Option.bind _.GitHubApp |> Option.bind _.WebhookSecretName, config.Azure.KeyVaultUri with
    | Some name, Some vault -> Ok (name, vault)
    | None, _ when config.Deploy.IsNone -> Error DeploySectionMissing
    | None, _ -> Error WebhookSecretNameMissing
    | Some _, None -> Error KeyVaultMissing

  let secret () : DependencyResult<byte array> =
    match secretSource () with
    | Error problem ->
      let error = { Dependency = Dependency.KeyVault; Type = Unknown (exn (WebhookConfigProblem.text problem)) }
      CancellableTaskResult.error error
    | Ok (name, vault) ->
      runDependencyWithLogging Dependency.KeyVault logger "ReceiveGitHubWebhook.GetWebhookSecret"
      <| cancellableTaskResult {
        let! ct = CancellableTask.getCancellationToken()
        let! secret = SecretClient(vault, credential).GetSecretAsync(name, cancellationToken = ct)
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

    cancellableTask {
      let! ct = CancellableTask.getCancellationToken()
      use buffer = new MemoryStream()
      do! req.Body.CopyToAsync(buffer, ct)

      let db = DeployDb(tableServiceClient, logger)

      let receive = ReceiveWorkflowRun.withDependencies { new ReceiveWorkflowRun.Dependencies with
        member _.GetSiteRepos () =
          match config.Deploy with
          | Some deploy -> { Production = deploy.ProductionRepo; Staging = deploy.StagingRepo }
          | None -> failwith (WebhookConfigProblem.text DeploySectionMissing)
        member _.GetWebhookSecret () = secret ()
        member _.GetRun repo number = db.GetRun repo number
        member _.SaveRun run = db.SaveRun run
      }

      let! result =
        receive
          { Event = header "X-GitHub-Event" |> Option.defaultValue ""
            Signature = header "X-Hub-Signature-256"
            Body = buffer.ToArray() }

      let statusCode =
        match result with
        | Ok (ReceiveWorkflowRun.Recorded run) ->
          logger.LogInformation("Recorded run {number} of site.yml in {repo}: {state}", run.Number.Value, run.Repo, run.State)
          HttpStatusCode.OK
        | Ok (ReceiveWorkflowRun.Skipped skipped) ->
          logger.LogInformation("The webhook of GitHub was not recorded: {skipped}", skipped)
          HttpStatusCode.OK
        | Error (ActionError.Logic ReceiveWorkflowRun.BadSignature) ->
          logger.LogWarning "The webhook of GitHub has a bad or missing signature"
          HttpStatusCode.Unauthorized
        | Error (ActionError.Logic (ReceiveWorkflowRun.MalformedPayload reason)) ->
          logger.LogWarning("The webhook of GitHub has a malformed payload: {reason}", reason)
          HttpStatusCode.BadRequest
        | Error (ActionError.Validation _)
        | Error (ActionError.Dependency _) ->
          HttpStatusCode.InternalServerError

      return req.CreateResponse statusCode
    }
    |> fun task -> task ct
