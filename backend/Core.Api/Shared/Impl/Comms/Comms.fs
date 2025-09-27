namespace TwoPoint.Core.Shared.Impl


open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FsToolkit.ErrorHandling
open IcedTasks

type EmailMessage =
  { Subject : NonEmptyString
    Content : NonEmptyString
    Recipient : EmailAddress }
  
module EmailMessage =
  
  let create subject content recipient = validation {
    let! subject = subject |> NonEmptyString.create (Some (nameof subject))
    let! content = content |> NonEmptyString.create (Some (nameof content))
    let! recipient = recipient |> EmailAddress.create
    return { Subject = subject; Content = content; Recipient = recipient }
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
      
      member this.SendEmail message =
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
            
          let! dbEmail = commsDb.CreateEmail email
          return dbEmail.OperationId
        }
    }
    
    
