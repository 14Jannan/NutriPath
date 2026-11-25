import { lazy, Suspense, type ReactNode } from 'react';
import { BrowserRouter, Route, Routes } from 'react-router';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { Toaster } from 'sonner';
import { ApiError } from '@/api/client';
import { ThemeProvider, useTheme } from '@/providers/theme';
import { AuthProvider } from '@/providers/auth';
import { AdminLayout } from '@/layouts/AdminLayout';
import { FullPageSpinner, RequireAuth, RequirePermission } from '@/routes/guards';
import { LoginPage } from '@/features/auth/LoginPage';
import { AcceptInvitationPage, ForgotPasswordPage, NotFoundPage, SessionExpiredPage } from '@/features/auth/AccountPages';

const DashboardPage = lazy(() => import('@/features/dashboard/DashboardPage'));
const UsersPage = lazy(() => import('@/features/users/UsersPage'));
const AdministratorsPage = lazy(() => import('@/features/admins/AdministratorsPage'));
const RolesPage = lazy(() => import('@/features/admins/RolesPage'));
const ProvidersPage = lazy(() => import('@/features/ai/ProvidersPage'));
const KeysPage = lazy(() => import('@/features/ai/KeysPage'));
const ConfigurationPage = lazy(() => import('@/features/ai/ConfigurationPage'));
const UsagePage = lazy(() => import('@/features/ai/UsagePage'));
const PlaygroundPage = lazy(() => import('@/features/ai/PlaygroundPage'));
const KnowledgePage = lazy(() => import('@/features/knowledge/KnowledgePage'));
const KnowledgeDetailPage = lazy(() => import('@/features/knowledge/KnowledgeDetailPage'));
const EmbeddingsPage = lazy(() => import('@/features/knowledge/EmbeddingsPage'));
const FoodsPage = lazy(() => import('@/features/foods/FoodsPage'));
const UsdaPage = lazy(() => import('@/features/foods/UsdaPage'));
const JobsPage = lazy(() => import('@/features/operations/JobsPage'));
const HealthPage = lazy(() => import('@/features/operations/HealthPage'));
const AuditPage = lazy(() => import('@/features/audit/AuditPage'));
const ProfilePage = lazy(() => import('@/features/profile/ProfilePage'));
const PromptsPage = lazy(() => import('@/features/ai/PromptsPage'));
const EvaluationsPage = lazy(() => import('@/features/ai/EvaluationsPage'));
const FeedbackPage = lazy(() => import('@/features/quality/FeedbackPage'));
const SafetyPage = lazy(() => import('@/features/quality/SafetyPage'));
const SafetyDetailPage = lazy(() => import('@/features/quality/SafetyDetailPage'));
const TerminologyPage = lazy(() => import('@/features/knowledge/TerminologyPage'));
const FlagsPage = lazy(() => import('@/features/settings/FlagsPage'));
const SystemSettingsPage = lazy(() => import('@/features/settings/SystemSettingsPage'));
const PrivacyPage = lazy(() => import('@/features/privacy/PrivacyPage'));

export function createQueryClient() {
  return new QueryClient({
    defaultOptions: {
      queries: {
        staleTime: 30_000,
        refetchOnWindowFocus: false,
        // Permission, auth and validation errors won't fix themselves on retry.
        retry: (count, error) => !(error instanceof ApiError && error.status < 500) && count < 2,
      },
    },
  });
}

const queryClient = createQueryClient();

function ThemedToaster() {
  const { resolved } = useTheme();
  return <Toaster theme={resolved} position="top-right" richColors closeButton />;
}

const page = (permission: string | undefined, element: ReactNode) => (
  <RequirePermission permission={permission}>
    <Suspense fallback={<FullPageSpinner />}>{element}</Suspense>
  </RequirePermission>
);

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/login" element={<LoginPage />} />
      <Route path="/forgot-password" element={<ForgotPasswordPage />} />
      <Route path="/accept-invitation" element={<AcceptInvitationPage />} />
      <Route path="/session-expired" element={<SessionExpiredPage />} />
      <Route element={<RequireAuth><AdminLayout /></RequireAuth>}>
        <Route index element={page('dashboard.read', <DashboardPage />)} />
        <Route path="users" element={page('users.read', <UsersPage />)} />
        <Route path="administrators" element={page('admins.read', <AdministratorsPage />)} />
        <Route path="roles" element={page('admins.read', <RolesPage />)} />
        <Route path="ai/providers" element={page('ai.providers.read', <ProvidersPage />)} />
        <Route path="ai/keys" element={page('ai.providers.read', <KeysPage />)} />
        <Route path="ai/configuration" element={page('ai.providers.read', <ConfigurationPage />)} />
        <Route path="ai/usage" element={page('ai.usage.read', <UsagePage />)} />
        <Route path="ai/playground" element={page('ai.evaluate', <PlaygroundPage />)} />
        <Route path="ai/prompts" element={page('ai.providers.read', <PromptsPage />)} />
        <Route path="ai/evaluations" element={page('ai.evaluate', <EvaluationsPage />)} />
        <Route path="feedback" element={page('feedback.read', <FeedbackPage />)} />
        <Route path="safety" element={page('safety.read', <SafetyPage />)} />
        <Route path="safety/:id" element={page('safety.read', <SafetyDetailPage />)} />
        <Route path="terminology" element={page('knowledge.read', <TerminologyPage />)} />
        <Route path="settings/flags" element={page('system.monitor', <FlagsPage />)} />
        <Route path="settings/system" element={page('system.monitor', <SystemSettingsPage />)} />
        <Route path="privacy" element={page('privacy.requests.read', <PrivacyPage />)} />
        <Route path="knowledge" element={page('knowledge.read', <KnowledgePage />)} />
        <Route path="knowledge/:id" element={page('knowledge.read', <KnowledgeDetailPage />)} />
        <Route path="embeddings" element={page('knowledge.read', <EmbeddingsPage />)} />
        <Route path="foods" element={page('foods.read', <FoodsPage />)} />
        <Route path="foods/usda" element={page('foods.read', <UsdaPage />)} />
        <Route path="jobs" element={page('system.monitor', <JobsPage />)} />
        <Route path="health" element={page('system.monitor', <HealthPage />)} />
        <Route path="audit" element={page('audit.read', <AuditPage />)} />
        <Route path="profile" element={page(undefined, <ProfilePage />)} />
        <Route path="*" element={<NotFoundPage />} />
      </Route>
    </Routes>
  );
}

export function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <ThemeProvider>
        <BrowserRouter>
          <AuthProvider>
            <AppRoutes />
            <ThemedToaster />
          </AuthProvider>
        </BrowserRouter>
      </ThemeProvider>
    </QueryClientProvider>
  );
}
