import type { AstroIntegration } from 'astro';
import { fileURLToPath } from 'url';
import * as path from 'path';
import * as fs from 'fs/promises';
import juice from 'juice';

/**
 * Prepare a newsletter page for email clients. Many clients ignore <style> blocks and external stylesheets,
 * so this inlines the styles and drops the site stylesheets and scripts that Astro adds for post components.
 */
export function toEmailHtml(html: string): string {
  const withoutSiteAssets = html
    .replace(/<link[^>]+rel="stylesheet"[^>]+href="\/[^"]*"[^>]*>/g, '')
    .replace(/<link[^>]+href="\/[^"]*"[^>]+rel="stylesheet"[^>]*>/g, '')
    .replace(/<script\b[\s\S]*?<\/script>/g, '');
  return juice(withoutSiteAssets);
}

/**
 * Custom Astro integration to handle newsletter HTML generation
 * This integration:
 * 1. Generates newsletter HTML pages during build
 * 2. Inlines their styles and moves them to a separate directory (dist/newsletter)
 * 3. Removes them from the served static output
 */
export default function newsletterExportIntegration(): AstroIntegration {
  let distWebsiteDir: string;
  let distDir: string;
  
  return {
    name: 'newsletter-export',
    hooks: {
      'astro:config:done': ({ config }) => {
        distWebsiteDir = fileURLToPath(config.outDir);
        distDir = path.dirname(distWebsiteDir); 
      },
      'astro:build:done': async () => {
        const newsletterDir = path.join(distWebsiteDir, 'newsletter');
        const exportDir = path.join(distDir, 'newsletter');
        
        try {
          // Check if newsletter directory exists
          await fs.access(newsletterDir);
          
          console.log('📧 Processing newsletter HTML files...');
          
          // Create export directory
          await fs.mkdir(exportDir, { recursive: true });
          
          // Move all files from /dist/website/newsletter to /dist/newsletter
          const files = await fs.readdir(newsletterDir, { recursive: true });
          
          for (const file of files) {
            const sourcePath = path.join(newsletterDir, file);
            const stat = await fs.stat(sourcePath);
            
            if (stat.isFile()) {
              const targetPath = path.join(exportDir, file);
              const targetDir = path.dirname(targetPath);
              
              // Ensure target directory exists
              await fs.mkdir(targetDir, { recursive: true });
              
              // Write the email version of the file to the export directory
              if (file.endsWith('.html')) {
                await fs.writeFile(targetPath, toEmailHtml(await fs.readFile(sourcePath, 'utf8')));
              } else {
                await fs.copyFile(sourcePath, targetPath);
              }
              console.log(`  ✓ Exported: ${file}`);
            }
          }
          
          // Remove the newsletter directory from dist so it's not served
          await fs.rm(newsletterDir, { recursive: true, force: true });
          
          console.log(`✅ Newsletter HTML files exported to ${exportDir}`);
          console.log('   These files are NOT served as pages but available for upload');
          
        } catch (error: any) {
          if (error.code === 'ENOENT') {
            console.log('ℹ️  No newsletter directory found - skipping export');
          } else {
            console.error('❌ Error processing newsletter files:', error);
            throw error;
          }
        }
      },
    },
  };
}
