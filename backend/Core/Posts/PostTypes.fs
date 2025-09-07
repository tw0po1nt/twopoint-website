namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling

open System
      
type CommenterStatus =
  | New
  | Trusted
  | Banned

module CommenterStatus =
  let create (value : string) =
    match value.ToLower() with
    | "new" -> Ok New
    | "trusted" -> Ok Trusted
    | "banned" -> Ok Banned
    | _ -> Error $"'{nameof CommenterStatus}' must be one of 'new', 'trusted', or 'banned'"
    

type Commenter =
  { EmailAddress : EmailAddress
    Name : NonEmptyString option
    CreatedDate : DateTime
    Status : CommenterStatus }
  
module Commenter =
  let create emailAddress name createdDate status = validation {
    let! emailAddress = EmailAddress.create emailAddress
    let! name = NonEmptyString.createMaybe (Some (nameof name)) name
    let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    let! status = CommenterStatus.create status
    return { Name = name; EmailAddress = emailAddress; CreatedDate = createdDate; Status = status }
  }
  
  
type CommentId =
  private | CommentId of Guid
  
  override this.ToString() =
    let (CommentId cid) = this
    cid.ToString()

module CommentId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof CommentId}' must be a valid guid"
      CommentId
      
      
type CommentApproval =
  | New
  | Approved
  | Rejected
  
module CommentApproval =
  let create (value : string) =
    match value.ToLower() with
    | "new" -> Ok New
    | "approved" -> Ok Approved
    | "rejected" -> Ok Rejected
    | _ -> Error $"'{nameof CommentApproval}' must be one of 'new', 'approved', or 'rejected'"


type CommentStatus =
  { Approval : CommentApproval
    CreatedDate : DateTime }
  
module CommentStatus =
  let create approval createdDate = validation {
    let! approval = CommentApproval.create approval
    let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    return { Approval = approval; CreatedDate = createdDate }
  }


type Comment =
  { Id : CommentId
    CreatedDate : DateTime
    Status : CommentStatus
    Commenter : Commenter
    Content : NonEmptyString }
  
module Comment =
  let create
    id
    createdDate
    (status:
      {| Approval : string
         CreatedDate : string
      |}
    )
    (commenter:
      {| EmailAddress : string
         Name : string option
         CreatedDate : string
         Status : string
      |}
    )
    content =
      validation {
        let! id = CommentId.create id
        let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
        let! status = CommentStatus.create status.Approval status.CreatedDate
        let! commenter = Commenter.create commenter.EmailAddress commenter.Name commenter.CreatedDate commenter.Status
        let! content = NonEmptyString.create (Some (nameof content)) content
        return { Id = id; CreatedDate = createdDate; Status = status; Commenter = commenter; Content = content }
      }
      
      
type Slug =
  private | Slug of string
  
  override this.ToString() =
    let (Slug slug) = this
    slug

module Slug =
  
  let create =
    Constrained.String.tryParse
      (regexParser @"^([a-z0-9]+(-[a-z0-9]+)*)$")
      "Slug is invalid"
      Slug
      
  let createMaybe = Option.traverseResult create
    
  let value (Slug slug) = slug
  
  module Unsafe =
    let create = Types.unsafeCreate create


type Post =
  { Title : NonEmptyString
    Slug : Slug
    CreatedDate : DateTime }
    

module Post =
  
  let create title slug createdDate = validation {
    let! title = title |> NonEmptyString.create (Some (nameof title))
    let! slug = Slug.create slug
    let! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    return { Title = title; Slug = slug; CreatedDate = createdDate }
  }
