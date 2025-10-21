namespace TwoPoint.Http

open System
open TwoPoint.Core.Shared

type AzureConfig =
  { CommsResourceEndpoint : Uri
    EmailSender : EmailAddress
    EntraTenantId : Guid
    ManagedIdentityClientId : Guid
    TableStorageUri : Uri }

type ValidRedirectUri =
  { Uri : Uri }
  
type Values = { AzureFunctionsEnvironment : string }

type Config =
  { Values : Values
    Azure : AzureConfig
    ValidRedirectUris : ValidRedirectUri list }
  
module Config =
  
  open TwoPoint.Http.Extensions    
  
  open Symbolica.Extensions.Configuration.FSharp
  
  let bind config =
    bind {
      let! values = Bind.section "Values" <| bind {
        let! azureFunctionsEnvironment = Bind.valueAt "AZURE_FUNCTIONS_ENVIRONMENT" Bind.string
        return { AzureFunctionsEnvironment = azureFunctionsEnvironment }
      }
      
      let! azure = Bind.section "Azure" <| bind {
        let! commsResourceEndpoint = Bind.valueAt "CommsResourceEndpoint" (Bind.uri UriKind.Absolute)
        let! emailSender = Bind.valueAt "EmailSender" Bind.emailAddress
        let! entraTenantId = Bind.valueAt "EntraTenantId" Bind.guid
        let! managedIdentityClientId = Bind.valueAt "ManagedIdentityClientId" Bind.guid
        let! tableStorageUri = Bind.valueAt "TableStorageUri" (Bind.uri UriKind.Absolute)
        return
          { CommsResourceEndpoint = commsResourceEndpoint
            EmailSender = emailSender
            EntraTenantId = entraTenantId
            ManagedIdentityClientId = managedIdentityClientId
            TableStorageUri = tableStorageUri }
      }
      
      let bindValidRedirectUri =  bind {
        let! uri = Bind.valueAt "Uri" (Bind.uri UriKind.Absolute)
        return { Uri = uri }
      }
      
      let! validRedirectUris = Bind.section "ValidRedirectUris" (Bind.list bindValidRedirectUri)
      
      return { Values = values; Azure = azure; ValidRedirectUris = validRedirectUris }
    }
    |> Binder.eval config
