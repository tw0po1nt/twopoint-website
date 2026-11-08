namespace TwoPoint.Http.Endpoints

open TwoPoint.Core.Posts
open TwoPoint.Core.Util
open TwoPoint.Dependencies.Util

open Azure.Messaging.ServiceBus
open FsToolkit.ErrorHandling
open IcedTasks
open Microsoft.Extensions.Logging

open System.Text.Json

[<RequireQualifiedAccess>]
module ReplyEmailQueueClient =

  let enqueue (serviceBus : ServiceBusClient) (logger : ILogger) (op : string) (post : Slug) (reply : CommentId) =
    runDependencyWithLogging Dependency.ServiceBus logger $"{op}.QueueReplyEmail"
    <| cancellableTaskResult {
      let! ct = CancellableTask.getCancellationToken()
      use sender = serviceBus.CreateSender ReplyEmailQueue.Name
      let json = JsonSerializer.Serialize { ReplyEmailMessage.Post = Slug.value post; ReplyId = string reply }
      do! sender.SendMessageAsync(ServiceBusMessage json, ct)
    }
