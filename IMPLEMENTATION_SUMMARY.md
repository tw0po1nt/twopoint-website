# Email Newsletter Feature Implementation - Summary

This PR implements email-friendly blog post rendering and Azure Blob Storage integration as requested.

## What Was Implemented

### 1. Email-Friendly HTML Generation (`/frontend/src/pages/email/[slug].astro`)
- Creates an `/email/{slug}` route for each blog post
- Generates static HTML during build time
- Uses inline CSS for maximum email client compatibility
- Follows the same URL pattern as regular blog posts

### 2. Email Layout (`/frontend/src/layouts/EmailBlogLayout.astro`)
- Table-based layout for email compatibility
- Inline styles embedded in `<style>` tag
- Includes:
  - Post title, metadata (author, date, category)
  - Full post content with styled elements
  - Link back to web version
  - Tags footer

### 3. Azure Upload Script (`/frontend/scripts/upload-email-posts.js`)
- Uploads all email HTML files to Azure Blob Storage
- Uses `DefaultAzureCredential` for secure, passwordless authentication
- Automatically creates private container if it doesn't exist (no public access)
- Sets proper content types and cache headers
- Maps local paths to Azure blob paths: `email/{slug}.html` → `posts/{slug}`

### 4. Build Integration
- Added `npm run upload:email` script to package.json
- Added `npm run build:production` that builds and uploads in one command
- Updated `.github/workflows/astro-prod.yml` to automatically upload on production deployments

### 5. Documentation (`/EMAIL_SETUP.md`)
- Complete setup guide for Azure resources
- Instructions for creating Service Principal
- GitHub Secrets configuration
- Local development setup
- Troubleshooting guide

### 6. Testing (`/scripts/test-email-generation.sh`)
- Bash script to verify email HTML generation
- Checks for proper file structure
- Validates HTML content

## What You Need To Do

### Step 1: Set Up Azure Resources

```bash
# 1. Create storage account
az storage account create \
  --name YOUR_STORAGE_ACCOUNT_NAME \
  --resource-group YOUR_RESOURCE_GROUP \
  --location eastus \
  --sku Standard_LRS

# 2. Create Service Principal with Storage Blob Data Contributor role
az ad sp create-for-rbac \
  --name "github-actions-twopoint" \
  --role "Storage Blob Data Contributor" \
  --scopes /subscriptions/{subscription-id}/resourceGroups/{resource-group}/providers/Microsoft.Storage/storageAccounts/{storage-account}
```

The second command will output JSON with credentials you need for the next step.

### Step 2: Add GitHub Secrets

Go to your repository → Settings → Secrets and variables → Actions

Add these 5 secrets:
1. `AZURE_STORAGE_ACCOUNT_NAME` - Your storage account name
2. `AZURE_STORAGE_CONTAINER_NAME` - Container name (e.g., "email-posts") 
3. `AZURE_CLIENT_ID` - From `appId` in Service Principal output
4. `AZURE_TENANT_ID` - From `tenant` in Service Principal output
5. `AZURE_CLIENT_SECRET` - From `password` in Service Principal output

### Step 3: Test It

Once secrets are added, push to main branch or manually trigger the production workflow. The email HTML will be automatically uploaded to Azure.

Verify in Azure Portal or with:
```bash
az storage blob list --account-name YOUR_ACCOUNT --container-name email-posts
```

## URL Structure

- **Website**: `https://staging.twopoint.dev/posts/danger-lurks-in-system-linq-enumerable-or-default-functions`
- **Email HTML**: `https://{storage-account}.blob.core.windows.net/email-posts/posts/danger-lurks-in-system-linq-enumerable-or-default-functions`

## Local Testing

Due to network restrictions in the sandbox environment, the build couldn't complete fully, but in your production environment:

```bash
cd frontend
npm run build                # Generates email HTML in dist/email/
npm run upload:email         # Uploads to Azure (requires az login first)
```

Or run the test script:
```bash
./scripts/test-email-generation.sh
```

## Security Features

✅ **No hardcoded secrets** - All credentials via environment variables  
✅ **DefaultAzureCredential** - Industry best practice for Azure auth  
✅ **Least privilege** - Service Principal only has blob upload permission  
✅ **GitHub Secrets** - Encrypted at rest and in transit  
✅ **continue-on-error** - Upload failure won't break deployment  

## Next Steps

After merging and setting up Azure:
1. Test email HTML in various email clients (Gmail, Outlook, Apple Mail)
2. Adjust styling if needed in `EmailBlogLayout.astro`
3. Set up email sending service (e.g., SendGrid, Mailchimp) to use these HTML files
4. Consider adding email subscription management

## Questions?

See `EMAIL_SETUP.md` for detailed documentation including:
- Complete Azure setup guide
- Troubleshooting common issues
- Authentication methods
- Container configuration
- Testing procedures
