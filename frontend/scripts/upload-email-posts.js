#!/usr/bin/env node

/**
 * Upload email HTML files to Azure Blob Storage
 * Uses DefaultAzureCredential for secure authentication
 */

import { BlobServiceClient } from '@azure/storage-blob';
import { DefaultAzureCredential } from '@azure/identity';
import { readdir, readFile } from 'fs/promises';
import { join, relative } from 'path';
import { fileURLToPath } from 'url';
import { dirname } from 'path';

const __filename = fileURLToPath(import.meta.url);
const __dirname = dirname(__filename);

// Configuration from environment variables
const storageAccountName = process.env.AZURE_STORAGE_ACCOUNT_NAME;
const containerName = process.env.AZURE_STORAGE_CONTAINER_NAME || 'email-posts';
const emailDistPath = join(__dirname, '../dist/email');

// Validate required environment variables
function validateConfig() {
  const missingVars = [];
  
  if (!storageAccountName) {
    missingVars.push('AZURE_STORAGE_ACCOUNT_NAME');
  }
  
  if (missingVars.length > 0) {
    console.error('❌ Missing required environment variables:');
    missingVars.forEach(varName => {
      console.error(`   - ${varName}`);
    });
    console.error('\nPlease set these variables before running the upload script.');
    console.error('\nFor local development:');
    console.error('  export AZURE_STORAGE_ACCOUNT_NAME=your-storage-account');
    console.error('\nFor GitHub Actions, add these as repository secrets.');
    process.exit(1);
  }
}

// Recursively find all HTML files in a directory
async function findHtmlFiles(dir, fileList = []) {
  const entries = await readdir(dir, { withFileTypes: true });
  
  for (const entry of entries) {
    const fullPath = join(dir, entry.name);
    
    if (entry.isDirectory()) {
      await findHtmlFiles(fullPath, fileList);
    } else if (entry.isFile() && entry.name.endsWith('.html')) {
      fileList.push(fullPath);
    }
  }
  
  return fileList;
}

// Upload a single file to Azure Blob Storage
async function uploadFileToBlob(blobClient, filePath, relativePath) {
  try {
    const fileContent = await readFile(filePath);
    
    // Set content type to HTML
    const uploadOptions = {
      blobHTTPHeaders: {
        blobContentType: 'text/html',
        blobCacheControl: 'public, max-age=3600'
      }
    };
    
    await blobClient.upload(fileContent, fileContent.length, uploadOptions);
    console.log(`  ✓ Uploaded: ${relativePath}`);
    return true;
  } catch (error) {
    console.error(`  ✗ Failed to upload ${relativePath}:`, error.message);
    return false;
  }
}

// Main upload function
async function uploadEmailPosts() {
  console.log('🚀 Starting Azure Blob Storage upload...\n');
  
  validateConfig();
  
  console.log(`📦 Storage Account: ${storageAccountName}`);
  console.log(`📁 Container: ${containerName}`);
  console.log(`📂 Source Directory: ${emailDistPath}\n`);
  
  try {
    // Create blob service client using DefaultAzureCredential
    // This supports multiple authentication methods in this order:
    // 1. Environment variables (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)
    // 2. Managed Identity (when running in Azure)
    // 3. Azure CLI credentials (for local development)
    // 4. Azure PowerShell credentials
    const credential = new DefaultAzureCredential();
    const blobServiceUrl = `https://${storageAccountName}.blob.core.windows.net`;
    const blobServiceClient = new BlobServiceClient(blobServiceUrl, credential);
    
    // Get container client (create if doesn't exist)
    const containerClient = blobServiceClient.getContainerClient(containerName);
    
    // Ensure container exists
    const containerExists = await containerClient.exists();
    if (!containerExists) {
      console.log(`📦 Creating container: ${containerName}`);
      await containerClient.create({
        access: 'none' // Private container - no public access
      });
    }
    
    // Find all HTML files
    console.log('🔍 Scanning for email HTML files...\n');
    const htmlFiles = await findHtmlFiles(emailDistPath);
    
    if (htmlFiles.length === 0) {
      console.log('⚠️  No HTML files found. Make sure the build completed successfully.');
      process.exit(1);
    }
    
    console.log(`📤 Uploading ${htmlFiles.length} file(s)...\n`);
    
    // Upload each file
    let successCount = 0;
    let failureCount = 0;
    
    for (const filePath of htmlFiles) {
      const relativePath = relative(emailDistPath, filePath);
      
      // Transform path: email/slug.html -> posts/slug
      const blobPath = relativePath
        .replace(/\.html$/, '')
        .replace(/\\/g, '/');
      
      const blobClient = containerClient.getBlockBlobClient(`posts/${blobPath}`);
      const success = await uploadFileToBlob(blobClient, filePath, blobPath);
      
      if (success) {
        successCount++;
      } else {
        failureCount++;
      }
    }
    
    // Summary
    console.log(`\n✅ Upload complete!`);
    console.log(`   Successful: ${successCount}`);
    
    if (failureCount > 0) {
      console.log(`   Failed: ${failureCount}`);
      process.exit(1);
    }
    
  } catch (error) {
    console.error('\n❌ Upload failed:', error.message);
    
    if (error.message.includes('DefaultAzureCredential')) {
      console.error('\n💡 Authentication failed. Please ensure one of the following:');
      console.error('   1. Azure CLI is installed and you are logged in (az login)');
      console.error('   2. Environment variables are set (AZURE_CLIENT_ID, AZURE_CLIENT_SECRET, AZURE_TENANT_ID)');
      console.error('   3. Running in Azure with Managed Identity enabled');
    }
    
    process.exit(1);
  }
}

// Run the upload
uploadEmailPosts();
