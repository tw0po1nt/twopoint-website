module TwoPoint.Dependencies.Tests.GitHubDispatchTests

open TwoPoint.Core.Deploys
open TwoPoint.Dependencies.Deploys
open TwoPoint.Dependencies.Util

open Azure.Security.KeyVault.Keys
open Azure.Security.KeyVault.Keys.Cryptography
open Expecto
open Microsoft.Extensions.Logging.Abstractions

open System
open System.Net
open System.Net.Http
open System.Text
open System.Threading
open System.Threading.Tasks

type private FakeSigner() =
  inherit CryptographyClient()

  override _.SignDataAsync(algorithm : SignatureAlgorithm, _ : byte array, _ : CancellationToken) =
    Task.FromResult(CryptographyModelFactory.SignResult("https://vault/keys/app", [| 1uy |], algorithm))

type private FakeGitHub(run : HttpStatusCode * string) =
  inherit HttpMessageHandler()
  let requests = ResizeArray<string>()

  member _.Requests = List.ofSeq requests

  override _.SendAsync(request, _) =
    let path = $"{request.Method} {request.RequestUri.AbsolutePath}"
    requests.Add path

    let status, body =
      match path with
      | "GET /repos/tw0po1nt/twopoint-website/installation" -> HttpStatusCode.OK, """{"id":5}"""
      | "POST /app/installations/5/access_tokens" -> HttpStatusCode.Created, """{"token":"ghs_1"}"""
      | "POST /repos/tw0po1nt/twopoint-website/actions/workflows/site.yml/dispatches" ->
        HttpStatusCode.OK, """{"workflow_run_id":9,"run_url":"https://api.github.com/repos/tw0po1nt/twopoint-website/actions/runs/9"}"""
      | "GET /repos/tw0po1nt/twopoint-website/actions/runs/9" -> run
      | _ -> HttpStatusCode.NotFound, "{}"

    Task.FromResult(new HttpResponseMessage(status, Content = new StringContent(body, Encoding.UTF8, "application/json")))

let private repo = { Owner = "tw0po1nt"; Name = "twopoint-website" }
let private app = { ClientId = "Iv23client"; SigningKey = Uri "https://vault/keys/app" }

let private dispatch (github : FakeGitHub) =
  let dispatch = GitHubDispatch(new HttpClient(github), FakeSigner(), app, SystemDependencies.live, NullLogger.Instance)
  (dispatch.Dispatch repo CancellationToken.None).Result

let private dispatched =
  [ "GET /repos/tw0po1nt/twopoint-website/installation"
    "POST /app/installations/5/access_tokens"
    "POST /repos/tw0po1nt/twopoint-website/actions/workflows/site.yml/dispatches"
    "GET /repos/tw0po1nt/twopoint-website/actions/runs/9" ]

[<Tests>]
let tests =
  testList "GitHubDispatch" [
    test "a dispatch reads the run number of the queued run" {
      let github = new FakeGitHub((HttpStatusCode.OK, """{"run_number":42}"""))
      Expect.equal (dispatch github) (Ok (Queued (RunNumber 42L))) "Expected the run number"
      Expect.equal github.Requests dispatched "Expected the token, the dispatch, and the run"
    }

    test "a failed read of the run number keeps the queued dispatch" {
      let github = new FakeGitHub((HttpStatusCode.InternalServerError, "{}"))
      Expect.equal (dispatch github) (Ok QueuedWithoutRunNumber) "Expected a queued run with no run number"
      Expect.equal github.Requests dispatched "Expected the token, the dispatch, and the run"
    }
  ]
