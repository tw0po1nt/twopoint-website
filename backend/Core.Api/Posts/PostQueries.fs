namespace TwoPoint.Core.Posts.Api

open TwoPoint.Core.Posts
open TwoPoint.Core.Posts.Dependencies
open TwoPoint.Core.Util

open IcedTasks

type IPostQueries =
  abstract member GetAllPosts: unit -> CancellableTask<QueryResult<PostInfo list>>
  abstract member GetPostBySlug: slug: string -> CancellableTask<QueryResult<Post option>>
  abstract member GetCommentsForPost: slug: string -> CancellableTask<QueryResult<Comment list option>>

[<RequireQualifiedAccess>]
module PostQueries =
  
  open FsToolkit.ErrorHandling
  
  let withDependencies (postDependencies: IPostDependencies) =
    
    { new IPostQueries with
    
      member this.GetAllPosts () = cancellableTaskResult {
        let! allPosts =
          postDependencies.GetAllPosts()
          |> CancellableTaskResult.mapError QueryError.Dependency
          
        return allPosts
      }
      
      member this.GetPostBySlug slug = cancellableTaskResult {
        let! slug =
          slug
          |> Slug.create
          |> Result.mapError QueryError.Validation
        
        let! post =
          postDependencies.GetPostBySlug slug
          |> CancellableTaskResult.mapError QueryError.Dependency
          
        return post
      }
      
      member this.GetCommentsForPost slug = cancellableTaskResult {
        let! post = this.GetPostBySlug slug
        return!
          post
          |> Option.traverseCancellableTaskResult postDependencies.GetCommentsForPost
          |> CancellableTaskResult.mapError QueryError.Dependency
      }
    }
  
  
