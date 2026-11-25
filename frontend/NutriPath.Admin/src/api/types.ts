// Shapes returned by the admin API (backend/NutriPath.Api/Controllers/Admin).

export interface AdminProfile {
  id: string;
  email: string;
  fullName: string;
  roles: string[];
  permissions: string[];
  mfaEnabled: boolean;
}

export interface SessionResponse {
  accessToken: string;
  accessTokenExpiresAtUtc: string;
  sessionExpiresAtUtc: string;
  profile: AdminProfile;
  recoveryCodes?: string[] | null;
}

export interface LoginStep {
  step: 'mfa' | 'mfa-enroll';
  ticket: string;
  enrollmentSecret?: string | null;
  otpAuthUri?: string | null;
}

export interface Paged<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface UserRow {
  id: string;
  fullName: string;
  email: string;
  roles: string[];
  emailVerified: boolean;
  status: 'Active' | 'Suspended' | 'Locked' | 'Unverified';
  createdAtUtc: string;
  lastLoginAtUtc: string | null;
  preferredLanguage: string | null;
  aiTokens30d: number;
}

export interface AuditEntry {
  id: number;
  atUtc: string;
  actorUserId: string | null;
  actorEmail: string | null;
  actorRoles: string | null;
  action: string;
  resourceType: string | null;
  resourceId: string | null;
  outcome: string;
  correlationId: string | null;
  summary: string | null;
  reason: string | null;
}

export interface ComponentHealth {
  name: string;
  status: 'Healthy' | 'Degraded' | 'Unavailable' | 'RateLimited' | 'Unknown';
  latencyMs: number | null;
  detail: string | null;
  checkedAtUtc: string;
}

export interface AiRuntimeDocument {
  provider?: string | null;
  geminiEnabled: boolean;
  groqEnabled: boolean;
  geminiModel?: string | null;
  geminiTemperature?: number | null;
  geminiMaxOutputTokens?: number | null;
  geminiThinkingLevel?: string | null;
  geminiTimeoutSeconds?: number | null;
  geminiMaxAttempts?: number | null;
  geminiMaxRetryDelaySeconds?: number | null;
  groqModel?: string | null;
  multilingualEnabled?: boolean | null;
  defaultLanguage?: string | null;
  supportedLanguages?: string[] | null;
  tokenLimit?: number | null;
  windowHours?: number | null;
}

export interface CredentialStatus {
  provider: string;
  source: 'Runtime' | 'Environment' | 'Missing' | 'Disabled';
  configured: boolean;
  hint: string | null;
  activeVersion: number | null;
  activatedAtUtc: string | null;
  lastValidatedAtUtc: string | null;
  updatedBy: string | null;
  encryptionAvailable: boolean;
  history: {
    id: string;
    version: number;
    hint: string;
    status: 'Active' | 'Inactive' | 'Revoked';
    createdAtUtc: string;
    activatedAtUtc: string | null;
    lastValidatedAtUtc: string | null;
    createdBy: string | null;
  }[];
}

export interface KnowledgeRow {
  id: string;
  title: string;
  category: string;
  organization: string;
  sourceUrl: string;
  language: string;
  publicationDate: string | null;
  version: number;
  status: 'Draft' | 'PendingReview' | 'Approved' | 'Rejected' | 'Archived';
  chunks: number;
  updatedAtUtc: string | null;
  reviewedAtUtc: string | null;
  reviewer: string | null;
  author: string | null;
  embedding: 'Indexed' | 'Partial' | 'Missing' | 'NotApplicable' | 'Unavailable';
  supersedesDocumentId: string | null;
}

export interface Job {
  id: string;
  type: string;
  status: 'Queued' | 'Running' | 'Succeeded' | 'Failed' | 'Cancelled';
  progress: number;
  attempts: number;
  error: string | null;
  result: string | null;
  createdAtUtc: string;
  startedAtUtc: string | null;
  finishedAtUtc: string | null;
  cancelRequested: boolean;
  createdBy: string | null;
}

export interface FoodRow {
  id: string;
  name: string;
  nameTamil: string | null;
  nameSinhala: string | null;
  category: string | null;
  source: string;
  servingSizeGrams: number;
  calories: number;
  proteinGrams: number;
  carbsGrams: number;
  fatGrams: number;
  fiberGrams: number;
  sugarGrams: number;
  sodiumMilligrams: number;
  allergens: string | null;
  allergenStatus: 'Unknown' | 'NoneDeclared' | 'Declared';
  isVerified: boolean;
  isArchived: boolean;
  lastUpdatedAtUtc: string;
}
