#r @"./Core/bin/Debug/net9.0/Core.dll"
#r @"./Core.Api/bin/Debug/net9.0/Core.Api.dll"

#r "nuget: Azure.Identity, 1.14.1"
#r "nuget: Azure.Data.Tables, 12.11.0"
#r "nuget: FSharp.Control.TaskSeq, 0.4.0"
#r "nuget: FsToolkit.ErrorHandling.IcedTasks, 5.0.1"
#r "nuget: IcedTasks, 0.11.8"
#r "nuget: Microsoft.Extensions.Logging.Console, 9.0.7"
#r "nuget: Tables.FSharp, 1.2.0"


// Azure Credential
open Azure.Identity
open TwoPoint.Core.Posts.Dependencies

let entraTenantId = "cdf89f94-8c88-48fc-b11c-dd24af4380b4"
let opts = DefaultAzureCredentialOptions()
opts.TenantId <- entraTenantId
let credential = DefaultAzureCredential(opts)

// Azure Table
open Azure.Data.Tables

let storageUri = "https://twopointwebsite.table.core.windows.net/"
let tableServiceClient = TableServiceClient(System.Uri storageUri, credential)

// Logging
open Microsoft.Extensions.Logging
open System.IO

let logLevel = LogLevel.Information
let logger =
  LoggerFactory
    .Create(fun builder ->
      builder
        .AddConsole()
        .SetMinimumLevel(logLevel)
        |> ignore
    )
    .CreateLogger(Path.GetFileName __SOURCE_FILE__)

// Execution helper
open TwoPoint.Core.Util
open IcedTasks
open System.Threading

let executeApi (api: 'input -> CancellableTask<ApiResult<'event list, 'error>>) input op =  
  let apiResult = (api input CancellationToken.None).Result
  
  match apiResult with
  | Ok events  -> printfn $"{op} succeeded with events: {events}"
  | Error errs -> printfn $"{op} failed with errors: {errs}"

// ========================================================================================
// POST API
// ========================================================================================
open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Api

let postDependencies = PostDependencies.live tableServiceClient logger

let postApi = PostApi.withDependencies postDependencies

let existingPost = Slug.Unsafe.create "functional-programming-more-than-just-a-coding-style"

/// <summary>
/// Fetch a TwoPoint blog post by slug
/// </summary>
let getPostBySlug slug =
  let postResult = (postDependencies.GetPostBySlug slug CancellationToken.None).Result
  
  match postResult with
  | Ok (Some post) -> printfn $"Post found: %A{post}"; Some post
  | Ok None        -> printfn "Post not found"; None
  | Error err      -> printfn $"Post lookup failed with error: {err.DebugMessage}"; None
  
  
/// <summary>
/// Fetch comments for a blog post
/// </summary>
let getCommentsForPost post =
  let commentsResult = (postDependencies.GetCommentsForPost post CancellationToken.None).Result
  
  match commentsResult with
  | Ok []       -> printfn $"No comments found for post: '{post.Title}'"; []
  | Ok comments -> printfn $"Found {List.length comments} comment(s) for post: '{post.Title}'"; comments
  | Error err   -> printfn $"Comment lookup failed with error: {err.DebugMessage}"; []
  
let newPost =
  { NewPostDto.Title = "Functional programming: more than just a coding style"
    Slug = "functional-programming-more-than-just-a-coding-style" }

/// <summary>
/// Execute the <c>createPost</c> API to create a new TwoPoint blog post
/// </summary>
let createPost newPost = executeApi postApi.CreatePost newPost "CreatePost"


let newComment =
  { NewCommentDto.Post = "functional-programming-more-than-just-a-coding-style"
    EmailAddress = "matt@twopoint.dev"
    Name = Some "Matt"
    Comment = "This is a test" }

/// <summary>
/// Execute the <c>postComment</c> API to create a new comment on a TwoPoint blog post
/// </summary>
let postComment newComment = executeApi postApi.PostComment newComment "PostComment"


let commentApprovalUpdate =
  { CommentApprovalUpdateDto.CommentId = "f9b7d9a7-b9b2-4d2f-80aa-daec513b8e21"
    Approval = CommentApproval.Approved.ToString().ToLower() }

/// <summary>
/// Execute the <c>postComment</c> API to create a new comment on a TwoPoint blog post
/// </summary>
let updateCommentApproval commentApprovalUpdate =
  executeApi postApi.UpdateCommentApproval commentApprovalUpdate "UpdateCommentApproval"

