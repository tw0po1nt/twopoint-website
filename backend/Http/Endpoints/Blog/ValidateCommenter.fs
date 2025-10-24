namespace TwoPoint.Http.Endpoints.Blog

open Azure.Communication.Email
open TwoPoint.Core.Posts.Api
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Posts.Logic
open TwoPoint.Core.Util
open TwoPoint.Http
open TwoPoint.Http.Endpoints

open Azure.Data.Tables
open FirebaseAdmin.Messaging
open Microsoft.Azure.Functions.Worker
open Microsoft.Azure.Functions.Worker.Http
open Microsoft.Extensions.Logging

open System.Net
open System.Threading

type CommenterValidationJson =
  { EmailAddress : string option
    Name : string option
    RedirectUri : string option }

type ValidateCommenter (
  config: Config,
  emailClient: EmailClient,
  messaging : FirebaseMessaging,
  logger: ILogger<PostComment>,
  tableServiceClient: TableServiceClient
) =
  
  [<Function("Blog-ValidateCommenter")>]
  member _.Run (
    [<HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "blog/commenters")>] req : HttpRequestData,
    ct : CancellationToken
  ) = task {
    let response = req.CreateResponse HttpStatusCode.OK
    logger.LogInformation("Processing 'Blog.ValidateCommenter' request")
    let validRedirectUris = config.ValidRedirectUris |> List.map _.Uri
    
    let! json = req.ReadFromJsonAsync<CommenterValidationJson>(ct)
    
    // Dependencies
    let postDependencies =
      PostDependencies.live
        validRedirectUris
        emailClient
        config.Azure.EmailSender
        messaging
        tableServiceClient
        logger
    let postActions = PostActions.withDependencies postDependencies
    
    let commenterValidation =
      { CommenterValidationDto.EmailAddress = json.EmailAddress |> Option.defaultValue ""
        Name = json.Name
        RedirectUri = json.RedirectUri |> Option.defaultValue "" }

    let! validateCommenterResult = ct |> postActions.ValidateCommenter commenterValidation    
    let apiResponse, statusCode =
      match validateCommenterResult with
      | Ok _ | Error (Logic (ValidateCommenterError.CommenterBanned _)) ->
        { Success = true; Message = None; Data = None }, HttpStatusCode.OK
      | Error (Logic (InvalidRedirect uri)) ->
        { Success = false; Message = Some $"Redirect uri '{uri}' is invalid"; Data = None }, HttpStatusCode.BadRequest
      | Error actionError when actionError.IsValidation ->
        { Success = false;  Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.BadRequest
      | Error actionError ->
        { Success = false; Message = Some (actionError.ToString()); Data = None }, HttpStatusCode.InternalServerError
      
    response.StatusCode <- statusCode
    do! response.WriteAsJsonAsync(apiResponse, ct)
    return response
  }
