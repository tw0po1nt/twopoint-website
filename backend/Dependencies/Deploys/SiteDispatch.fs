namespace TwoPoint.Dependencies.Deploys

open TwoPoint.Core.Deploys
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure.Security.KeyVault.Keys.Cryptography
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System
open System.Net.Http
open System.Net.Http.Headers
open System.Net.Http.Json
open System.Text
open System.Text.Json
open System.Text.Json.Serialization

/// The private key of the GitHub App is a Key Vault key, and Key Vault signs each app JWT.
type GitHubApp =
  { ClientId : string
    SigningKey : Uri }

module GitHubAppJwt =

  let private base64Url (bytes : byte array) =
    Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_')

  let private encode (json : string) = json |> Encoding.UTF8.GetBytes |> base64Url

  /// GitHub refuses an app JWT that expires more than 10 minutes after its issue time.
  /// The issue time is 60 seconds early, because the clock of GitHub can be behind.
  let unsigned (clientId : string) (now : DateTimeOffset) =
    let header = JsonSerializer.Serialize {| alg = "RS256"; typ = "JWT" |}
    let payload =
      JsonSerializer.Serialize
        {| iat = now.AddSeconds(-60.).ToUnixTimeSeconds()
           exp = now.AddMinutes(9.).ToUnixTimeSeconds()
           iss = clientId |}
    $"{encode header}.{encode payload}"

  let complete (unsigned : string) (signature : byte array) = $"{unsigned}.{base64Url signature}"

type private Installation = { Id : int64 }

type private InstallationToken = { Token : string }

type private DispatchRun =
  { [<JsonPropertyName "run_url">]
    RunUrl : Uri }

type private Run =
  { [<JsonPropertyName "run_number">]
    RunNumber : int64 }

/// Sends `workflow_dispatch` to `site.yml` as the GitHub App, with an installation token for each dispatch.
type GitHubDispatch(httpClient : HttpClient, signer : CryptographyClient, app : GitHubApp, logger : ILogger) =

  let api = Uri "https://api.github.com/"

  let send (request : HttpRequestMessage) (bearer : string) = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    request.Headers.Authorization <- AuthenticationHeaderValue("Bearer", bearer)
    request.Headers.Accept.Add(MediaTypeWithQualityHeaderValue "application/vnd.github+json")
    request.Headers.UserAgent.Add(ProductInfoHeaderValue("twopoint-admin", "1.0"))
    request.Headers.Add("X-GitHub-Api-Version", "2022-11-28")
    let! response = httpClient.SendAsync(request, ct)

    if not response.IsSuccessStatusCode then
      let! body = response.Content.ReadAsStringAsync ct
      failwith $"GitHub answered {int response.StatusCode} to {request.Method} {request.RequestUri}: {body}"

    return response
  }

  let read (request : HttpRequestMessage) (bearer : string) : CancellableTask<'T> = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    use! response = send request bearer
    return! response.Content.ReadFromJsonAsync<'T>(cancellationToken = ct)
  }

  let appJwt () = cancellableTask {
    let! ct = CancellableTask.getCancellationToken()
    let unsigned = GitHubAppJwt.unsigned app.ClientId DateTimeOffset.UtcNow
    let! signed = signer.SignDataAsync(SignatureAlgorithm.RS256, Encoding.ASCII.GetBytes unsigned, ct)
    return GitHubAppJwt.complete unsigned signed.Signature
  }

  let installationToken (repo : Repo) = cancellableTask {
    let! jwt = appJwt ()
    let! (installation : Installation) = read (new HttpRequestMessage(HttpMethod.Get, Uri(api, $"repos/{repo}/installation"))) jwt
    let request = new HttpRequestMessage(HttpMethod.Post, Uri(api, $"app/installations/{installation.Id}/access_tokens"))
    request.Content <- JsonContent.Create {| repositories = [ repo.Name ]; permissions = {| actions = "write" |} |}
    let! (token : InstallationToken) = read request jwt
    return token.Token
  }

  member _.Dispatch (repo : Repo) : DependencyResult<Dispatch> =
    runDependencyWithLogging Dependency.GitHub logger $"{nameof GitHubDispatch}.Dispatch"
    <| cancellableTaskResult {
      let! token = installationToken repo
      let request = new HttpRequestMessage(HttpMethod.Post, Uri(api, $"repos/{repo}/actions/workflows/site.yml/dispatches"))
      request.Content <- JsonContent.Create {| ref = "main"; return_run_details = true |}
      let! (dispatched : DispatchRun) = read request token
      let! (run : Run) = read (new HttpRequestMessage(HttpMethod.Get, dispatched.RunUrl)) token
      logger.LogInformation("GitHub queued run {runNumber} of site.yml in {repo}", run.RunNumber, repo)
      return Queued (RunNumber run.RunNumber)
    }

/// The local backend logs each dispatch and sends nothing.
type LoggedDispatch(logger : ILogger) =

  member _.Dispatch (repo : Repo) : DependencyResult<Dispatch> =
    logger.LogInformation("The local backend sends no dispatch of site.yml to {repo}", repo)
    CancellableTaskResult.singleton Logged
