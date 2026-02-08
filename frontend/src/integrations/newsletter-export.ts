import type { AstroIntegration } from 'astro';
import { fileURLToPath } from 'url';
import * as path from 'path';
import * as fs from 'fs/promises';

/**
 * Custom Astro integration to handle newsletter HTML generation
 * This integration:
 * 1. Generates newsletter HTML pages during build
 * 2. Moves them to a separate directory (dist/newsletter-export)
 * 3. Removes them from the served static output
 */
export default function newsletterExportIntegration(): AstroIntegration {
  let distDir: string;
  
  return {
    name: 'newsletter-export',
    hooks: {
      'astro:config:done': ({ config }) => {
        distDir = fileURLToPath(config.outDir);
      },
      'astro:build:done': async () => {
        const newsletterDir = path.join(distDir, 'newsletter');
        const exportDir = path.join(distDir, 'newsletter-export');
        
        try {
          // Check if newsletter directory exists
          await fs.access(newsletterDir);
          
          console.log('📧 Processing newsletter HTML files...');
          
          // Create export directory
          await fs.mkdir(exportDir, { recursive: true });
          
          // Move all files from newsletter to newsletter-export
          const files = await fs.readdir(newsletterDir, { recursive: true });
          
          for (const file of files) {
            const sourcePath = path.join(newsletterDir, file);
            const stat = await fs.stat(sourcePath);
            
            if (stat.isFile()) {
              const targetPath = path.join(exportDir, file);
              const targetDir = path.dirname(targetPath);
              
              // Ensure target directory exists
              await fs.mkdir(targetDir, { recursive: true });
              
              // Copy file to export directory
              await fs.copyFile(sourcePath, targetPath);
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
