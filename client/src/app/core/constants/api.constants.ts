import { environment } from '../../../environments/environment';

/**
 * Single source of truth for every API endpoint.
 * All services import from here — changing the base URL or a path
 * only needs to happen in one place.
 *
 * Usage in a service:
 *   this.http.get(API.workspaces.list())
 *   this.http.get(API.workspaces.byId(id))
 */
const base = environment.apiBase;  // e.g. '/api'

export const API = {

  auth: {
    register:  ()                    => `${base}/auth/register`,
    login:     ()                    => `${base}/auth/login`,
    refresh:   ()                    => `${base}/auth/refresh`,
    revoke:    ()                    => `${base}/auth/revoke`,
    me:        ()                    => `${base}/auth/me`,
    aiSettings: ()                   => `${base}/auth/me/ai-settings`,
  },

  workspaces: {
    list:      ()                    => `${base}/workspaces`,
    byId:      (id: string)          => `${base}/workspaces/${id}`,
    members:   (id: string)          => `${base}/workspaces/${id}/members`,
    member:    (wsId: string, uId: string) => `${base}/workspaces/${wsId}/members/${uId}`,
    endpoints: (wsId: string)        => `${base}/workspaces/${wsId}/endpoints`,
  },

  endpoints: {
    byId:            (id: string)    => `${base}/endpoints/${id}`,
    regenerateToken: (id: string)    => `${base}/endpoints/${id}/regenerate-token`,
    requests:        (id: string)    => `${base}/endpoints/${id}/requests`,
    rules:           (id: string)    => `${base}/endpoints/${id}/rules`,
    reorder:         (id: string)    => `${base}/endpoints/${id}/rules/reorder`,
  },

  requests: {
    byId:    (id: string)            => `${base}/requests/${id}`,
    analyze: (id: string)            => `${base}/requests/${id}/analyze`,
  },

  rules: {
    byId:    (id: string)            => `${base}/rules/${id}`,
    toggle:  (id: string)            => `${base}/rules/${id}/toggle`,
  },

} as const;
