namespace TwoPoint.Dependencies.Comms

open TwoPoint.Dependencies.Util
open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

/// `Content` is the HTML part. A message with no `PlainText` has only the HTML part.
type EmailMessage =
  { Subject : NonEmptyString
    Content : NonEmptyString
    PlainText : NonEmptyString option
    Recipient : EmailAddress }
  
module EmailMessage =
  
  let create subject content recipient = validation {
    let! subject = subject |> NonEmptyString.create (Some (nameof subject))
    let! content = content |> NonEmptyString.create (Some (nameof content))
    let! recipient = recipient |> EmailAddress.create
    return { Subject = subject; Content = content; PlainText = None; Recipient = recipient }
  }

  let createWithPlainText subject content plainText (recipient : EmailAddress) = validation {
    let! subject = subject |> NonEmptyString.create (Some (nameof subject))
    and! content = content |> NonEmptyString.create (Some (nameof content))
    and! plainText = plainText |> NonEmptyString.create (Some (nameof plainText))
    return { Subject = subject; Content = content; PlainText = Some plainText; Recipient = recipient }
  }
  
type ICommsService =
  abstract member SendEmail: message: EmailMessage -> CancellableTaskResult<OperationId, DependencyError>

[<RequireQualifiedAccess>]
module internal CommsService =
  
  open Azure
  open Azure.Communication.Email
  
  open System
  
  let private runComms logger op fn =
    runDependencyWithLogging Dependency.Communications logger op fn
  
  let live (emailClient: EmailClient) tableServiceClient logger (sender: TwoPoint.Core.Shared.EmailAddress) =
    
    let commsDb = CommsDb.live tableServiceClient logger
    
    { new ICommsService with
      
      member _.SendEmail message =
        runComms logger $"{nameof ICommsService}.SendEmail"
        <| cancellableTaskResult {
          let! ct = CancellableTask.getCancellationToken()
          // Send the message
          let recipient = message.Recipient.ToString()
          let! emailSendOperation = emailClient.SendAsync(
            wait = WaitUntil.Started,
            senderAddress = sender.ToString(),
            recipientAddress = recipient,
            subject = message.Subject.ToString(),
            htmlContent = message.Content.ToString(),
            plainTextContent = (message.PlainText |> Option.map NonEmptyString.value |> Option.toObj),
            cancellationToken = ct
          )
          
          let operationId, operationIdSource =
            match emailSendOperation.Id with
            | "NOT_SET" -> Guid.NewGuid().ToString(), "twopoint" // todo: model as ISystemDependencies
            | otherwise -> otherwise, "azure"
          
          // Track the operation
          let email =
            { DbEmail.OperationId = operationId
              OperationIdSource = operationIdSource
              CreatedDate =  DateTime.UtcNow // todo: model as ISystemDependencies
              Recipient = recipient
              Status = "sent"
              StatusReason = None }
            
          let! (dbEmail: DbEmail) = commsDb.CreateEmail email
          return dbEmail.OperationId
        }
    }
