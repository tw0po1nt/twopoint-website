namespace TwoPoint.Http.Endpoints.Admin

open System

type PushDeviceDto =
  { Id : string
    Label : string
    CreatedDate : DateTime
    LastSeenDate : DateTime }

[<AutoOpen>]
module PushDeviceExt =

  type TwoPoint.Core.Notifications.RegisteredDevice with
    member this.ToDto() =
      { PushDeviceDto.Id = this.Id.ToString()
        Label = this.Label.ToString()
        CreatedDate = this.CreatedDate
        LastSeenDate = this.LastSeenDate }
