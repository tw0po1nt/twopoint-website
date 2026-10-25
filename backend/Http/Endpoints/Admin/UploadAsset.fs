namespace TwoPoint.Http.Endpoints.Admin

open TwoPoint.Core.Posts.Actions
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Posts
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open IcedTasks
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System
open System.IO
open System.Net
open System.Threading

type UploadedAssetDto = { Name : string; Url : string }

module UploadAssetBody =

  /// Reads one byte more than the limit, so that the action sees a file that is too large and the read stops there.
  let read (req : HttpRequestData) (ct : CancellationToken) =
    task {
      let limit = UploadAsset.Logic.maxBytes + 1
      use buffer = new MemoryStream()
      let chunk : byte[] = Array.zeroCreate 81920
      let mutable finished = false

      while not finished && int buffer.Length < limit do
        let! count = req.Body.ReadAsync(Memory<byte>(chunk, 0, min chunk.Length (limit - int buffer.Length)), ct)

        if count = 0 then finished <- true else buffer.Write(chunk, 0, count)

      return buffer.ToArray()
    }

type UploadAsset (
  logger : ILogger<UploadAsset>,
  assetsClient : AssetsClient
) =

  [<Function("Admin-Assets-Upload")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "internal/assets")>] req : HttpRequestData,
    context : FunctionContext,
    ct : CancellationToken
  ) =
    let op = "Admin.Assets.Upload"
    let httpContext = context.GetHttpContext() |> Option.ofObj
    let claimsPrincipal = httpContext |> Option.map _.User

    ct |> (
      Auth.runIfAuthorized logger req claimsPrincipal op
      <| fun _ -> cancellableTask {
        let! ct = CancellableTask.getCancellationToken()
        logger.LogInformation("Processing '{op}' request", op)
        let response = req.CreateResponse HttpStatusCode.OK

        let! bytes = UploadAssetBody.read req ct

        let assetStore = AssetStore(assetsClient, logger)
        let uploadAsset = UploadAsset.withDependencies {
          new UploadAsset.Dependencies with

          member _.WriteAsset name contentType content = assetStore.WriteAsset name contentType content
        }

        let! result = uploadAsset { Bytes = bytes }

        let apiResponse, statusCode =
          match result with
          | Ok(UploadAsset.Event.Uploaded(name, url)) ->
            { Success = true; Message = None; Data = Some { Name = name; Url = url.AbsoluteUri } }, HttpStatusCode.OK
          | Error(Logic UploadAsset.Error.UnsupportedType) ->
            { Success = false; Message = Some "Upload a JPEG, PNG, WebP, GIF, or PDF file"; Data = None },
            HttpStatusCode.UnsupportedMediaType
          | Error(Logic UploadAsset.Error.TooLarge) ->
            { Success = false; Message = Some "The file is larger than 25 MB"; Data = None },
            HttpStatusCode.RequestEntityTooLarge
          | Error actionError when actionError.IsValidation ->
            { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
          | Error actionError ->
            { Success = false; Message = Some(actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError

        response.StatusCode <- statusCode
        do! response.WriteAsJsonAsync(apiResponse, ct)
        return response
      }
    )
