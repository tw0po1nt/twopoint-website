namespace TwoPoint.Core.Posts

open TwoPoint.Core.Shared

open FsToolkit.ErrorHandling

open System

type DeviceRegistrationId = 
  private | DeviceRegistrationId of Guid

  override this.ToString() =
    let (DeviceRegistrationId drid) = this
    drid.ToString()
    
module DeviceRegistrationId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof DeviceRegistrationId}' must be a valid guid"
      DeviceRegistrationId
      
  let createMaybe = Option.traverseResult create


type UserExternalId = 
  private | UserExternalId of Guid

  override this.ToString() =
    let (UserExternalId ueid) = this
    ueid.ToString()
    
module UserExternalId =
  let create =
    Constrained.String.tryParse
      Guid.TryParseOption
      $"'{nameof UserExternalId}' must be a valid guid"
      UserExternalId
      
  let createMaybe = Option.traverseResult create
  

type Client = 
  | Android
  | IOS
  
  override this.ToString() =
    match this with
    | Android -> "android"
    | IOS -> "ios"
  
module Client =
  let create (client : string) = validation {
    match client.ToLower() with
    | "android" -> return Android
    | "ios" -> return IOS
    | _ -> return! Validation.error $"'{nameof client}' must be 'android' or 'ios'"
  }

type DeviceRegistration =
  { Id : DeviceRegistrationId
    UserExternalId : UserExternalId
    Client : Client
    Token : NonEmptyString
    CreatedDate : DateTime }
  
module DeviceRegistration =
  
  let create id userExternalId client token createdDate = validation {
    // todo: update all my *other* validation CE usages to use `and!` because I didn't read the documentation first
    let! id = DeviceRegistrationId.create id
    and! userExternalId = UserExternalId.create userExternalId
    and! client = Client.create client
    and! token = NonEmptyString.create (Some (nameof token)) token
    and! createdDate = DateTime.TryValidateUtc(createdDate, (nameof createdDate))
    
    return
      { Id = id
        UserExternalId = userExternalId
        Client = client
        Token = token
        CreatedDate = createdDate }
  }
