namespace TwoPoint.Http

open System
open TwoPoint.Core.Shared

type AzureConfig =
  { CommsResourceEndpoint : Uri
    EmailSender : EmailAddress
    EntraTenantId : Guid
    ManagedIdentityClientId : Guid
    TableStorageUri : Uri
    ServiceBusUri : Uri
    KeyVaultUri : Uri option }

type FirebaseConfig =
  { ServiceAccountJsonPath : string option
    ServiceAccountJsonFromKeyVault : string option }

type ValidRedirectUri =
  { Uri : Uri }

type WelcomeConfig =
  { Enabled : bool
    NewsletterContainerUri : Uri
    UnsubscribeUri : Uri }

type Config =
  { Azure : AzureConfig
    Firebase : FirebaseConfig
    ValidRedirectUris : ValidRedirectUri list
    Welcome : WelcomeConfig option }

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
        let! serviceBusUri = Bind.valueAt "ServiceBusUri" (Bind.uri UriKind.Absolute)
        let! keyVaultUri = Bind.optValueAt "KeyVaultUri" (Bind.uri UriKind.Absolute)
        return
          { CommsResourceEndpoint = commsResourceEndpoint
            EmailSender = emailSender
            EntraTenantId = entraTenantId
            ManagedIdentityClientId = managedIdentityClientId
            TableStorageUri = tableStorageUri
            ServiceBusUri = serviceBusUri
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

      let! welcome = Bind.optSection "Welcome" <| bind {
        let! enabled = Bind.optValueAt "Enabled" Bind.bool
        let! newsletterContainerUri = Bind.valueAt "NewsletterContainerUri" (Bind.uri UriKind.Absolute)
        let! unsubscribeUri = Bind.valueAt "UnsubscribeUri" (Bind.uri UriKind.Absolute)
        return
          { Enabled = enabled |> Option.defaultValue false
            NewsletterContainerUri = newsletterContainerUri
            UnsubscribeUri = unsubscribeUri }
      }

      return
        { Azure = azure
          Firebase = firebase
          ValidRedirectUris = validRedirectUris
          Welcome = welcome }
    }
    |> Binder.eval config
