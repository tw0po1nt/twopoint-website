# Firebase Admin SDK Setup

This document explains how to configure Firebase Admin SDK authentication for both local development and Azure deployment.

## Overview

The Firebase Admin SDK requires service account credentials to authenticate. We've implemented a secure approach that works for:
- **Local Development**: Uses a local JSON file (excluded from source control)
- **Azure Deployment**: Uses Azure Key Vault to store credentials securely

## Local Development Setup

1. **Get your Firebase service account credentials**:
   - Go to the [Firebase Console](https://console.firebase.google.com/)
   - Select your project
   - Go to Project Settings > Service Accounts
   - Click "Generate New Private Key"
   - Save the downloaded JSON file as `service-account-key.json`

2. **Place the file in your project**:
   ```bash
   # From the repository root
   cp ~/Downloads/your-project-firebase-adminsdk-xxxxx.json backend/Http/service-account-key.json
   ```

3. **Verify configuration**:
   - The file is already configured in `local.settings.json`
   - The file pattern is already in `.gitignore` (will not be committed)

4. **Run your application**:
   ```bash
   cd backend/Http
   dotnet run
   ```

## Azure Deployment Setup

For Azure, credentials are stored in Azure Key Vault and accessed via Managed Identity.

### Step 1: Store Firebase Credentials in Key Vault

```bash
# Set your Key Vault name
KEY_VAULT_NAME="your-keyvault-name"

# Upload the service account JSON as a secret
# Note: Read the file content and store it as a secret value
az keyvault secret set \
  --vault-name $KEY_VAULT_NAME \
  --name "firebase-service-account-key" \
  --file ./backend/Http/service-account-key.json
```

### Step 2: Grant Managed Identity Access to Key Vault

The managed identity of your Azure Function App must have the "Key Vault Secrets User" role.

### Step 3: Configure Application Settings in Azure

Add these application settings to your Azure Function App via Azure Portal:
1. Go to your Function App in Azure Portal
2. Navigate to Configuration > Application Settings
3. Add these settings:
   - `Azure__KeyVaultUri`: `https://your-keyvault-name.vault.azure.net/`
   - `Firebase__ServiceAccountJsonFromKeyVault`: `firebase-service-account-key`

## Configuration Reference

### Local Development (`local.settings.json`)
```json
{
  "Firebase": {
    "ServiceAccountJsonPath": "./service-account-key.json"
  }
}
```

### Azure (Application Settings)
```json
{
  "Azure": {
    "KeyVaultUri": "https://your-keyvault-name.vault.azure.net/"
  },
  "Firebase": {
    "ServiceAccountJsonFromKeyVault": "firebase-service-account-key"
  }
}
```

## Troubleshooting

### Local Development Issues

**Error: Firebase service account file not found**
- Ensure `service-account-key.json` exists in `backend/Http/`
- Check that the path in `local.settings.json` is correct

### Azure Deployment Issues

**Error: Azure credential is required when using Key Vault**
- Ensure Managed Identity is enabled on your Function App
- Verify the Managed Identity has access to Key Vault

**Error: Access denied to Key Vault**
- Verify the Managed Identity has "Get" and "List" permissions for secrets
- Check that the Key Vault URI is correct in application settings

**Error: Secret not found**
- Verify the secret name matches what's configured in `Firebase__ServiceAccountJsonFromKeyVault`
- Check that the secret exists in Key Vault: `az keyvault secret show --vault-name $KEY_VAULT_NAME --name firebase-service-account-key`

## Security Best Practices

1. ✅ **Never commit** `service-account-key.json` to source control
2. ✅ **Rotate credentials** periodically by generating new service account keys
3. ✅ **Use Key Vault** for all production environments
4. ✅ **Grant minimal permissions** - only the secrets the app needs
5. ✅ **Monitor access** to Key Vault through Azure Monitor

