# Email Newsletter Setup for Blog Posts

This document describes the email newsletter feature that renders blog posts as email-friendly HTML and uploads them to Azure Blob Storage.

## Overview

The system automatically generates email-optimized HTML versions of all blog posts during production builds. These HTML files use inline CSS for maximum compatibility with email clients and are uploaded to Azure Blob Storage for use in email campaigns.

## Features

- **Email-friendly HTML**: All blog posts are rendered with inline CSS styles
- **Email client compatibility**: Tested formatting that works across major email clients
- **Automated uploads**: Production builds automatically upload to Azure Blob Storage
- **Secure authentication**: Uses Azure DefaultAzureCredential for secure, passwordless authentication
- **Same URL structure**: Email HTML is uploaded to match the website URL structure

## URL Structure

Blog posts follow the same URL pattern in Azure Blob Storage as they do on the website:

- Website: `https://staging.twopoint.dev/posts/danger-lurks-in-system-linq-enumerable-or-default-functions`
- Email HTML: `https://{storage-account}.blob.core.windows.net/{container}/posts/danger-lurks-in-system-linq-enumerable-or-default-functions`

## Setup Instructions

### 1. Azure Resources Setup

Create the required Azure resources:

```bash
# Create resource group (if needed)
az group create --name twopoint-resources --location eastus

# Create storage account
az storage account create \
  --name twopointstorage \
  --resource-group twopoint-resources \
  --location eastus \
  --sku Standard_LRS

# The container will be created automatically by the upload script
```

### 2. Authentication Setup

The upload script uses `DefaultAzureCredential` which supports multiple authentication methods:

#### For Local Development:

Use Azure CLI authentication:
```bash
az login
```

#### For GitHub Actions (Recommended):

Use a Service Principal with the following steps:

1. **Create a Service Principal:**
```bash
az ad sp create-for-rbac \
  --name "github-actions-twopoint" \
  --role "Storage Blob Data Contributor" \
  --scopes /subscriptions/{subscription-id}/resourceGroups/twopoint-resources/providers/Microsoft.Storage/storageAccounts/twopointstorage
```

This command will output JSON with the credentials you need. Save this output securely.

2. **Add GitHub Secrets** (see section 3 below)

### 3. GitHub Repository Secrets

Add these secrets to your GitHub repository:

**Navigation:** Repository → Settings → Secrets and variables → Actions → New repository secret

| Secret Name | Description | Where to Find It | Required |
|------------|-------------|------------------|----------|
| `AZURE_STORAGE_ACCOUNT_NAME` | Name of your Azure Storage account | The name you chose when creating the storage account | Yes |
| `AZURE_STORAGE_CONTAINER_NAME` | Container name for email HTML files (defaults to `email-posts` if not set) | Choose any valid container name | No |
| `AZURE_CLIENT_ID` | Service Principal Application (client) ID | From `appId` in the create-for-rbac output | Yes |
| `AZURE_TENANT_ID` | Azure AD Tenant ID | From `tenant` in the create-for-rbac output | Yes |
| `AZURE_CLIENT_SECRET` | Service Principal password/secret | From `password` in the create-for-rbac output | Yes |

**Example create-for-rbac output:**
```json
{
  "appId": "12345678-1234-1234-1234-123456789abc",     ← Use for AZURE_CLIENT_ID
  "displayName": "github-actions-twopoint",
  "password": "your-secret-here",                      ← Use for AZURE_CLIENT_SECRET
  "tenant": "87654321-4321-4321-4321-987654321xyz"    ← Use for AZURE_TENANT_ID
}
```

### 4. Verify Setup

After adding the secrets, the next production deployment will automatically upload email HTML to Azure. You can verify by:

1. Triggering a workflow run (push to main or manual trigger)
2. Checking the GitHub Actions log for the "Upload email HTML to Azure Blob Storage" step
3. Verifying files in Azure Storage using the Azure Portal or CLI:

```bash
az storage blob list \
  --account-name twopointstorage \
  --container-name email-posts \
  --output table
```

## Usage

### Local Development

Test email rendering locally:

```bash
# Build the site (includes email HTML generation)
cd frontend
npm run build

# Check generated email HTML
ls dist/email/

# Test upload (requires Azure CLI authentication)
npm run upload:email
```

### Production Deployment

Email HTML is automatically uploaded during production builds when deploying to the main branch. The GitHub Actions workflow:

1. Builds the Astro site (generates email HTML)
2. Runs the upload script to push email HTML to Azure
3. Deploys the main site to GitHub Pages

### Manual Upload

To manually trigger an upload:

```bash
cd frontend
npm run upload:email
```

## Email HTML Structure

Each blog post is rendered using the `EmailBlogLayout.astro` layout which:

- Uses inline CSS for styling (no external stylesheets)
- Employs table-based layout for email client compatibility
- Includes all post metadata (title, author, date, category, tags)
- Applies responsive styling that works on mobile and desktop email clients
- Provides a link back to the web version

## Styling Approach

The email renderer applies inline styles to all HTML elements:

- **Headings**: Bold, appropriately sized with margins
- **Paragraphs**: Proper line height and spacing
- **Links**: Blue color with underline
- **Code blocks**: Monospace font with background color
- **Lists**: Proper indentation and spacing
- **Tables**: Full borders and cell padding
- **Images**: Responsive with max-width

## Security Notes

- **No connection strings**: Never use connection strings; use DefaultAzureCredential instead
- **Least privilege**: Service Principal only has "Storage Blob Data Contributor" role
- **Secrets management**: All credentials stored in GitHub Secrets, never in code
- **Container access**: Container is private with no public access - only authenticated requests can read blobs

## Troubleshooting

### Upload fails with authentication error

Ensure you've set all required environment variables and the Service Principal has the correct role assignment.

### No HTML files found

The build must complete successfully before uploading. Check that `npm run build` creates files in `dist/email/`.

### Container not found

The upload script automatically creates the container if it doesn't exist. Ensure the Service Principal has permission to create containers.

## Email Client Testing

Before sending emails to subscribers, test the HTML in various email clients:

- Gmail (web, mobile)
- Outlook (desktop, web, mobile)
- Apple Mail (iOS, macOS)
- Yahoo Mail
- ProtonMail

Use services like Litmus or Email on Acid for comprehensive testing.

## Future Enhancements

Potential improvements to consider:

- Add email preview functionality to the website
- Implement newsletter subscription management
- Create email templates with different layouts
- Add automated email sending integration
- Track email open rates and engagement
