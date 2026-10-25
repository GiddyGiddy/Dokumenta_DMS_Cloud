// Generates src/environments/environment.ts for production builds.
// Usage: API_URL=https://<api>/api SPA_REDIRECT_URI=https://<swa-host> node scripts/write-env.mjs
import { writeFileSync } from 'node:fs';

const required = (name) => {
  const value = process.env[name];
  if (!value) {
    console.error(`Missing required environment variable ${name}`);
    process.exit(1);
  }
  return value;
};

const apiBase = required('API_URL').replace(/\/$/, '');
const apiURL = apiBase.endsWith('/api') ? apiBase : `${apiBase}/api`;
const redirectUri = required('SPA_REDIRECT_URI').replace(/\/$/, '');
const tenantId = process.env.ENTRA_TENANT_ID ?? '55215293-ec4f-4ae2-a3e8-f76555cb6175';
const clientId = process.env.ENTRA_CLIENT_ID ?? '17423049-7fff-4aec-86ef-35fa0c52cbba';
const apiScope = process.env.ENTRA_API_SCOPE ?? `api://${clientId}/access_as_user`;

const content = `export const environment = {
  apiURL: ${JSON.stringify(apiURL)},
  entra: {
    tenantId: ${JSON.stringify(tenantId)},
    spaClientId: ${JSON.stringify(clientId)},
    apiScope: ${JSON.stringify(apiScope)},
    redirectUri: ${JSON.stringify(redirectUri)},
  },
};
`;
writeFileSync(new URL('../src/environments/environment.ts', import.meta.url), content);
console.log('Wrote src/environments/environment.ts');
