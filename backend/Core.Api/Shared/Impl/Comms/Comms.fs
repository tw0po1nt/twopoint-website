namespace TwoPoint.Core.Shared.Impl


open TwoPoint.Core.Shared
open TwoPoint.Core.Util

open FirebaseAdmin.Messaging
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
  
type PushNotification =
  { Title : NonEmptyString
    Body : NonEmptyString
    Recipient : NonEmptyString }
  
module PushNotification =
  
  let create title body recipient = validation {
    let! title = title |> NonEmptyString.create (Some (nameof title))
    let! body = body |> NonEmptyString.create (Some (nameof body))
    let! recipient = recipient |> NonEmptyString.create (Some (nameof recipient))
    return { Title = title; Body = body; Recipient = recipient }
  }

type ICommsService =
  abstract member SendEmail: message: EmailMessage -> CancellableTaskResult<OperationId, DependencyError>
  abstract member SendPushNotification: message: PushNotification -> CancellableTaskResult<OperationId, DependencyError>

[<RequireQualifiedAccess>]
module internal CommsService =
  
  open Azure
  open Azure.Communication.Email
  
  open System
  
  let private runComms logger op fn =
    runDependencyWithLogging Dependency.Communications logger op fn
  
  let live (emailClient: EmailClient) (messaging : FirebaseMessaging) tableServiceClient logger (sender: TwoPoint.Core.Shared.EmailAddress) =
    
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
            
          let! (dbEmail: DbEmail) = commsDb.CreateEmail email
          return dbEmail.OperationId
        }
        
      member this.SendPushNotification message =
        runComms logger $"{nameof ICommsService}.SendPushNotification"
        <| cancellableTaskResult {
          let! ct = CancellableTask.getCancellationToken()
          // Send the message
          let recipient = message.Recipient.ToString()
          let message = Message(
            Token = recipient,
            Notification = Notification(
              Title = message.Title.ToString(),
              Body = message.Body.ToString()
            )
          )
          
          let! operationId = messaging.SendAsync(message, ct)
          
          // Track the operation
          let push =
            { DbPush.Id = Guid.NewGuid.ToString() // todo: model as ISystemDependencies
              OperationId = operationId
              CreatedDate =  DateTime.UtcNow // todo: model as ISystemDependencies
              Recipient = recipient }
            
          let! dbPush = commsDb.CreatePush push
          return dbPush.OperationId
        }
    }
    
    
