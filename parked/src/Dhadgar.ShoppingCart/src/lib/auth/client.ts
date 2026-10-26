import { createAuthClient } from '@dhadgar/shared-auth';

const GATEWAY_URL = import.meta.env.PUBLIC_GATEWAY_URL || 'http://localhost:5000';

export const authClient = createAuthClient({
  gatewayUrl: GATEWAY_URL,
});
