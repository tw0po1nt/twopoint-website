namespace TwoPoint.Http

open System
open TwoPoint.Core.Shared

type AzureConfig =
  { CommsResourceEndpoint : Uri
    EmailSender : EmailAddress
    EntraTenantId : Guid
    ManagedIdentityClientId : Guid
    TableStorageUri : Uri
    KeyVaultUri : Uri option }

type FirebaseConfig =
  { ServiceAccountJsonPath : string option
    ServiceAccountJsonFromKeyVault : string option }

type ValidRedirectUri =
  { Uri : Uri }

type Config =
  { Azure : AzureConfig
    Firebase : FirebaseConfig
    ValidRedirectUris : ValidRedirectUri list }
  
module Config =
  
  open TwoPoint.Http.Extensions    
  
  open Symbolica.Extensions.Configuration.FSharp
  
  let bind config =
    bind {
      let! azure = Bind.section "Azure" <| bind {
        let! commsResourceEndpoint = Bind.valueAt "CommsResourceEndpoint" (Bind.uri UriKind.Absolute)
        let! emailSender = Bind.valueAt "EmailSender" Bind.emailAddress
        let! entraTenantId = Bind.valueAt "EntraTenantId" Bind.guid
        let! managedIdentityClientId = Bind.valueAt "ManagedIdentityClientId" Bind.guid
        let! tableStorageUri = Bind.valueAt "TableStorageUri" (Bind.uri UriKind.Absolute)
        let! keyVaultUri = Bind.optValueAt "KeyVaultUri" (Bind.uri UriKind.Absolute)
        return
          { CommsResourceEndpoint = commsResourceEndpoint
            EmailSender = emailSender
            EntraTenantId = entraTenantId
            ManagedIdentityClientId = managedIdentityClientId
            TableStorageUri = tableStorageUri
            KeyVaultUri = keyVaultUri }
      }
      
      let! firebase = Bind.section "Firebase" <| bind {
        let! serviceAccountJsonPath = Bind.optValueAt "ServiceAccountJsonPath" Bind.string
        let! serviceAccountJsonFromKeyVault = Bind.optValueAt "ServiceAccountJsonFromKeyVault" Bind.string
        return
          { ServiceAccountJsonPath = serviceAccountJsonPath
            ServiceAccountJsonFromKeyVault = serviceAccountJsonFromKeyVault }
      }
      
      let bindValidRedirectUri =  bind {
        let! uri = Bind.valueAt "Uri" (Bind.uri UriKind.Absolute)
        return { Uri = uri }
      }
      
      let! validRedirectUris = Bind.section "ValidRedirectUris" (Bind.list bindValidRedirectUri)
      
      return { Azure = azure; Firebase = firebase; ValidRedirectUris = validRedirectUris }
    }
    |> Binder.eval config
